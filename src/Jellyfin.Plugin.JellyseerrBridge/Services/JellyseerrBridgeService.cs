using Jellyfin.Plugin.JellyseerrBridge.Configuration;
using Jellyfin.Plugin.JellyseerrBridge.BridgeModels;
using Jellyfin.Plugin.JellyseerrBridge.JellyseerrModel;
using Jellyfin.Plugin.JellyseerrBridge.JellyseerrModel.Server;
using Microsoft.Extensions.Logging;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using Jellyfin.Data.Enums;
using MediaBrowser.Model.Entities;
using System.Text.Json;

namespace Jellyfin.Plugin.JellyseerrBridge.Services;

/// <summary>
/// Service for managing bridge folders and metadata.
/// </summary>
public class JellyseerrBridgeService
{
    private readonly ILogger<JellyseerrBridgeService> _logger;
    private readonly ILibraryManager _libraryManager;

    public JellyseerrBridgeService(ILogger<JellyseerrBridgeService> logger, ILibraryManager libraryManager)
    {
        _logger = logger;
        _libraryManager = libraryManager;
    }

    /// <summary>
    /// Get all existing items of a specific type from Jellyfin libraries.
    /// </summary>
    public Task<List<T>> GetExistingItemsAsync<T>() where T : BaseItem
    {
        var items = new List<T>();
        var syncDirectory = Plugin.GetConfigOrDefault<string>(nameof(PluginConfiguration.LibraryDirectory));
        
        _logger.LogInformation("[JellyseerrBridge] Sync directory: {SyncDirectory}", syncDirectory);

        try
        {
            // Get all libraries
            var libraries = _libraryManager.GetVirtualFolders();
            
            foreach (var library in libraries)
            {
                _logger.LogInformation("[JellyseerrBridge] Processing library: {LibraryName} (Type: {LibraryType})", 
                    library.Name, library.CollectionType);

                // Skip libraries that are actually the Jellyseerr sync directory
                // Check if any items in this library are located in the sync directory
                var sampleQuery = new InternalItemsQuery
                {
                    Recursive = true,
                    Limit = 10 // Check a few items to see if library contains sync directory items
                };
                
                var sampleItems = _libraryManager.GetItemsResult(sampleQuery).Items;
                var hasItemsInSyncDirectory = sampleItems.Any(item => 
                    JellyseerrFolderUtils.IsPathInSyncDirectory(item.Path));
                
                if (hasItemsInSyncDirectory)
                {
                    _logger.LogDebug("[JellyseerrBridge] Skipping library {LibraryName} - contains items in Jellyseerr sync directory", 
                        library.Name);
                    continue;
                }
                
                // Only scan libraries that are compatible with the target item type
                var libraryCollectionType = library.CollectionType?.ToString();
                if (!JellyfinTypeMapping.IsLibraryTypeCompatible<T>(libraryCollectionType))
                {
                    _logger.LogDebug("[JellyseerrBridge] Skipping library {LibraryName} - type {LibraryType} not compatible with {ItemType}", 
                        library.Name, library.CollectionType, typeof(T).Name);
                    continue;
                }

                // Get items from this library
                var query = new InternalItemsQuery
                {
                    IncludeItemTypes = new[] { JellyfinTypeMapping.GetBaseItemKind<T>() },
                    Recursive = true
                };

                var libraryItems = _libraryManager.GetItemsResult(query).Items.Cast<T>().ToList();
                
                // Filter out items that are located in the sync directory
                var filteredItems = libraryItems.Where(item => 
                    !JellyseerrFolderUtils.IsPathInSyncDirectory(item.Path)).ToList();
                
                items.AddRange(filteredItems);
                
                var excludedCount = libraryItems.Count - filteredItems.Count;
                if (excludedCount > 0)
                {
                    _logger.LogInformation("[JellyseerrBridge] Excluded {ExcludedCount} {ItemType} from library {LibraryName} (located in sync directory)", 
                        excludedCount, typeof(T).Name, library.Name);
                }
                
                _logger.LogInformation("[JellyseerrBridge] Found {ItemCount} {ItemType} in library {LibraryName}", 
                    filteredItems.Count, typeof(T).Name, library.Name);
            }

            _logger.LogInformation("[JellyseerrBridge] Total {ItemType} found across all libraries: {TotalCount}", 
                typeof(T).Name, items.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[JellyseerrBridge] Error getting existing {ItemType} from libraries", typeof(T).Name);
        }

        return Task.FromResult(items);
    }

    /// <summary>
    /// Filter items to exclude those that already exist in main libraries.
    /// </summary>
    public async Task<List<TJellyseerr>> FilterItemsAsync<TJellyseerr, TExisting>(List<TJellyseerr> items, string itemTypeName) 
        where TJellyseerr : TmdbMediaResult, IJellyseerrItem, IEquatable<TJellyseerr>
        where TExisting : BaseItem
    {
        var excludeFromMainLibraries = Plugin.GetConfigOrDefault<bool?>(nameof(PluginConfiguration.ExcludeFromMainLibraries)) ?? true;
        
        if (!excludeFromMainLibraries)
        {
            _logger.LogInformation("[JellyseerrBridge] ExcludeFromMainLibraries is disabled - returning all {ItemCount} {ItemType}", items.Count, itemTypeName);
            return items;
        }

        var existingItems = await GetExistingItemsAsync<TExisting>();
        _logger.LogInformation("[JellyseerrBridge] Found {ExistingItemCount} existing {ItemType} in main libraries", existingItems.Count, itemTypeName);

        var filteredItems = new List<TJellyseerr>();
        int skippedCount = 0;

        foreach (var item in items)
        {
            if (existingItems.Any(existing => item.Equals(existing)))
            {
                _logger.LogInformation("[JellyseerrBridge] Skipping {ItemType} {ItemMediaName} - already exists in main libraries", itemTypeName, item.MediaName);
                skippedCount++;
                continue;
            }

            filteredItems.Add(item);
        }

        _logger.LogInformation("[JellyseerrBridge] Filtered {OriginalCount} {ItemType} to {FilteredCount} (skipped {SkippedCount})", 
            items.Count, itemTypeName, filteredItems.Count, skippedCount);

        return filteredItems;
    }

    /// <summary>
    /// Find matches between existing Jellyfin items and bridge metadata.
    /// </summary>
    public async Task<List<TJellyseerr>> FindMatches<TJellyfin, TJellyseerr>(
        List<TJellyfin> existingItems, 
        List<TJellyseerr> bridgeMetadata) 
        where TJellyfin : BaseItem 
        where TJellyseerr : TmdbMediaResult, IJellyseerrItem, IEquatable<TJellyseerr>
    {
        var matches = new List<TJellyseerr>();
        var syncDirectory = Plugin.GetConfigOrDefault<string>(nameof(PluginConfiguration.LibraryDirectory));

        foreach (var existingItem in existingItems)
        {
            _logger.LogDebug("[JellyseerrBridge] Checking existing item: {ItemName} (Id: {ItemId})", 
                existingItem.Name, existingItem.Id);
            
            // Use the built-in IEquatable<TJellyfin> implementation
            var bridgeMatch = bridgeMetadata.FirstOrDefault(bm => 
            {
                var isMatch = bm.Equals(existingItem);
                _logger.LogDebug("[JellyseerrBridge] Comparing bridge item '{BridgeMediaName}' (Id: {BridgeId}) with existing item '{ExistingName}' (Id: {ExistingId}) - Match: {IsMatch}", 
                    bm.MediaName, bm.Id, existingItem.Name, existingItem.Id, isMatch);
                return isMatch;
            });

            if (bridgeMatch != null)
            {
                _logger.LogInformation("[JellyseerrBridge] Found match: '{BridgeMediaName}' (Id: {BridgeId}) matches '{ExistingName}' (Id: {ExistingId})", 
                    bridgeMatch.MediaName, bridgeMatch.Id, existingItem.Name, existingItem.Id);
                
                // Find the bridge folder directory for this item
                var bridgeFolderPath = await FindBridgeFolderPathAsync(syncDirectory, bridgeMatch);
                
                // Create .ignore file with Jellyfin item metadata
                if (!string.IsNullOrEmpty(bridgeFolderPath))
                {
                    await CreateIgnoreFileAsync(bridgeFolderPath, existingItem);
                }

                // Add the actual bridge model to matches
                matches.Add(bridgeMatch);
            }
        }

        _logger.LogInformation("[JellyseerrBridge] Found {MatchCount} matches between Jellyfin items and bridge metadata", matches.Count);
        return matches;
    }

    /// <summary>
    /// Find the bridge folder path for a given item.
    /// </summary>
    public async Task<string?> FindBridgeFolderPathAsync<TJellyseerr>(string baseDirectory, TJellyseerr item) 
        where TJellyseerr : TmdbMediaResult, IJellyseerrItem, IEquatable<TJellyseerr>
    {
        if (string.IsNullOrEmpty(baseDirectory) || !Directory.Exists(baseDirectory))
        {
            return null;
        }

        try
        {
            // Look for folders that might contain this item
            var directories = Directory.GetDirectories(baseDirectory, "*", SearchOption.AllDirectories);
            
            foreach (var directory in directories)
            {
                var metadataFile = Path.Combine(directory, "metadata.json");
                if (File.Exists(metadataFile))
                {
                    try
                    {
                        var json = await File.ReadAllTextAsync(metadataFile);
                        var metadata = JsonSerializer.Deserialize<TJellyseerr>(json);
                        
                        if (metadata != null && item.Equals(metadata))
                        {
                            return directory;
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "[JellyseerrBridge] Error reading metadata file: {MetadataFile}", metadataFile);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[JellyseerrBridge] Error finding bridge folder path for item: {ItemMediaName}", item.MediaName);
        }

        return null;
    }

    /// <summary>
    /// Create an ignore file for a Jellyfin item in the bridge folder.
    /// </summary>
    private async Task CreateIgnoreFileAsync(string bridgeFolderPath, BaseItem item)
    {
        try
        {
            var ignoreFilePath = Path.Combine(bridgeFolderPath, ".ignore");
            var itemJson = JsonSerializer.Serialize(item, new JsonSerializerOptions { WriteIndented = true });

            await File.WriteAllTextAsync(ignoreFilePath, itemJson);
            _logger.LogInformation("[JellyseerrBridge] Created ignore file for {ItemName} in {BridgeFolder}", item.Name, bridgeFolderPath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[JellyseerrBridge] Error creating ignore file for {ItemName}", item.Name);
        }
    }

    /// <summary>
    /// Read bridge folder metadata and return both movies and shows.
    /// </summary>
    public async Task<(List<JellyseerrMovie> movies, List<JellyseerrShow> shows)> ReadBridgeFolderMetadataAsync()
    {
        var syncDirectory = Plugin.GetConfigOrDefault<string>(nameof(PluginConfiguration.LibraryDirectory));

        if (string.IsNullOrEmpty(syncDirectory) || !Directory.Exists(syncDirectory))
        {
            _logger.LogWarning("[JellyseerrBridge] Sync directory not configured or does not exist: {SyncDirectory}", syncDirectory);
            return (new List<JellyseerrMovie>(), new List<JellyseerrShow>());
        }

        try
        {
            // Use the new folder managers for type-specific reading
            var movieManager = new JellyseerrFolderManager<JellyseerrMovie>(_logger, syncDirectory);
            var showManager = new JellyseerrFolderManager<JellyseerrShow>(_logger, syncDirectory);

            // Read both types in parallel
            var movieTask = movieManager.ReadMetadataAsync();
            var showTask = showManager.ReadMetadataAsync();

            await Task.WhenAll(movieTask, showTask);

            var movies = await movieTask;
            var shows = await showTask;

            _logger.LogInformation("[JellyseerrBridge] Read {MovieCount} movies and {ShowCount} shows from bridge folders", 
                movies.Count, shows.Count);

            return (movies, shows);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[JellyseerrBridge] Error reading bridge folder metadata");
            return (new List<JellyseerrMovie>(), new List<JellyseerrShow>());
        }
    }

    /// <summary>
    /// Test method to verify Jellyfin library scanning functionality by comparing existing items against bridge folder metadata.
    /// </summary>
    public async Task<List<IJellyseerrItem>> TestLibraryScanAsync()
    {
        var syncDirectory = Plugin.GetConfigOrDefault<string>(nameof(PluginConfiguration.LibraryDirectory));
        var excludeFromMainLibraries = Plugin.GetConfigOrDefault<bool?>(nameof(PluginConfiguration.ExcludeFromMainLibraries)) ?? true;

        try
        {
            _logger.LogInformation("Testing Jellyfin library scan functionality against bridge folder metadata...");
            
            // For test purposes, always scan for bridge items regardless of ExcludeFromMainLibraries setting
            // This allows users to see what bridge items exist even when the setting is disabled
            
            if (string.IsNullOrEmpty(syncDirectory) || !Directory.Exists(syncDirectory))
            {
                _logger.LogWarning("Sync directory not configured or does not exist: {SyncDirectory}", syncDirectory);
                return new List<IJellyseerrItem>();
            }

            // Get existing Jellyfin items
            var existingMovies = await GetExistingItemsAsync<Movie>();
            var existingShows = await GetExistingItemsAsync<Series>();

            // Read bridge folder metadata
            var (bridgeMovieMetadata, bridgeShowMetadata) = await ReadBridgeFolderMetadataAsync();

            // Compare and find matches
            var movieMatches = await FindMatches(existingMovies, bridgeMovieMetadata);
            var showMatches = await FindMatches(existingShows, bridgeShowMetadata);

            // Combine all bridge metadata into a single list
            var allBridgeItems = new List<IJellyseerrItem>();
            allBridgeItems.AddRange(bridgeMovieMetadata.Cast<IJellyseerrItem>());
            allBridgeItems.AddRange(bridgeShowMetadata.Cast<IJellyseerrItem>());

            _logger.LogInformation("Library scan test completed. Found {MovieCount} movies, {ShowCount} shows, {MatchCount} matches", 
                bridgeMovieMetadata.Count, bridgeShowMetadata.Count, movieMatches.Count + showMatches.Count);

            return allBridgeItems;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during library scan test");
            return new List<IJellyseerrItem>();
        }
    }
}

/// <summary>
/// Maps between Jellyfin item types, collection type strings, and BaseItemKind enums.
/// </summary>
public class JellyfinTypeMapping
{
    // Collection type string constants
    public const string MoviesCollectionType = "movies";
    public const string TvShowsCollectionType = "tvshows";

    // BaseItemKind constants
    public static readonly BaseItemKind MovieKind = BaseItemKind.Movie;
    public static readonly BaseItemKind SeriesKind = BaseItemKind.Series;

    public static bool IsLibraryTypeCompatible<T>(string? libraryCollectionType) where T : BaseItem
    {
        if (string.IsNullOrEmpty(libraryCollectionType))
            return false;

        // Check if the collection type string actually contains the relevant keywords
        return typeof(T) switch
        {
            Type t when t == typeof(Movie) => libraryCollectionType.Contains(MoviesCollectionType, StringComparison.OrdinalIgnoreCase),
            Type t when t == typeof(Series) => libraryCollectionType.Contains(TvShowsCollectionType, StringComparison.OrdinalIgnoreCase),
            _ => throw new NotSupportedException($"Unsupported item type: {typeof(T).Name}")
        };
    }

    public static BaseItemKind GetBaseItemKind<T>() where T : BaseItem
    {
        return typeof(T) switch
        {
            Type t when t == typeof(Movie) => MovieKind,
            Type t when t == typeof(Series) => SeriesKind,
            _ => throw new NotSupportedException($"Unsupported item type: {typeof(T).Name}")
        };
    }
}