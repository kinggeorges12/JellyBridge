using Jellyfin.Plugin.JellyBridge.BridgeModels;
using Jellyfin.Plugin.JellyBridge.JellyseerrModel;
using Jellyfin.Plugin.JellyBridge.JellyseerrModel.Server;
using Jellyfin.Plugin.JellyBridge.Utils;
using Jellyfin.Plugin.JellyBridge.Configuration;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JellyBridge.Services;

/// <summary>
/// Service for discovering and scanning library items between Jellyfin and Jellyseerr.
/// </summary>
public class DiscoverService
{
    private readonly DebugLogger<DiscoverService> _logger;
    private readonly PlaceholderVideoGenerator _placeholderVideoGenerator;
    private readonly ApiService _apiService;
    private readonly MetadataService _metadataService;

    public DiscoverService(ILogger<DiscoverService> logger, PlaceholderVideoGenerator placeholderVideoGenerator, ApiService apiService, MetadataService metadataService)
    {
        _logger = new DebugLogger<DiscoverService>(logger);
        _placeholderVideoGenerator = placeholderVideoGenerator;
        _apiService = apiService;
        _metadataService = metadataService;
    }
    
    #region FromJellyseerr

    #region Process
    

    /// <summary>
    /// Generic method to fetch discover data for all networks using the specified endpoint.
    /// </summary>
    /// <typeparam name="T">The Jellyseerr type (JellyseerrMovie or JellyseerrShow)</typeparam>
    /// <returns>List of items fetched from all networks</returns>
    public async Task<List<T>> FetchDiscoverMediaAsync<T>() where T : TmdbMediaResult, IJellyseerrItem
    {
        var config = Plugin.GetConfiguration();
        var networkMap = config?.NetworkMap ?? new List<JellyseerrNetwork>();
        var allItems = new HashSet<T>();
        
        foreach (var network in networkMap)
        {
            _logger.LogDebug("Fetching {MediaType} for network: {NetworkName} (ID: {NetworkId}, Country: {Country}, Priority: {DisplayPriority})", T.LibraryType, network.Name, network.Id, network.Country, network.DisplayPriority);
            
            // Add network Id and Country parameters to query parameters
            var networkParameters = new Dictionary<string, object> {
                ["watchRegion"] = network.Country,
                ["watchProviders"] = network.Id
            };
            JellyseerrEndpoint? endpoint = null;  
            if (typeof(T) == typeof(JellyseerrMovie)) {
                endpoint = JellyseerrEndpoint.DiscoverMovies;
            } else if (typeof(T) == typeof(JellyseerrShow)) {
                endpoint = JellyseerrEndpoint.DiscoverTv;
            }
            if (endpoint == null) {
                _logger.LogError("Unsupported type: {Type}", typeof(T));
                throw new InvalidOperationException($"Unsupported type: {typeof(T)}");
            }

            // Debug: Log the parameters being used
            _logger.LogTrace("Calling {Endpoint} with parameters: {Parameters}", 
                endpoint.Value, 
                string.Join(", ", networkParameters.Select(kvp => $"{kvp.Key}={kvp.Value}")));
            
            var networkData = await _apiService.CallEndpointAsync(endpoint.Value, parameters: networkParameters);
            
            if (networkData == null)
            {
                _logger.LogWarning("API call returned null for {MediaType} endpoint for network: {NetworkName}", T.LibraryType, network.Name);
                continue;
            }
            
            var items = (List<T>)networkData;
            
            // Debug: Log the response data
            _logger.LogTrace("API call returned {ItemCount} items for {NetworkName}", items.Count, network.Name);
            
            if (items.Count == 0)
            {
                _logger.LogWarning("No {MediaType} returned for network: {NetworkName}", T.LibraryType, network.Name);
            }
            
            // Add items to HashSet (automatically handles duplicates)
            foreach (var item in items)
            {
                // Set the network tag for this item
                item.NetworkTag = network.Name;
                item.NetworkId = network.Id;
                allItems.Add(item);
            }
            
            _logger.LogTrace("Retrieved {ItemCount} {MediaType} for {NetworkName}", items.Count, T.LibraryType, network.Name);
        }
        
        // Convert HashSet to List for return
        var result = allItems.ToList();
        _logger.LogDebug("Total unique {MediaType} after deduplication: {TotalCount}", T.LibraryType, result.Count);
        
        return result;
    }

    #endregion

    #region Created
    
