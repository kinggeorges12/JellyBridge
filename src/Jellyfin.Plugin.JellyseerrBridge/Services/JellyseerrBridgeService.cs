using Jellyfin.Plugin.JellyseerrBridge.Configuration;
using Jellyfin.Plugin.JellyseerrBridge.BridgeModels;
using Jellyfin.Plugin.JellyseerrBridge.JellyseerrModel;
using Jellyfin.Plugin.JellyseerrBridge.JellyseerrModel.Server;
using Jellyfin.Plugin.JellyseerrBridge.Serialization;
using Microsoft.Extensions.Logging;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using Jellyfin.Data.Enums;
using MediaBrowser.Model.Entities;
using MediaBrowser.Controller.Dto;
using System.Text.Json;

namespace Jellyfin.Plugin.JellyseerrBridge.Services;

/// <summary>
/// Service for managing bridge folders and metadata.
/// </summary>
public class JellyseerrBridgeService
{
    private readonly ILogger<JellyseerrBridgeService> _logger;
    private readonly ILibraryManager _libraryManager;
    private readonly IDtoService _dtoService;

    public JellyseerrBridgeService(ILogger<JellyseerrBridgeService> logger, ILibraryManager libraryManager, IDtoService dtoService)
    {
        _logger = logger;
        _libraryManager = libraryManager;
        _dtoService = dtoService;
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

                // Skip libraries that contain the Jellyseerr sync directory
                // Check if any of the library's monitored locations are in the sync directory
                var hasSyncDirectoryLocation = library.Locations?.Any(location => 
                    JellyseerrFolderUtils.IsPathInSyncDirectory(location)) == true;
                
                if (hasSyncDirectoryLocation)
                {
                    _logger.LogDebug("[JellyseerrBridge] Skipping library {LibraryName} - monitors Jellyseerr sync directory", 
                        library.Name);
                    continue;
                }
                
                // Only scan libraries that are compatible with the target item type
                if (!JellyfinTypeMapping.IsLibraryTypeCompatible<T>(library.CollectionType))
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
                
                // Debug: Log each individual item being read from Jellyfin library
                foreach (var item in libraryItems)
                {
                    var tmdbId = item.GetProviderId("Tmdb");
                    var tvdbId = item.GetProviderId("Tvdb");
                    var imdbId = item.GetProviderId("Imdb");
                    _logger.LogDebug("[JellyseerrBridge] Reading Jellyfin {ItemType}: '{ItemName}' (TMDB: {TmdbId}, TVDB: {TvdbId}, IMDB: {ImdbId})", 
                        typeof(T).Name, item.Name, tmdbId ?? "null", tvdbId ?? "null", imdbId ?? "null");
                }
                
                items.AddRange(libraryItems);
                
                _logger.LogInformation("[JellyseerrBridge] Found {ItemCount} {ItemType} in library {LibraryName}", 
                    libraryItems.Count, typeof(T).Name, library.Name);
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
        where TJellyseerr : TmdbMediaResult, IJellyseerrItem
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
        where TJellyseerr : TmdbMediaResult, IJellyseerrItem
    {
        var matches = new List<TJellyseerr>();
        var folderManager = new JellyseerrFolderManager<TJellyseerr>();

        foreach (var existingItem in existingItems)
        {
            _logger.LogDebug("[JellyseerrBridge] Checking existing item: {ItemName} (Id: {ItemId})", 
                existingItem.Name, existingItem.Id);
            
            // Use the custom EqualsItem implementation rather than Equals cause I don't trust compile-time resolution.
            var bridgeMatch = bridgeMetadata.FirstOrDefault(bm => 
            {
                var isMatch = bm.EqualsItem(existingItem);
                
                // Log detailed comparison info for debugging
                var jellyfinType = existingItem.GetType().Name;
                var tmdbId = existingItem.GetProviderId("Tmdb");
                var tvdbId = existingItem.GetProviderId("Tvdb");
                var imdbId = existingItem.GetProviderId("Imdb");
                _logger.LogInformation("[JellyseerrBridge] Comparing bridge item: {BridgeItem} (Type: {BridgeType}) with existing Jellyfin item: {JellyfinItem} (Type: {JellyfinType}, TMDB: {TmdbId}, TVDB: {TvdbId}, IMDB: {ImdbId}) - Match: {IsMatch}", 
                    bm.ToString(), bm.GetType().Name, existingItem, jellyfinType, tmdbId ?? "null", tvdbId ?? "null", imdbId ?? "null", isMatch);
                
                return isMatch;
            });

            if (bridgeMatch != null)
            {
                _logger.LogInformation("[JellyseerrBridge] Found match: '{BridgeMediaName}' (Id: {BridgeId}) matches '{ExistingName}' (Id: {ExistingId})", 
                    bridgeMatch.MediaName, bridgeMatch.Id, existingItem.Name, existingItem.Id);
                
                // Get the bridge folder path using the folder manager
                var bridgeFolderPath = folderManager.GetItemDirectory(bridgeMatch);
                
                // Create .ignore file with Jellyfin item metadata
                await CreateIgnoreFileAsync(bridgeFolderPath, existingItem);

                // Add the actual bridge model to matches
                matches.Add(bridgeMatch);
            }
        }

        _logger.LogInformation("[JellyseerrBridge] Found {MatchCount} matches between Jellyfin items and bridge metadata", matches.Count);
        return matches;
    }

    /// <summary>
    /// Create an ignore file for a Jellyfin item in the bridge folder.
    /// </summary>
    private async Task CreateIgnoreFileAsync(string bridgeFolderPath, BaseItem item)
    {
        try
        {
            var ignoreFilePath = Path.Combine(bridgeFolderPath, ".ignore");
            
            _logger.LogInformation("[JellyseerrBridge] Creating ignore file for {ItemName} (Id: {ItemId}) at {IgnoreFilePath}", 
                item.Name, item.Id, ignoreFilePath);
            
            // Use DtoService to get a proper BaseItemDto with all metadata
            var dtoOptions = new DtoOptions(); // Default constructor includes all fields
            var itemDto = _dtoService.GetBaseItemDto(item, dtoOptions);
            
            _logger.LogInformation("[JellyseerrBridge] Successfully created BaseItemDto for {ItemName} - DTO has {PropertyCount} properties", 
                item.Name, itemDto?.GetType().GetProperties().Length ?? 0);
            
            var itemJson = JsonSerializer.Serialize(itemDto, new JsonSerializerOptions {
                WriteIndented = true
            });
            
            _logger.LogInformation("[JellyseerrBridge] Successfully serialized {ItemName} to JSON - JSON length: {JsonLength} characters", 
                item.Name, itemJson?.Length ?? 0);

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
            var movieManager = new JellyseerrFolderManager<JellyseerrMovie>();
            var showManager = new JellyseerrFolderManager<JellyseerrShow>();

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
/// Maps between Jellyfin item types, collection type enums, and BaseItemKind enums.
/// </summary>
public class JellyfinTypeMapping
{
    // BaseItemKind constants
    public static readonly BaseItemKind MovieKind = BaseItemKind.Movie;
    public static readonly BaseItemKind SeriesKind = BaseItemKind.Series;

    public static bool IsLibraryTypeCompatible<T>(CollectionTypeOptions? libraryCollectionType) where T : BaseItem
    {
        if (!libraryCollectionType.HasValue)
            return false;

        // Check if the collection type is compatible with the target item type
        return typeof(T) switch
        {
            Type t when t == typeof(Movie) => libraryCollectionType.Value == CollectionTypeOptions.movies || libraryCollectionType.Value == CollectionTypeOptions.mixed,
            Type t when t == typeof(Series) => libraryCollectionType.Value == CollectionTypeOptions.tvshows || libraryCollectionType.Value == CollectionTypeOptions.mixed,
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