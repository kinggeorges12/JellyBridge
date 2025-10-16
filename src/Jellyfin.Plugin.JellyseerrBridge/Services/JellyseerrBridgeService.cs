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
/// Result of a processing operation.
/// </summary>
public class ProcessResult
{
    public int Processed { get; set; }
    public int Created { get; set; }
    public int Updated { get; set; }
    public List<IJellyseerrItem> ItemsAdded { get; set; } = new();
    public List<IJellyseerrItem> ItemsUpdated { get; set; } = new();

    public override string ToString()
    {
        return $"Created: {Created}, Updated: {Updated}";
    }
}

/// <summary>
/// Service for managing bridge folders and metadata.
/// </summary>
public class JellyseerrBridgeService
{
    private readonly ILogger<JellyseerrBridgeService> _logger;
    private readonly ILibraryManager _libraryManager;
    private readonly IDtoService _dtoService;
    private readonly PlaceholderVideoGenerator _placeholderVideoGenerator;

    public JellyseerrBridgeService(ILogger<JellyseerrBridgeService> logger, ILibraryManager libraryManager, IDtoService dtoService, PlaceholderVideoGenerator placeholderVideoGenerator)
    {
        _logger = logger;
        _libraryManager = libraryManager;
        _dtoService = dtoService;
        _placeholderVideoGenerator = placeholderVideoGenerator;
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
                
                // Also skip libraries that appear to be the Jellyseerr library itself
                var isJellyseerrLibrary = library.Name?.Contains("Jellyseerr", StringComparison.OrdinalIgnoreCase) == true ||
                                        library.Name?.Contains("Bridge", StringComparison.OrdinalIgnoreCase) == true;
                
                _logger.LogDebug("[JellyseerrBridge] Library {LibraryName} locations: {Locations}, HasSyncDirectory: {HasSyncDirectory}, IsJellyseerrLibrary: {IsJellyseerrLibrary}", 
                    library.Name, string.Join(", ", library.Locations ?? new string[0]), hasSyncDirectoryLocation, isJellyseerrLibrary);
                
                if (hasSyncDirectoryLocation || isJellyseerrLibrary)
                {
                    _logger.LogInformation("[JellyseerrBridge] Skipping library {LibraryName} - monitors Jellyseerr sync directory or is Jellyseerr library", 
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
    /// Find matches between existing Jellyfin items and bridge metadata.
    /// </summary>
    public async Task<List<TJellyseerr>> IgnoreMatches<TJellyfin, TJellyseerr>(
        List<TJellyfin> existingItems, 
        List<TJellyseerr> bridgeMetadata) 
        where TJellyfin : BaseItem 
        where TJellyseerr : TmdbMediaResult, IJellyseerrItem
    {
        var matches = new List<TJellyseerr>();
        var folderManager = new JellyseerrFolderManager<TJellyseerr>();
        var ignoreFileTasks = new List<Task>();

        foreach (var existingItem in existingItems)
        {
            _logger.LogDebug("[JellyseerrBridge] Checking existing item: {ItemName} (Id: {ItemId})", 
                existingItem.Name, existingItem.Id);
            
            // Use the custom EqualsItem implementation rather than Equals cause I don't trust compile-time resolution.
            var bridgeMatch = bridgeMetadata.FirstOrDefault(bm => bm.EqualsItem(existingItem));

            if (bridgeMatch != null)
            {
                _logger.LogInformation("[JellyseerrBridge] Found match: '{BridgeMediaName}' (Id: {BridgeId}) matches '{ExistingName}' (Id: {ExistingId})", 
                    bridgeMatch.MediaName, bridgeMatch.Id, existingItem.Name, existingItem.Id);
                
                // Get the bridge folder path using the folder manager
                var bridgeFolderPath = folderManager.GetItemDirectory(bridgeMatch);
                
                // Add .ignore file creation task to the list
                ignoreFileTasks.Add(CreateIgnoreFileAsync(bridgeFolderPath, existingItem));

                // Add the actual bridge model to matches
                matches.Add(bridgeMatch);
            }
        }
        
        // Await all ignore file creation tasks at the end
        await Task.WhenAll(ignoreFileTasks);

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

        try
        {
            _logger.LogInformation("Testing Jellyfin library scan functionality against bridge folder metadata...");
            
            if (string.IsNullOrEmpty(syncDirectory) || !Directory.Exists(syncDirectory))
            {
                _logger.LogWarning("Sync directory not configured or does not exist: {SyncDirectory}", syncDirectory);
                return new List<IJellyseerrItem>();
            }

            // Read bridge folder metadata
            var (bridgeMovieMetadata, bridgeShowMetadata) = await ReadBridgeFolderMetadataAsync();

            // Use LibraryScanAsync to find matches
            var matchedItems = await LibraryScanAsync(bridgeMovieMetadata, bridgeShowMetadata);

            _logger.LogInformation("Library scan test completed. Found {MovieCount} movies, {ShowCount} shows, {MatchCount} matches", 
                bridgeMovieMetadata.Count, bridgeShowMetadata.Count, matchedItems.Count);

            // Return all bridge items (not just matches) for test display purposes
            var allBridgeItems = new List<IJellyseerrItem>();
            allBridgeItems.AddRange(bridgeMovieMetadata.Cast<IJellyseerrItem>());
            allBridgeItems.AddRange(bridgeShowMetadata.Cast<IJellyseerrItem>());

            return allBridgeItems;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during library scan test");
            return new List<IJellyseerrItem>();
        }
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

            _logger.LogInformation("Starting recursive deletion of .ignore files from: {SyncDirectory}", syncDirectory);
            
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

            _logger.LogInformation("Completed deletion of .ignore files. Deleted {DeletedCount} files out of {TotalCount} found", 
                deletedCount, ignoreFiles.Length);
            
            return Task.FromResult(deletedCount);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during .ignore file deletion");
            return Task.FromResult(0);
        }
    }

    /// <summary>
    /// Test method to verify Jellyfin library scanning functionality by comparing existing items against bridge folder metadata.
    /// </summary>
    public async Task<List<IJellyseerrItem>> LibraryScanAsync(List<JellyseerrMovie> movies, List<JellyseerrShow> shows)
    {
        var excludeFromMainLibraries = Plugin.GetConfigOrDefault<bool?>(nameof(PluginConfiguration.ExcludeFromMainLibraries)) ?? true;
        if (!excludeFromMainLibraries)
        {
            _logger.LogInformation("Including main libraries in JellyseerrBridge");
            
            // Delete all existing .ignore files when including main libraries
            var deletedCount = await DeleteAllIgnoreFilesAsync();
            _logger.LogInformation("Deleted {DeletedCount} .ignore files from JellyseerrBridge", deletedCount);
            
            return new List<IJellyseerrItem>();
        }
        _logger.LogInformation("Excluding main libraries from JellyseerrBridge");
        var syncDirectory = Plugin.GetConfigOrDefault<string>(nameof(PluginConfiguration.LibraryDirectory));

        try
        {
            _logger.LogInformation("Scanning Jellyfin library for {MovieCount} movies and {ShowCount} shows", movies.Count, shows.Count);
            
            // For test purposes, always scan for bridge items regardless of ExcludeFromMainLibraries setting
            // This allows users to see what bridge items exist even when the setting is disabled
            
            if (string.IsNullOrEmpty(syncDirectory) || !Directory.Exists(syncDirectory))
            {
                _logger.LogWarning("Sync directory not configured or does not exist: {SyncDirectory}", syncDirectory);
                return new List<IJellyseerrItem>();
            }

            // Get existing Jellyfin items
            var existingMoviesTask = GetExistingItemsAsync<Movie>();
            var existingShowsTask = GetExistingItemsAsync<Series>();

            // Compare and find matches
            var movieMatchesTask = IgnoreMatches(await existingMoviesTask, movies);
            var showMatchesTask = IgnoreMatches(await existingShowsTask, shows);

            // Wait for both to complete
            await Task.WhenAll(movieMatchesTask, showMatchesTask);
            var movieMatches = await movieMatchesTask;
            var showMatches = await showMatchesTask;

            // Combine all bridge metadata into a single list
            var matchedItems = new List<IJellyseerrItem>();
            matchedItems.AddRange(movieMatches);
            matchedItems.AddRange(showMatches);

            _logger.LogInformation("Library scan test completed. Matched {MatchedMovieCount}/{MovieCount} movies + {MatchedShowCount}/{ShowCount} shows = {TotalCount} total", 
                movieMatches.Count, movies.Count, showMatches.Count, shows.Count, matchedItems.Count);

            return matchedItems;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during library scan test");
            return new List<IJellyseerrItem>();
        }
    }

    /// <summary>
    /// Create folders and JSON metadata files for movies or TV shows using JellyseerrFolderManager.
    /// </summary>
    public async Task<ProcessResult> CreateFoldersAsync<TJellyseerr>(List<TJellyseerr> items) 
        where TJellyseerr : TmdbMediaResult, IJellyseerrItem
    {
        var result = new ProcessResult();
        
        // Get configuration values using centralized helper
        var baseDirectory = Plugin.GetConfigOrDefault<string>(nameof(PluginConfiguration.LibraryDirectory));
        
        _logger.LogInformation("[JellyseerrBridge] CreateFoldersAsync: Starting folder creation for {ItemType} - Base Directory: {BaseDirectory}, Items Count: {ItemCount}", 
            typeof(TJellyseerr).Name, baseDirectory, items.Count);
        
        // Create folder manager for this type
        var folderManager = new JellyseerrFolderManager<TJellyseerr>();
        
        foreach (var item in items)
        {
            try
            {
                result.Processed++;
                
                _logger.LogInformation("[JellyseerrBridge] CreateFoldersAsync: Processing item {ItemNumber}/{TotalItems} - MediaName: '{MediaName}', Id: {Id}, Year: '{Year}'", 
                    result.Processed, items.Count, item.MediaName, item.Id, item.Year);
                
                // Generate folder name and get directory path
                var folderName = folderManager.GetItemDirectory(item);
                var folderExists = Directory.Exists(folderName);

                _logger.LogInformation("[JellyseerrBridge] CreateFoldersAsync: Folder details - Name: '{FolderName}', Exists: {FolderExists}", 
                    folderName, folderExists);

                // Write metadata using folder manager
                var success = await folderManager.WriteMetadataAsync(item);
                
                if (success)
                {
                    if (folderExists)
                    {
                        result.Updated++;
                        result.ItemsUpdated.Add(item);
                        _logger.LogInformation("[JellyseerrBridge] CreateFoldersAsync: ✅ UPDATED {Type} folder: '{FolderName}'", 
                            typeof(TJellyseerr).Name, folderName);
                    }
                    else
                    {
                        result.Created++;
                        result.ItemsAdded.Add(item);
                        _logger.LogInformation("[JellyseerrBridge] CreateFoldersAsync: ✅ CREATED {Type} folder: '{FolderName}'", 
                            typeof(TJellyseerr).Name, folderName);
                    }
                }
                else
                {
                    _logger.LogError("[JellyseerrBridge] CreateFoldersAsync: ❌ FAILED to create folder for {Item} - MediaName: '{MediaName}', Id: {Id}", 
                        item, item.MediaName, item.Id);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[JellyseerrBridge] CreateFoldersAsync: ❌ ERROR creating folder for {Item} - MediaName: '{MediaName}', Id: {Id}", 
                    item, item.MediaName, item.Id);
            }
        }
        
        _logger.LogInformation("[JellyseerrBridge] CreateFoldersAsync: Completed folder creation for {ItemType} - Processed: {Processed}, Created: {Created}, Updated: {Updated}", 
            typeof(TJellyseerr).Name, result.Processed, result.Created, result.Updated);
        
        return result;
    }
    
    /// <summary>
    /// Creates placeholder videos only for items that are NOT in the ignored items list.
    /// </summary>
    public async Task<ProcessResult> CreatePlaceholderVideosAsync<TJellyseerr>(
        List<TJellyseerr> allItems, 
        List<TJellyseerr> ignoredItems) 
        where TJellyseerr : class, IJellyseerrItem
    {
        var result = new ProcessResult();
        var folderManager = new JellyseerrFolderManager<TJellyseerr>();
        
        // Create a hash set of ignored items for fast lookup
        var ignoredHashes = new HashSet<int>(ignoredItems.Select(item => item.GetItemHashCode()));
        
        _logger.LogInformation("[JellyseerrBridge] CreatePlaceholderVideosAsync: Processing {TotalCount} items, {IgnoredCount} ignored, {PlaceholderCount} will get placeholders", 
            allItems.Count, ignoredItems.Count, allItems.Count - ignoredItems.Count);
        
        foreach (var item in allItems)
        {
            try
            {
                result.Processed++;
                
                // Skip if this item is in the ignored list
                if (ignoredHashes.Contains(item.GetItemHashCode()))
                {
                    _logger.LogDebug("[JellyseerrBridge] CreatePlaceholderVideosAsync: Skipping ignored item: {ItemName}", item.MediaName);
                    continue;
                }
                
                // Get the folder path for this item
                var folderPath = folderManager.GetItemDirectory(item);
                
                if (string.IsNullOrEmpty(folderPath) || !Directory.Exists(folderPath))
                {
                    _logger.LogWarning("[JellyseerrBridge] CreatePlaceholderVideosAsync: Folder does not exist for {ItemName}: {FolderPath}", 
                        item.MediaName, folderPath);
                    continue;
                }
                
                // Create placeholder video based on media type
                bool success = false;
                if (item is JellyseerrMovie)
                {
                    success = await _placeholderVideoGenerator.GeneratePlaceholderMovieAsync(folderPath);
                }
                else if (item is JellyseerrShow)
                {
                    success = await _placeholderVideoGenerator.GeneratePlaceholderShowAsync(folderPath);
                }
                
                if (success)
                {
                    result.Created++;
                    _logger.LogInformation("[JellyseerrBridge] CreatePlaceholderVideosAsync: ✅ Created placeholder video for {ItemName}", item.MediaName);
                }
                else
                {
                    _logger.LogWarning("[JellyseerrBridge] CreatePlaceholderVideosAsync: ❌ Failed to create placeholder video for {ItemName}", item.MediaName);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[JellyseerrBridge] CreatePlaceholderVideosAsync: ❌ ERROR creating placeholder video for {ItemName}", item.MediaName);
            }
        }
        
        _logger.LogInformation("[JellyseerrBridge] CreatePlaceholderVideosAsync: Completed - Processed: {Processed}, Created: {Created}", 
            result.Processed, result.Created);
        
        return result;
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