using Jellyfin.Plugin.JellyseerrBridge.Configuration;
using Jellyfin.Plugin.JellyseerrBridge.BridgeModels;
using Jellyfin.Plugin.JellyseerrBridge.JellyseerrModel;
using Jellyfin.Plugin.JellyseerrBridge.JellyseerrModel.Server;
using Jellyfin.Plugin.JellyseerrBridge.Utils;
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
    private readonly PlaceholderVideoGenerator _placeholderVideoGenerator;
    private readonly IUserManager _userManager;
    private readonly IUserDataManager _userDataManager;

    public JellyseerrBridgeService(ILogger<JellyseerrBridgeService> logger, ILibraryManager libraryManager, IDtoService dtoService, PlaceholderVideoGenerator placeholderVideoGenerator, IUserManager userManager, IUserDataManager userDataManager)
    {
        _logger = logger;
        _libraryManager = libraryManager;
        _dtoService = dtoService;
        _placeholderVideoGenerator = placeholderVideoGenerator;
        _userManager = userManager;
        _userDataManager = userDataManager;
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
                
                _logger.LogInformation("[JellyseerrBridge] Library {LibraryName} locations: {Locations}, HasSyncDirectory: {HasSyncDirectory}, IsJellyseerrLibrary: {IsJellyseerrLibrary}", 
                    library.Name, string.Join(", ", library.Locations ?? new string[0]), hasSyncDirectoryLocation, isJellyseerrLibrary);
                
                // Additional debugging for path comparison
                if (library.Locations != null)
                {
                    foreach (var location in library.Locations)
                    {
                        var isInSync = JellyseerrFolderUtils.IsPathInSyncDirectory(location);
                        _logger.LogInformation("[JellyseerrBridge] Location '{Location}' -> IsInSyncDirectory: {IsInSync}", location, isInSync);
                    }
                }
                
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
        var syncDirectory = Plugin.GetConfigOrDefault<string>(nameof(PluginConfiguration.LibraryDirectory));

        foreach (var existingItem in existingItems)
        {
            _logger.LogDebug("[JellyseerrBridge] Checking existing item: {ItemName} (Id: {ItemId})", 
                existingItem.Name, existingItem.Id);
            
            // Safety check: Skip items that are already in the Jellyseerr library directory
            if (string.IsNullOrEmpty(existingItem.Path) || 
                JellyseerrFolderUtils.IsPathInSyncDirectory(existingItem.Path))
            {
                _logger.LogDebug("[JellyseerrBridge] Skipping item {ItemName} - already in Jellyseerr library directory: {ItemPath}", 
                    existingItem.Name, existingItem.Path);
                continue;
            }
            
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
            else
            {
                // No match found - check if there's a .ignore file to delete
                var bridgeItem = bridgeMetadata.FirstOrDefault(bm => bm.EqualsItem(existingItem));
                if (bridgeItem != null)
                {
                    var bridgeFolderPath = folderManager.GetItemDirectory(bridgeItem);
                    var ignoreFilePath = Path.Combine(bridgeFolderPath, ".ignore");
                    
                    if (File.Exists(ignoreFilePath))
                    {
                        _logger.LogInformation("[JellyseerrBridge] No match found for '{BridgeMediaName}' - deleting .ignore file", 
                            bridgeItem.MediaName);
                        ignoreFileTasks.Add(Task.Run(() => File.Delete(ignoreFilePath)));
                    }
                }
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

        try
        {
            if (string.IsNullOrEmpty(syncDirectory) || !Directory.Exists(syncDirectory))
            {
                throw new InvalidOperationException($"Sync directory does not exist: {syncDirectory}");
            }

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
    public async Task<(List<IJellyseerrItem> allBridgeItems, List<IJellyseerrItem> matchedItems, List<IJellyseerrItem> unmatchedItems)> TestLibraryScanAsync()
    {
        var syncDirectory = Plugin.GetConfigOrDefault<string>(nameof(PluginConfiguration.LibraryDirectory));

        try
        {
            _logger.LogInformation("Testing Jellyfin library scan functionality against bridge folder metadata...");
            
            if (string.IsNullOrEmpty(syncDirectory) || !Directory.Exists(syncDirectory))
            {
                _logger.LogWarning("Sync directory not configured or does not exist: {SyncDirectory}", syncDirectory);
                return (new List<IJellyseerrItem>(), new List<IJellyseerrItem>(), new List<IJellyseerrItem>());
            }

            // Read bridge folder metadata
            var (bridgeMovieMetadata, bridgeShowMetadata) = await ReadBridgeFolderMetadataAsync();

            // Find matches between bridge folder metadata and existing Jellyfin items
            var (matchedItems, unmatchedItems) = await LibraryScanAsync(bridgeMovieMetadata, bridgeShowMetadata);

            _logger.LogInformation("Library scan test completed. Found {MovieCount} movies, {ShowCount} shows, {MatchCount} matches", 
                bridgeMovieMetadata.Count, bridgeShowMetadata.Count, matchedItems.Count);

            // Return all bridge items (not just matches) for test display purposes
            var allBridgeItems = new List<IJellyseerrItem>();
            allBridgeItems.AddRange(bridgeMovieMetadata.Cast<IJellyseerrItem>());
            allBridgeItems.AddRange(bridgeShowMetadata.Cast<IJellyseerrItem>());

            return (allBridgeItems, matchedItems, unmatchedItems);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during library scan test");
            return (new List<IJellyseerrItem>(), new List<IJellyseerrItem>(), new List<IJellyseerrItem>());
        }
    }

    /// <summary>
    /// Test method to scan all users and their favorite items.
    /// </summary>
    public Task<TestFavoritesResult> TestFavoritesScanAsync()
    {
        try
        {
            _logger.LogInformation("Testing Jellyfin favorites scan functionality...");
            
            var result = new TestFavoritesResult();
            var allUsers = _userManager.Users.ToList();
            result.TotalUsers = allUsers.Count;
            
            _logger.LogInformation("Found {UserCount} users in Jellyfin", allUsers.Count);
            
            foreach (var user in allUsers)
            {
                var userFavorites = new UserFavorites
                {
                    UserId = user.Id,
                    UserName = user.Username ?? $"User {user.Id}",
                    Favorites = new List<FavoriteItem>()
                };
                
                // Get all items from all libraries
                var allItems = _libraryManager.GetItemList(new InternalItemsQuery
                {
                    IncludeItemTypes = new[] { BaseItemKind.Movie, BaseItemKind.Series },
                    Recursive = true
                });
                
                foreach (var item in allItems)
                {
                    var userData = _userDataManager.GetUserData(user, item);
                    if (userData.IsFavorite)
                    {
                        userFavorites.Favorites.Add(new FavoriteItem
                        {
                            Id = item.Id,
                            Name = item.Name,
                            Type = item is Movie ? "Movie" : "Series",
                            Year = item.ProductionYear,
                            Path = item.Path
                        });
                    }
                }
                
                userFavorites.FavoriteCount = userFavorites.Favorites.Count;
                result.UserFavorites.Add(userFavorites);
                
                if (userFavorites.FavoriteCount > 0)
                {
                    result.UsersWithFavorites++;
                    result.TotalFavorites += userFavorites.FavoriteCount;
                }
                
                _logger.LogDebug("User {UserName} has {FavoriteCount} favorites", userFavorites.UserName, userFavorites.FavoriteCount);
            }
            
            _logger.LogInformation("Favorites scan test completed. {UsersWithFavorites} users have favorites, total {TotalFavorites} favorite items", 
                result.UsersWithFavorites, result.TotalFavorites);
            
            return Task.FromResult(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during favorites scan test");
            throw;
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
    /// Returns matched and unmatched items as IJellyseerrItem lists.
    /// </summary>
    public async Task<(List<IJellyseerrItem> matched, List<IJellyseerrItem> unmatched)> LibraryScanAsync(List<JellyseerrMovie> movies, List<JellyseerrShow> shows)
    {
        // Combine all items for processing
        var allItems = new List<IJellyseerrItem>();
        allItems.AddRange(movies.Cast<IJellyseerrItem>());
        allItems.AddRange(shows.Cast<IJellyseerrItem>());
        
        _logger.LogInformation("Excluding main libraries from JellyseerrBridge");
        var syncDirectory = Plugin.GetConfigOrDefault<string>(nameof(PluginConfiguration.LibraryDirectory));
        var excludeFromMainLibraries = Plugin.GetConfigOrDefault<bool>(nameof(PluginConfiguration.ExcludeFromMainLibraries));

        if (excludeFromMainLibraries) {
            try
            {
                if (string.IsNullOrEmpty(syncDirectory) || !Directory.Exists(syncDirectory))
                {
                    throw new InvalidOperationException($"Sync directory does not exist: {syncDirectory}");
                }

                _logger.LogInformation("Scanning Jellyfin library for {MovieCount} movies and {ShowCount} shows", movies.Count, shows.Count);

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

                // Combine all bridge metadata into matched and unmatched lists
                var matchedItems = new List<IJellyseerrItem>();
                matchedItems.AddRange(movieMatches);
                matchedItems.AddRange(showMatches);

                // Filter unmatched items by excluding matched ones
                var matchedIds = matchedItems.Select(m => m.Id).ToHashSet();
                var unmatchedItems = allItems.Where(item => !matchedIds.Contains(item.Id)).ToList();

                _logger.LogInformation("Library scan completed. Matched {MatchedMovieCount}/{MovieCount} movies + {MatchedShowCount}/{ShowCount} shows = {TotalCount} total. Unmatched: {UnmatchedCount} items", 
                    movieMatches.Count, movies.Count, showMatches.Count, shows.Count, matchedItems.Count, unmatchedItems.Count);

                return (matchedItems, unmatchedItems);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during library scan test");
            }
        } else {
            _logger.LogInformation("Including main libraries in JellyseerrBridge");
            
            // Delete all existing .ignore files when including main libraries
            var deletedCount = await DeleteAllIgnoreFilesAsync();
            _logger.LogInformation("Deleted {DeletedCount} .ignore files from JellyseerrBridge", deletedCount);
        } 
        return (new List<IJellyseerrItem>(), allItems);
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
        
        // Add all items to processed list at once
        result.ItemsProcessed.AddRange(items);
        
        foreach (var item in items)
        {
            try
            {
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
                        result.ItemsUpdated.Add(item);
                        _logger.LogInformation("[JellyseerrBridge] CreateFoldersAsync: ✅ UPDATED {Type} folder: '{FolderName}'", 
                            typeof(TJellyseerr).Name, folderName);
                    }
                    else
                    {
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
    /// Create season folders for all TV shows.
    /// Creates Season 01 through Season 12 folders with season placeholder videos for each show.
    /// </summary>
    public async Task CreateSeasonFoldersForShows(List<JellyseerrShow> shows)
    {
        _logger.LogInformation("[JellyseerrBridge] CreateSeasonFoldersForShows: Starting season folder creation for {ShowCount} shows", shows.Count);
        
        var folderManager = new JellyseerrFolderManager<JellyseerrShow>();
        
        foreach (var show in shows)
        {
            try
            {
                var showFolderPath = folderManager.GetItemDirectory(show);
                
                _logger.LogInformation("[JellyseerrBridge] CreateSeasonFoldersForShows: Creating season folders for show '{MediaName}' in '{ShowFolderPath}'", 
                    show.MediaName, showFolderPath);
                
                var seasonFolderName = "Season 01";
                var seasonFolderPath = Path.Combine(showFolderPath, seasonFolderName);
                
                try
                {
                    // Create season folder if it doesn't exist
                    if (!Directory.Exists(seasonFolderPath))
                    {
                        Directory.CreateDirectory(seasonFolderPath);
                        _logger.LogDebug("[JellyseerrBridge] CreateSeasonFoldersForShows: Created season folder: '{SeasonFolderPath}'", seasonFolderPath);
                    }
                    
                    // Generate season placeholder video
                    var placeholderSuccess = await _placeholderVideoGenerator.GeneratePlaceholderSeasonAsync(seasonFolderPath);
                    if (placeholderSuccess)
                    {
                        _logger.LogDebug("[JellyseerrBridge] CreateSeasonFoldersForShows: Created season placeholder for: '{SeasonFolderPath}'", seasonFolderPath);
                    }
                    else
                    {
                        _logger.LogWarning("[JellyseerrBridge] CreateSeasonFoldersForShows: Failed to create season placeholder for: '{SeasonFolderPath}'", seasonFolderPath);
                    }
                    
                    _logger.LogInformation("[JellyseerrBridge] CreateSeasonFoldersForShows: ✅ Created season folder for show '{MediaName}'", show.MediaName);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "[JellyseerrBridge] CreateSeasonFoldersForShows: Error creating season folder for show '{MediaName}'", show.MediaName);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[JellyseerrBridge] CreateSeasonFoldersForShows: ❌ ERROR creating season folders for show '{MediaName}'", show.MediaName);
            }
        }
        
        _logger.LogInformation("[JellyseerrBridge] CreateSeasonFoldersForShows: Completed season folder creation for {ShowCount} shows", shows.Count);
    }
    
    /// <summary>
    /// Creates placeholder videos for the provided unmatched items.
    /// </summary>
    public async Task<List<TJellyseerr>> CreatePlaceholderVideosAsync<TJellyseerr>(
        List<TJellyseerr> unmatchedItems) 
        where TJellyseerr : class, IJellyseerrItem
    {
        var processedItems = new List<TJellyseerr>();
        var folderManager = new JellyseerrFolderManager<TJellyseerr>();
        var tasks = new List<Task>();
        
        _logger.LogInformation("[JellyseerrBridge] CreatePlaceholderVideosAsync: Processing {UnmatchedCount} unmatched items for placeholder creation", 
            unmatchedItems.Count);
        
        foreach (var item in unmatchedItems)
        {
            try
            {
                // Get the folder path for this item
                var folderPath = folderManager.GetItemDirectory(item);
                
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
                _logger.LogInformation("[JellyseerrBridge] CreatePlaceholderVideosAsync: ✅ Created placeholder video for {ItemName}", item.MediaName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[JellyseerrBridge] CreatePlaceholderVideosAsync: ❌ ERROR creating placeholder video for {ItemName}", item.MediaName);
            }
        }
        
        // Await all placeholder video tasks
        await Task.WhenAll(tasks);
        
        _logger.LogInformation("[JellyseerrBridge] CreatePlaceholderVideosAsync: Completed - Processed {ProcessedCount} items", 
            processedItems.Count);
        
        return processedItems;
    }

    /// <summary>
    /// Helper method to process items for cleanup.
    /// </summary>
    private List<TJellyseerr> ProcessItemsForCleanup<TJellyseerr>(
        List<TJellyseerr> items) 
        where TJellyseerr : class, IJellyseerrItem
    {
        var deletedItems = new List<TJellyseerr>();
        var maxCollectionDays = Plugin.GetConfigOrDefault<int>(nameof(PluginConfiguration.MaxCollectionDays));
        var cutoffDate = DateTime.Now.AddDays(-maxCollectionDays);
        var folderManager = new JellyseerrFolderManager<TJellyseerr>();
        var itemType = typeof(TJellyseerr).Name.ToLower().Replace("jellyseerr", "");
        
        _logger.LogInformation("[JellyseerrBridge] ProcessItemsForCleanup: Processing {ItemCount} {ItemType}s for cleanup (older than {MaxCollectionDays} days, before {CutoffDate})", 
            items.Count, itemType, maxCollectionDays, cutoffDate.ToString("yyyy-MM-dd HH:mm:ss"));
        
        try
        {
            foreach (var item in items)
            {
                // Check if the item's CreatedDate is older than the cutoff date
                // Treat null CreatedDate as very old (past cutoff date)
                if (item.CreatedDate?.DateTime < cutoffDate || item.CreatedDate == null)
                {
                    _logger.LogDebug("[JellyseerrBridge] ProcessItemsForCleanup: Marking {ItemType} for removal - {ItemName} (Created: {CreatedDate})", 
                        itemType, item.MediaName, item.CreatedDate?.ToString("yyyy-MM-dd HH:mm:ss"));
                    
                    var itemDirectory = folderManager.GetItemDirectory(item);
                    
                    if (Directory.Exists(itemDirectory))
                    {
                        Directory.Delete(itemDirectory, true);
                        deletedItems.Add(item);
                        _logger.LogInformation("[JellyseerrBridge] ProcessItemsForCleanup: ✅ Removed old {ItemType} '{ItemName}' (Created: {CreatedDate})", 
                            itemType, item.MediaName, item.CreatedDate?.ToString("yyyy-MM-dd HH:mm:ss"));
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[JellyseerrBridge] ProcessItemsForCleanup: Error processing {ItemType}", itemType);
        }
        
        return deletedItems;
    }

    /// <summary>
    /// Cleans up metadata by removing items older than the specified number of days.
    /// </summary>
    public async Task<ProcessResult> CleanupMetadataAsync()
    {
        var result = new ProcessResult();

        var maxCollectionDays = Plugin.GetConfigOrDefault<int>(nameof(PluginConfiguration.MaxCollectionDays));
        var cutoffDate = DateTime.Now.AddDays(-maxCollectionDays);

        try
        {
            // Read all bridge folder metadata
            var (movies, shows) = await ReadBridgeFolderMetadataAsync();
            
            _logger.LogInformation("[JellyseerrBridge] CleanupMetadataAsync: Found {MovieCount} movies and {ShowCount} shows to check for cleanup", 
                movies.Count, shows.Count);

            // Process movies and shows using the same logic
            var deletedMovies = ProcessItemsForCleanup(movies);
            var deletedShows = ProcessItemsForCleanup(shows);
            
            // Create ProcessResult from the results
            result.ItemsProcessed.AddRange(movies);
            result.ItemsProcessed.AddRange(shows);
            result.ItemsDeleted.AddRange(deletedMovies);
            result.ItemsDeleted.AddRange(deletedShows);

            _logger.LogInformation("[JellyseerrBridge] CleanupMetadataAsync: Completed cleanup - {Result}", result.ToString());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[JellyseerrBridge] CleanupMetadataAsync: Error during cleanup process");
        }

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
