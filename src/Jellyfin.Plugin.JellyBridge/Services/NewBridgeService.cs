using Jellyfin.Plugin.JellyBridge.Configuration;
using Jellyfin.Plugin.JellyBridge.BridgeModels;
using Jellyfin.Plugin.JellyBridge.JellyseerrModel;
using Jellyfin.Plugin.JellyBridge.Utils;
using Jellyfin.Plugin.JellyBridge.JellyfinModels;
using Microsoft.Extensions.Logging;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Dto;
using System.Text.Json;

namespace Jellyfin.Plugin.JellyBridge.Services;

/// <summary>
/// Service for managing bridge folders and metadata (refactored version with only helper methods).
/// </summary>
public class NewBridgeService
{
    private readonly DebugLogger<NewBridgeService> _logger;
    private readonly JellyfinILibraryManager _libraryManager;
    private readonly JellyfinIDtoService _dtoService;
    private readonly MetadataService _metadataService;
    private readonly DiscoverService _discoverService;

    public NewBridgeService(ILogger<NewBridgeService> logger, JellyfinILibraryManager libraryManager, JellyfinIDtoService dtoService, MetadataService metadataService, DiscoverService discoverService)
    {
        _logger = new DebugLogger<NewBridgeService>(logger);
        _libraryManager = libraryManager;
        _dtoService = dtoService;
        _metadataService = metadataService;
        _discoverService = discoverService;
    }

    /// <summary>
    /// Test method to verify Jellyfin library scanning functionality by comparing existing items against bridge folder metadata.
    /// Returns matched items as JellyMatch objects and unmatched items as IJellyseerrItem lists.
    /// </summary>
    public async Task<(List<JellyMatch> matched, List<IJellyseerrItem> unmatched)> LibraryScanAsync(List<JellyseerrMovie> movies, List<JellyseerrShow> shows)
    {
        // Combine all items for processing
        var allItems = new List<IJellyseerrItem>();
        allItems.AddRange(movies.Cast<IJellyseerrItem>());
        allItems.AddRange(shows.Cast<IJellyseerrItem>());
        
        _logger.LogDebug("Excluding main libraries from JellyBridge");
        var syncDirectory = Plugin.GetConfigOrDefault<string>(nameof(PluginConfiguration.LibraryDirectory));
        var excludeFromMainLibraries = Plugin.GetConfigOrDefault<bool>(nameof(PluginConfiguration.ExcludeFromMainLibraries));

        if (excludeFromMainLibraries) {
            try
            {
                if (!Directory.Exists(syncDirectory))
                {
                    throw new InvalidOperationException($"Sync directory does not exist: {syncDirectory}");
                }

                _logger.LogTrace("Scanning Jellyfin library for {MovieCount} movies and {ShowCount} shows", movies.Count, shows.Count);

                // Get existing Jellyfin items
                var existingMovies = JellyfinHelper.GetExistingItems<Movie>(_libraryManager.Inner);
                var existingShows = JellyfinHelper.GetExistingItems<Series>(_libraryManager.Inner);

                // Find matches between Jellyfin items and bridge metadata
                var movieMatches = FindMatches(existingMovies, movies);
                var showMatches = FindMatches(existingShows, shows);

                // Combine all matches into a single list
                var allMatches = new List<JellyMatch>();
                allMatches.AddRange(movieMatches);
                allMatches.AddRange(showMatches);

                // Filter unmatched items by excluding matched ones
                var matchedIds = allMatches.Select(m => m.JellyseerrItem.Id).ToHashSet();
                var unmatchedItems = allItems.Where(item => !matchedIds.Contains(item.Id)).ToList();

                _logger.LogDebug("Library scan completed. Matched {MatchedMovieCount}/{MovieCount} movies + {MatchedShowCount}/{ShowCount} shows = {TotalCount} total. Unmatched: {UnmatchedCount} items", 
                    movieMatches.Count, movies.Count, showMatches.Count, shows.Count, allMatches.Count, unmatchedItems.Count);

                return (allMatches, unmatchedItems);
            }
            catch (MissingMethodException ex)
            {
                _logger.LogDebug(ex, "Using incompatible Jellyfin version. Skipping library scan");
                return (new List<JellyMatch>(), allItems);
            }
            catch (Exception ex)
            {
                    _logger.LogError(ex, "Error during library scan test");
            }
        } else {
            _logger.LogDebug("Including main libraries in JellyBridge");
            
            // Delete all existing .ignore files when including main libraries
            var deletedCount = await _discoverService.DeleteAllIgnoreFilesAsync();
            _logger.LogTrace("Deleted {DeletedCount} .ignore files from JellyBridge", deletedCount);
        } 
        return (new List<JellyMatch>(), allItems);
    }