    /// <summary>
    /// Creates placeholder videos for the provided unmatched items.
    /// </summary>
    public async Task<List<TJellyseerr>> CreatePlaceholderVideosAsync<TJellyseerr>(
        List<TJellyseerr> unmatchedItems) 
        where TJellyseerr : IJellyseerrItem
    {
        var processedItems = new List<TJellyseerr>();
        var tasks = new List<Task>();
        
        _logger.LogDebug("CreatePlaceholderVideosAsync: Processing {UnmatchedCount} unmatched items for placeholder creation", 
            unmatchedItems.Count);
        
        foreach (var item in unmatchedItems)
        {
            try
            {
                // Get the folder path for this item
                var folderPath = _metadataService.GetJellyseerrItemDirectory(item);
                
                if (string.IsNullOrEmpty(folderPath) || !Directory.Exists(folderPath))
                {
                    throw new InvalidOperationException($"Folder does not exist for item: {item.MediaName}");
                }
                
                // Create placeholder video based on media type
                if (item is JellyseerrMovie)
                {
                    tasks.Add(_placeholderVideoGenerator.GeneratePlaceholderMovieAsync(folderPath));
                }
                else if (item is JellyseerrShow)
                {
                    tasks.Add(_placeholderVideoGenerator.GeneratePlaceholderShowAsync(folderPath));
                }
                
                processedItems.Add(item);
                _logger.LogTrace("CreatePlaceholderVideosAsync: ✅ Created placeholder video for {ItemName}", item.MediaName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "CreatePlaceholderVideosAsync: ❌ ERROR creating placeholder video for {ItemName}", item.MediaName);
            }
        }
        
        // Await all placeholder video tasks
        await Task.WhenAll(tasks);
        
        _logger.LogDebug("CreatePlaceholderVideosAsync: Completed - Processed {ProcessedCount} items", 
            processedItems.Count);
        
        return processedItems;
    }

    /// <summary>
    /// Create season folders for all TV shows.
    /// Creates Season 01 through Season 12 folders with season placeholder videos for each show.
    /// </summary>
    public async Task CreateSeasonFoldersForShows(List<JellyseerrShow> shows)
    {
        _logger.LogDebug("CreateSeasonFoldersForShows: Starting season folder creation for {ShowCount} shows", shows.Count);
        
        foreach (var show in shows)
        {
            try
            {
                var showFolderPath = _metadataService.GetJellyseerrItemDirectory(show);
                
                _logger.LogTrace("CreateSeasonFoldersForShows: Creating season folders for show '{MediaName}' in '{ShowFolderPath}'", 
                    show.MediaName, showFolderPath);
                
                var seasonFolderName = "Season 00";
                var seasonFolderPath = Path.Combine(showFolderPath, seasonFolderName);
                
                try
                {
                    // Create season folder if it doesn't exist
                    if (!Directory.Exists(seasonFolderPath))
                    {
                        Directory.CreateDirectory(seasonFolderPath);
                        _logger.LogDebug("CreateSeasonFoldersForShows: Created season folder: '{SeasonFolderPath}'", seasonFolderPath);
                    }
                    
                    // Generate season placeholder video
                    var placeholderSuccess = await _placeholderVideoGenerator.GeneratePlaceholderSeasonAsync(seasonFolderPath);
                    if (placeholderSuccess)
                    {
                        _logger.LogDebug("CreateSeasonFoldersForShows: Created season placeholder for: '{SeasonFolderPath}'", seasonFolderPath);
                    }
                    else
                    {
                        _logger.LogWarning("CreateSeasonFoldersForShows: Failed to create season placeholder for: '{SeasonFolderPath}'", seasonFolderPath);
                    }
                    
                    _logger.LogTrace("CreateSeasonFoldersForShows: ✅ Created season folder for show '{MediaName}'", show.MediaName);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "CreateSeasonFoldersForShows: Error creating season folder for show '{MediaName}'", show.MediaName);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "CreateSeasonFoldersForShows: ❌ ERROR creating season folders for show '{MediaName}'", show.MediaName);
            }
        }
        
