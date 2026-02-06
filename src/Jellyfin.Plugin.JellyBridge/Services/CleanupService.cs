using Jellyfin.Plugin.JellyBridge.BridgeModels;
using Jellyfin.Plugin.JellyBridge.JellyseerrModel;
using Jellyfin.Plugin.JellyBridge.Utils;
using Jellyfin.Plugin.JellyBridge.Configuration;
using Microsoft.Extensions.Logging;
using System.IO;
using System.Linq;

namespace Jellyfin.Plugin.JellyBridge.Services;

/// <summary>
/// Service for cleaning up JellyBridge metadata and folders.
/// </summary>
public class CleanupService
{
    private readonly DebugLogger<CleanupService> _logger;
    private readonly MetadataService _metadataService;
    private readonly DiscoverService _discoverService;

    public CleanupService(ILogger<CleanupService> logger, MetadataService metadataService, DiscoverService discoverService)
    {
        _logger = new DebugLogger<CleanupService>(logger);
        _metadataService = metadataService;
        _discoverService = discoverService;
    }

    /// <summary>
    /// Cleans up metadata by removing items older than the specified number of days.
    /// Also deletes JellyBridge folders that do not contain a metadata.json file.
    /// Note: Items with invalid NetworkIds should be filtered and ignored in DiscoverService, not deleted here.
    /// </summary>
    public async Task<CleanupResult> CleanupMetadataAsync()
    {
        var result = new CleanupResult();
        var maxRetentionDays = Plugin.GetConfigOrDefault<int>(nameof(PluginConfiguration.MaxRetentionDays));
        var cutoffDate = DateTime.Now.AddDays(-maxRetentionDays);

        try
        {
            // Read all bridge folder metadata
            var (movies, shows) = await _metadataService.ReadMetadataAsync();
            
            _logger.LogDebug("Found {MovieCount} movies and {ShowCount} shows to check for cleanup", 
                movies.Count, shows.Count);

            // Combine movies and shows into a single list, then process together
            var allItems = new List<IJellyseerrItem>();
            allItems.AddRange(movies.Cast<IJellyseerrItem>());
            allItems.AddRange(shows.Cast<IJellyseerrItem>());

            // Process all items together - only delete items that are older than retention days
            var deletedItems = DeleteExpiredMedia(allItems);
            result.ItemsProcessed.AddRange(allItems);

            // Process folders with .nfo files and delete those without metadata.json
            var (processedMovies, processedShows, deletedMovies, deletedShows) = await FindMissingMetadataFolders();
            result.ItemsDeleted.AddRange(deletedItems);
            result.MoviesCleaned = deletedMovies.Count;
            result.ShowsCleaned = deletedShows.Count;

            _logger.LogDebug("Processed {ProcessedCount} folders with .nfo files, deleted {DeletedCount} folders without metadata.json", 
                processedMovies.Count + processedShows.Count, deletedMovies.Count + deletedShows.Count);

            // Run placeholder video creation and collect created items
            var processedPlaceholders = await CreateMissingPlaceholderVideosAsync(allItems);
            result.ItemsCreated.AddRange(processedPlaceholders);

            // Determine refresh plan
            var hasDeletedItems = result.ItemsDeleted.Count > 0 || result.ItemsCleaned > 0;
            var hasCreatedItems = result.ItemsCreated.Count > 0;
            if (hasDeletedItems || hasCreatedItems)
            {
                result.Refresh = new RefreshPlan
                {
                    CreateRefresh = hasCreatedItems,
                    RemoveRefresh = hasDeletedItems,
                    RefreshImages = false
                };
                _logger.LogDebug("Cleanup removed {DeletedCount} items, created {CreatedCount} placeholders, refresh plan set (CreateRefresh: {CreateRefresh}, RemoveRefresh: {RemoveRefresh}, RefreshImages: {RefreshImages})", 
                    result.ItemsDeleted.Count, processedPlaceholders.Count, result.Refresh.CreateRefresh, result.Refresh.RemoveRefresh, result.Refresh.RefreshImages);
            }
            
            result.Success = true;
            result.Message = "✅ Cleanup completed successfully";

            // Log the full output using CleanupResult's ToString()
            _logger.LogInformation("Missing data removed and placeholders rebuilt:\n{CleanupOutput}", result.ToString());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during cleanup process");
            result.Success = false;
            result.Message = $"❌ Cleanup failed: {ex.Message}";
        }

        return result;
    }