    /// <summary>
    /// Find matches between existing Jellyfin items and bridge metadata.
    /// </summary>
    private List<JellyMatch> FindMatches<TJellyfin, TJellyseerr>(
        List<TJellyfin> existingItems, 
        List<TJellyseerr> bridgeMetadata) 
        where TJellyfin : BaseItem 
        where TJellyseerr : TmdbMediaResult, IJellyseerrItem
    {
        var matches = new List<JellyMatch>();

        foreach (var existingItem in existingItems)
        {
            _logger.LogDebug("Checking existing item: {ItemName} (Id: {ItemId})", 
                existingItem.Name, existingItem.Id);
            
            // Safety check: Skip items that are already in the Jellyseerr library directory
            if (string.IsNullOrEmpty(existingItem.Path) || 
                FolderUtils.IsPathInSyncDirectory(existingItem.Path))
            {
                _logger.LogDebug("Skipping item {ItemName} - already in Jellyseerr library directory: {ItemPath}", 
                    existingItem.Name, existingItem.Path);
                continue;
            }
            
            // Use the custom EqualsItem implementation rather than Equals cause I don't trust compile-time resolution.
            var bridgeMatch = bridgeMetadata.FirstOrDefault(bm => bm.EqualsItem(existingItem));

            if (bridgeMatch != null)
            {
                _logger.LogTrace("Found match: '{BridgeMediaName}' (Id: {BridgeId}) matches '{ExistingName}' (Id: {ExistingId})", 
                    bridgeMatch.MediaName, bridgeMatch.Id, existingItem.Name, existingItem.Id);
                
                matches.Add(new JellyMatch(bridgeMatch, existingItem));
            }
        }

        _logger.LogDebug("Found {MatchCount} matches between Jellyfin items and bridge metadata", matches.Count);
        return matches;
    }

    /// <summary>
    /// Create ignore files for matched items.
    /// </summary>
    public async Task CreateIgnoreFilesAsync(List<JellyMatch> matches)
    {
        var ignoreFileTasks = new List<Task>();

        foreach (var match in matches)
        {
            var bridgeFolderPath = _metadataService.GetJellyseerrItemDirectory(match.JellyseerrItem);
            var item = match.JellyfinItem;
            var ignoreFilePath = Path.Combine(bridgeFolderPath, ".ignore");
            
            try
            {
                _logger.LogTrace("Creating ignore file for {ItemName} (Id: {ItemId}) at {IgnoreFilePath}", 
                    item.Name, item.Id, ignoreFilePath);
                
                // Use DtoService to get a proper BaseItemDto with all metadata
                var dtoOptions = new DtoOptions(); // Default constructor includes all fields
                var itemDto = _dtoService.Inner.GetBaseItemDto(item, dtoOptions);
                
                _logger.LogTrace("Successfully created BaseItemDto for {ItemName} - DTO has {PropertyCount} properties", 
                    item.Name, itemDto?.GetType().GetProperties().Length ?? 0);
                
                var itemJson = JsonSerializer.Serialize(itemDto!, new JsonSerializerOptions {
                    WriteIndented = true
                });
                
                _logger.LogTrace("Successfully serialized {ItemName} to JSON - JSON length: {JsonLength} characters", 
                    item.Name, itemJson?.Length ?? 0);

                await File.WriteAllTextAsync(ignoreFilePath, itemJson);
                _logger.LogTrace("Created ignore file for {ItemName} in {BridgeFolder}", item.Name, bridgeFolderPath);
            }
            catch (MissingMethodException ex)
            {
                _logger.LogDebug(ex, "Using incompatible Jellyfin version. Writing empty ignore file for {ItemName}", item.Name);
                await File.WriteAllTextAsync(ignoreFilePath, "");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating ignore file for {ItemName}", item.Name);
            }
        }

        // Await all ignore file creation tasks
        await Task.WhenAll(ignoreFileTasks);
    }
}