        _logger.LogDebug("CreateSeasonFoldersForShows: Completed season folder creation for {ShowCount} shows", shows.Count);
    }


    #endregion
    
    #region Deleted

    /// <summary>
    /// Cleans up metadata by removing items older than the specified number of days.
    /// </summary>
    public async Task<(List<JellyseerrMovie> deletedMovies, List<JellyseerrShow> deletedShows)> CleanupMetadataAsync()
    {
        var maxRetentionDays = Plugin.GetConfigOrDefault<int>(nameof(PluginConfiguration.MaxRetentionDays));
        var cutoffDate = DateTime.Now.AddDays(-maxRetentionDays);

        try
        {
            // Read all bridge folder metadata
            var (movies, shows) = await _metadataService.ReadMetadataAsync();
            
            _logger.LogDebug("CleanupMetadataAsync: Found {MovieCount} movies and {ShowCount} shows to check for cleanup", 
                movies.Count, shows.Count);

            // Process movies and shows using the same logic
            var deletedMovies = ProcessItemsForCleanup(movies);
            var deletedShows = ProcessItemsForCleanup(shows);

            _logger.LogDebug("CleanupMetadataAsync: Completed cleanup - Deleted {MovieCount} movies, {ShowCount} shows", 
                deletedMovies.Count, deletedShows.Count);
            
            return (deletedMovies, deletedShows);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "CleanupMetadataAsync: Error during cleanup process");
            return (new List<JellyseerrMovie>(), new List<JellyseerrShow>());
        }
    }

    /// <summary>
    /// Validates that the NetworkId exists in the NetworkMap configuration.
    /// </summary>
    private bool ValidateNetworkId(IJellyseerrItem item)
    {
        // If no NetworkId is set, delete the item
        if (!item.NetworkId.HasValue)
        {
            return false;
        }

        var networkMap = Plugin.GetConfigOrDefault<List<JellyseerrNetwork>>(nameof(PluginConfiguration.NetworkMap));
        
        // Check if the NetworkId exists in the NetworkMap
        return networkMap.Any(network => network.Id == item.NetworkId.Value);
    }

    /// <summary>
    /// Helper method to process items for cleanup.
    /// </summary>
    private List<TJellyseerr> ProcessItemsForCleanup<TJellyseerr>(
        List<TJellyseerr> items) 
        where TJellyseerr : TmdbMediaResult, IJellyseerrItem
    {
        var deletedItems = new List<TJellyseerr>();
        var maxRetentionDays = Plugin.GetConfigOrDefault<int>(nameof(PluginConfiguration.MaxRetentionDays));
        var cutoffDate = DateTimeOffset.Now.AddDays(-maxRetentionDays);
        var itemType = typeof(TJellyseerr).Name.ToLower().Replace("jellyseerr", "");
        
        _logger.LogTrace("ProcessItemsForCleanup: Processing {ItemCount} {ItemType}s for cleanup (older than {MaxRetentionDays} days, before {CutoffDate})", 
            items.Count, itemType, maxRetentionDays, cutoffDate.ToString("yyyy-MM-dd HH:mm:ss"));
        
        try
        {
            foreach (var item in items)
            {
                string deletionReason = "";

                // Validate NetworkId exists in NetworkMap configuration
                if (!ValidateNetworkId(item))
                {
                    deletionReason = $"NetworkId {item.NetworkId} not found in NetworkMap configuration";
                }
                // Check if the item's CreatedDate is older than the cutoff date
                // Treat null CreatedDate as very old (past cutoff date)
                else if (item.CreatedDate == null || item.CreatedDate.Value < cutoffDate)
                {
                    deletionReason = $"Created {item.CreatedDate?.ToString("yyyy-MM-dd HH:mm:ss") ?? "N/A"} is older than cutoff {cutoffDate:yyyy-MM-dd HH:mm:ss}";
                }

                if (!string.IsNullOrEmpty(deletionReason))
                {
                    var itemDirectory = _metadataService.GetJellyseerrItemDirectory(item);
                    
                    if (Directory.Exists(itemDirectory))
                    {
                        Directory.Delete(itemDirectory, true);
                        deletedItems.Add(item);
                        _logger.LogTrace("ProcessItemsForCleanup: ✅ Removed {ItemType} '{ItemName}' - {Reason}", 
                            itemType, item.MediaName, deletionReason);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ProcessItemsForCleanup: Error processing {ItemType}", itemType);
        }
        
        return deletedItems;
    }

    /// <summary>
    /// Recursively deletes all .ignore files from the Jellyseerr bridge directory.
    /// </summary>
    public Task<int> DeleteAllIgnoreFilesAsync()
    {
        var syncDirectory = Plugin.GetConfigOrDefault<string>(nameof(PluginConfiguration.LibraryDirectory));
        
        try
        {
            if (string.IsNullOrEmpty(syncDirectory) || !Directory.Exists(syncDirectory))
            {
                _logger.LogWarning("Sync directory not configured or does not exist: {SyncDirectory}", syncDirectory);
                return Task.FromResult(0);
            }

            _logger.LogTrace("Starting recursive deletion of .ignore files from: {SyncDirectory}", syncDirectory);
            
            var deletedCount = 0;
            var ignoreFiles = Directory.GetFiles(syncDirectory, ".ignore", SearchOption.AllDirectories);
            
            foreach (var ignoreFile in ignoreFiles)
            {
                try
                {
                    File.Delete(ignoreFile);
                    deletedCount++;
                    _logger.LogDebug("Deleted .ignore file: {IgnoreFile}", ignoreFile);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to delete .ignore file: {IgnoreFile}", ignoreFile);
                }
            }

            _logger.LogTrace("Completed deletion of .ignore files. Deleted {DeletedCount} files out of {TotalCount} found", 
                deletedCount, ignoreFiles.Length);
            
            return Task.FromResult(deletedCount);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during .ignore file deletion");
            return Task.FromResult(0);
        }
    }

    #endregion

    #endregion
}