    /// <summary>
    /// Deletes expired media items that are older than the specified number of days.
    /// Note: Items with invalid NetworkIds are not processed here - they should be ignored in DiscoverService.
    /// </summary>
    private List<IJellyseerrItem> DeleteExpiredMedia(List<IJellyseerrItem> items)
    {
        var deletedItems = new List<IJellyseerrItem>();
        var maxRetentionDays = Plugin.GetConfigOrDefault<int>(nameof(PluginConfiguration.MaxRetentionDays));
        var cutoffDate = DateTimeOffset.Now.AddDays(-maxRetentionDays);
        
        _logger.LogTrace("Processing {ItemCount} items for cleanup (older than {MaxRetentionDays} days, before {CutoffDate})", 
            items.Count, maxRetentionDays, cutoffDate.ToString("yyyy-MM-dd HH:mm:ss"));
        
        foreach (var item in items)
        {
            try
            {
                // Only delete items that are older than the cutoff date
                // Treat null CreatedDate as very old (past cutoff date)
                if (item.CreatedDate == null || item.CreatedDate.Value < cutoffDate)
                {
                    var itemDirectory = _metadataService.GetJellyBridgeItemDirectory(item);
                    
                    if (Directory.Exists(itemDirectory))
                    {
                        var deletionReason = $"Created {item.CreatedDate?.ToString("yyyy-MM-dd HH:mm:ss") ?? "N/A"} is older than cutoff {cutoffDate:yyyy-MM-dd HH:mm:ss}";
                        Directory.Delete(itemDirectory, true);
                        deletedItems.Add(item);
                        _logger.LogTrace("✅ Removed {ItemType} '{ItemName}' - {Reason}", 
                            item.MediaType, item.MediaName, deletionReason);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing cleanup for item '{ItemName}' ({ItemType})", 
                    item.MediaName, item.MediaType);
                continue;
            }
        }
        
        return deletedItems;
    }

    /// <summary>
    /// Searches for folders containing movie.nfo or tvshow.nfo files, checks for metadata.json,
    /// and deletes folders without metadata.json files.
    /// </summary>
    private async Task<(List<string> processedMovies, List<string> processedShows, List<string> deletedMovies, List<string> deletedShows)> FindMissingMetadataFolders()
    {
        var processedMovies = new List<string>();
        var processedShows = new List<string>();
        var deletedMovies = new List<string>();
        var deletedShows = new List<string>();
        var syncDirectory = FolderUtils.GetBaseDirectory();

        var movieNfoFilename = JellyseerrMovie.GetNfoFilename();
        var showNfoFilename = JellyseerrShow.GetNfoFilename();
        var metadataFilename = IJellyseerrItem.GetMetadataFilename();
        
        try
        {
            _logger.LogTrace("Searching for folders with .nfo files in: {SyncDirectory}", syncDirectory);
            
            // Search for all movie.nfo and tvshow.nfo files recursively
            var movieNfoFiles = Directory.GetFiles(syncDirectory, movieNfoFilename, SearchOption.AllDirectories).ToList();
            var showNfoFiles = Directory.GetFiles(syncDirectory, showNfoFilename, SearchOption.AllDirectories).ToList();
            
            // Process movies and shows separately
            var (processedMoviesList, deletedMoviesList) = await DeleteExpiredMediaFolders(movieNfoFiles, movieNfoFilename, metadataFilename);
            var (processedShowsList, deletedShowsList) = await DeleteExpiredMediaFolders(showNfoFiles, showNfoFilename, metadataFilename);
            
            processedMovies.AddRange(processedMoviesList);
            processedShows.AddRange(processedShowsList);
            deletedMovies.AddRange(deletedMoviesList);
            deletedShows.AddRange(deletedShowsList);
            
            var totalDeleted = deletedMovies.Count + deletedShows.Count;
            if (totalDeleted > 0)
            {
                _logger.LogDebug("Processed {ProcessedCount} folders with .nfo files, deleted {DeletedCount} folders without metadata.json", 
                    processedMovies.Count + processedShows.Count, totalDeleted);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing folders with .nfo files");
        }
        
        return (processedMovies, processedShows, deletedMovies, deletedShows);
    }

    /// <summary>
    /// Processes folders that contain movie.nfo or tvshow.nfo files.
    /// Checks for metadata.json and deletes folders without it.
    /// Returns lists of folder paths (strings).
    /// </summary>
    private Task<(List<string> processed, List<string> deleted)> DeleteExpiredMediaFolders(
        List<string> nfoFiles, string nfoFilename, string metadataFilename)
    {
        var processedFolders = new List<string>();
        var deletedFolders = new List<string>();
        
        try
        {
            foreach (var nfoFile in nfoFiles)
            {
                try
                {
                    var folderPath = Path.GetDirectoryName(nfoFile);
                    if (string.IsNullOrEmpty(folderPath)) continue;
                    
                    var metadataFile = Path.Combine(folderPath, metadataFilename);
                    
                    // All folders with .nfo files are processed
                    if (File.Exists(metadataFile))
                    {
                        processedFolders.Add(folderPath);
                    }
                    else
                    {
                        // Folder has .nfo but no metadata.json - delete it
                        Directory.Delete(folderPath, true);
                        deletedFolders.Add(folderPath);
                        _logger.LogTrace("✅ Deleted folder with {NfoFile} but no metadata.json: {FolderPath}", nfoFilename, folderPath);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error processing folder: {FolderPath}", Path.GetDirectoryName(nfoFile));
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing folders with .nfo files");
        }
        
        return Task.FromResult((processedFolders, deletedFolders));
    }

    /// <summary>
    /// Filters out items with placeholders (by AssetExtension) and creates missing placeholders using DiscoverService.
    /// </summary>
    public async Task<List<IJellyseerrItem>> CreateMissingPlaceholderVideosAsync(List<IJellyseerrItem> items)
    {
        var itemsWithoutPlaceholder = new List<IJellyseerrItem>();
        foreach (var item in items)
        {
            try
            {
                var folderPath = _metadataService.GetJellyBridgeItemDirectory(item);
                if (string.IsNullOrEmpty(folderPath) || !Directory.Exists(folderPath))
                    continue;
                if (!Directory.GetFiles(folderPath, "*" + PlaceholderVideoGenerator.AssetExtension, SearchOption.AllDirectories).Any())
                {
                    _logger.LogTrace("No placeholder found for {ItemName} at {FolderPath}", item.MediaName, folderPath);
                    itemsWithoutPlaceholder.Add(item);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking placeholder for item: {ItemName}", item.MediaName);
                continue;
            }
        }

        var filteredItems = _discoverService.FilterIgnoredItems(itemsWithoutPlaceholder);
        var created = await _discoverService.CreatePlaceholderVideosAsync(filteredItems);
        return created;
    }

}