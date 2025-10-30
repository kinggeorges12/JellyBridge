using Microsoft.Extensions.Logging;
using Jellyfin.Plugin.JellyBridge.Configuration;
using Jellyfin.Plugin.JellyBridge.JellyseerrModel;
using Jellyfin.Plugin.JellyBridge.JellyseerrModel.Server;
using Jellyfin.Plugin.JellyBridge.BridgeModels;
using Jellyfin.Plugin.JellyBridge.Utils;
using Jellyfin.Plugin.JellyBridge;
using Jellyfin.Plugin.JellyBridge.JellyfinModels;
using System.Linq;
using MediaBrowser.Controller.Library;

namespace Jellyfin.Plugin.JellyBridge.Services;

/// <summary>
/// Service for syncing Jellyseerr data with Jellyfin libraries.
/// </summary>
public partial class SyncService
{
    private readonly DebugLogger<SyncService> _logger;
    private readonly ApiService _apiService;
    private readonly JellyfinILibraryManager _libraryManager;
    private readonly IUserManager _userManager;
    private readonly JellyfinIUserDataManager _userDataManager;
    private readonly BridgeService _bridgeService;
    private readonly LibraryService _libraryService;
    private readonly DiscoverService _discoverService;
    private readonly FavoriteService _favoriteService;
    private readonly MetadataService _metadataService;
    

    public SyncService(
        ILogger<SyncService> logger,
        ApiService apiService,
        JellyfinILibraryManager libraryManager,
        IUserManager userManager,
        JellyfinIUserDataManager userDataManager,
        BridgeService bridgeService,
        LibraryService libraryService,
        DiscoverService discoverService,
        FavoriteService favoriteService,
        MetadataService metadataService)
    {
        _logger = new DebugLogger<SyncService>(logger);
        _apiService = apiService;
        _libraryManager = libraryManager;
        _userManager = userManager;
        _userDataManager = userDataManager;
        _bridgeService = bridgeService;
        _libraryService = libraryService;
        _discoverService = discoverService;
        _favoriteService = favoriteService;
        _metadataService = metadataService;
    }

    /// <summary>
    /// Check if a sync operation is currently running.
    /// </summary>
    public bool IsSyncRunning => Plugin.IsOperationRunning;

    /// <summary>
    /// Sync folder structure and JSON metadata files from Jellyseerr to Jellyfin.
    /// Note: Caller is responsible for locking.
    /// </summary>
    public async Task<SyncJellyseerrResult> SyncFromJellyseerr()
    {
        var result = new SyncJellyseerrResult();

        try
        {
            _logger.LogDebug("Starting sync from Jellyseerr to Jellyfin...");

            // Test connection first
            var status = (SystemStatus)await _apiService.CallEndpointAsync(JellyseerrEndpoint.Status);
            if (status == null)
            {
                _logger.LogWarning("Failed to connect to Jellyseerr, skipping sync from Jellyseerr to Jellyfin");
                result.Success = false;
                result.Message = "🔌 Failed to connect to Jellyseerr API";
                return result;
            }

            // Fetch movies for all networks
            var discoverMovies = await _discoverService.FetchDiscoverMediaAsync<JellyseerrMovie>();
            
            // Fetch TV shows for all networks
            var discoverShows = await _discoverService.FetchDiscoverMediaAsync<JellyseerrShow>();

            _logger.LogDebug("Retrieved {MovieCount} movies, {ShowCount} TV shows from Jellyseerr",
                discoverMovies.Count, discoverShows.Count);

            // Check if we actually got any data from the API calls
            if (discoverMovies.Count == 0 && discoverShows.Count == 0)
            {
                _logger.LogError("No data retrieved from Jellyseerr API - all API calls returned empty results");
                result.Success = false;
                result.Message = "📭 No data retrieved from Jellyseerr API. Check API connection and configuration.";
                result.Details = "All API calls returned empty results. This may indicate:\n" +
                               "- API connection issues\n" +
                               "- Invalid API key\n" +
                               "- JSON parsing errors (check logs for JsonException warnings)\n" +
                               "- Empty discover results for configured networks";
                return result;
            }

            _logger.LogDebug("Processing {MovieCount} movies and {ShowCount} shows from Jellyseerr", 
                discoverMovies.Count, discoverShows.Count);

            // Process movies
            _logger.LogTrace("🎬 Creating Jellyfin folders for movies from Jellyseerr...");
            var movieTask = _metadataService.CreateFoldersAsync(discoverMovies);

            // Process TV shows
            _logger.LogTrace("📺 Creating Jellyfin folders for TV shows from Jellyseerr...");
            var showTask = _metadataService.CreateFoldersAsync(discoverShows);

            // Wait for both to complete
            await Task.WhenAll(movieTask, showTask);
            
            // Get the results and set them immediately
            var (addedMovies, updatedMovies) = await movieTask;
            var (addedShows, updatedShows) = await showTask;
            result.AddedMovies = addedMovies;
            result.UpdatedMovies = updatedMovies;
            result.AddedShows = addedShows;
            result.UpdatedShows = updatedShows;

            List<JellyMatch> matchedItems = new List<JellyMatch>();
            List<IJellyseerrItem> unmatchedItems = new List<IJellyseerrItem>();
            var discoverMedia = new List<IJellyseerrItem>();
            discoverMedia.AddRange(discoverMovies.Cast<IJellyseerrItem>());
            discoverMedia.AddRange(discoverShows.Cast<IJellyseerrItem>());
            var excludeFromMainLibraries = Plugin.GetConfigOrDefault<bool>(nameof(PluginConfiguration.ExcludeFromMainLibraries));
            if (excludeFromMainLibraries) {
                // Run library scan to find matches and get unmatched items
                (var allMatchedItems, unmatchedItems) = await _bridgeService.LibraryScanAsync(discoverMedia);
                // Remove matches that point to items already inside the JellyBridge sync directory
                // Remove any unmatched items that already have an ignore file in their folder
                unmatchedItems = _discoverService.FilterIgnoredItems(unmatchedItems);
            } else {
                unmatchedItems = discoverMedia;
                _logger.LogDebug("Including main libraries in JellyBridge");
                
                // Delete all existing .ignore files when including main libraries
                var deletedCount = await _discoverService.DeleteAllIgnoreFilesAsync();
                _logger.LogTrace("Deleted {DeletedCount} .ignore files from JellyBridge", deletedCount);
            } 

            // Create ignore files for matched items
            _logger.LogDebug("🔄 Creating ignore files for {MatchCount} items already in Jellyfin library",
                matchedItems.Count);
            var ignoreTask = _bridgeService.CreateIgnoreFilesAsync(matchedItems);

            var unmatchedShows = unmatchedItems.OfType<JellyseerrShow>().ToList();
            var unmatchedMovies = unmatchedItems.OfType<JellyseerrMovie>().ToList();
            // Create placeholder videos for unmatched movies only
            _logger.LogDebug("🎬 Creating placeholder videos for {UnmatchedMovieCount} unmatched movies not in Jellyfin library...", 
                unmatchedMovies.Count);
            var placeholderMovieTask = _discoverService.CreatePlaceholderVideosAsync(unmatchedMovies);

            // Create season folders for unmatched TV shows only
            _logger.LogDebug("📺 Creating season folders for {UnmatchedShowCount} TV shows not in Jellyfin library...", unmatchedShows.Count);
            var placeholderShowTask = _discoverService.CreateSeasonFoldersForShows(unmatchedShows);
            
            await Task.WhenAll(ignoreTask, placeholderMovieTask, placeholderShowTask);

            // Clean up old metadata before refreshing library
            _logger.LogDebug("🧹 Cleaning up old metadata from Jellyseerr bridge folder...");
            var (deletedMovies, deletedShows) = await _discoverService.CleanupMetadataAsync();
            result.DeletedMovies = deletedMovies;
            result.DeletedShows = deletedShows;
            
            result.Success = true;
            result.Message = "✅ Sync from Jellyseerr to Jellyfin completed successfully";
            result.Details = $"Movies: {addedMovies.Count} added, {updatedMovies.Count} updated, {deletedMovies.Count} deleted | Shows: {addedShows.Count} added, {updatedShows.Count} updated, {deletedShows.Count} deleted";

            _logger.LogTrace("✅ Sync from Jellyseerr to Jellyfin completed successfully - Movies: {MovieAdded} added, {MovieUpdated} updated | Shows: {ShowAdded} added, {ShowUpdated} updated", 
                addedMovies.Count, updatedMovies.Count, addedShows.Count, updatedShows.Count);

            // Check if library management is enabled
            var ranFirstTime = Plugin.GetConfigOrDefault<bool>(nameof(PluginConfiguration.RanFirstTime));
            var itemsDeleted = deletedMovies.Count > 0 || deletedShows.Count > 0;
            bool? refreshSuccess = false;
            
            if (ranFirstTime)
            {
                // Normal operation - only refresh Jellyseerr libraries
                _logger.LogDebug("🔄 Refreshing Jellyseerr library with synced content... (FullRefresh: {FullRefresh}, ItemsDeleted: {Deleted})", itemsDeleted, itemsDeleted);
                refreshSuccess = _libraryService.RefreshJellyseerrLibrary(fullRefresh: itemsDeleted);
            }
            else
            {
                // First time running - scan all libraries
                _logger.LogDebug("🔄 First-time initialization - scanning all Jellyfin libraries for cleanup...");
                refreshSuccess = _libraryService.ScanAllLibrariesForFirstTime();
            }
            if (refreshSuccess == true)
            {
                _logger.LogTrace("✅ Jellyfin library refresh started successfully");
                var refreshType = !ranFirstTime ? "first-time full scan" : (itemsDeleted ? "full" : "partial");
                result.Message += $" and started {refreshType} library refresh";
            }
            else if (refreshSuccess == false)
            {
                _logger.LogWarning("⚠️ Jellyfin library refresh failed");
                result.Message += " (library management failed)";
            }
            // refresh success is null if library management is disabled
        }
        catch (DirectoryNotFoundException ex)
        {
            _logger.LogError(ex, "Directory not found during sync from Jellyseerr to Jellyfin");
            result.Success = false;
            result.Message = $"📁 Directory not found: {ex.Message}";
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogError(ex, "Access denied during sync from Jellyseerr to Jellyfin");
            result.Success = false;
            result.Message = $"🚫 Access denied: {ex.Message}";
        }
        catch (IOException ex)
        {
            _logger.LogError(ex, "I/O error during sync from Jellyseerr to Jellyfin");
            result.Success = false;
            result.Message = $"💾 I/O error: {ex.Message}";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during sync from Jellyseerr to Jellyfin");
            result.Success = false;
            result.Message = $"❌ Sync from Jellyseerr to Jellyfin failed: {ex.Message}";
            result.Details = $"Exception type: {ex.GetType().Name}\nStack trace: {ex.StackTrace}";
        }
        
        return result;
    }

    /// <summary>
    /// Sync data from Jellyfin to Jellyseerr by creating requests for favorited bridge-only items.
    /// Note: Caller is responsible for locking.
    /// </summary>
    public async Task<SyncJellyfinResult> SyncToJellyseerr()
    {
        var result = new SyncJellyfinResult();

        try
        {
            _logger.LogDebug("Starting sync to Jellyseerr...");
            
            // Step 1: Get bridge folder items directly from Jellyfin
            var bridgeLibraryPath = Plugin.GetConfigOrDefault<string>(nameof(PluginConfiguration.LibraryDirectory));
            
            // Get bridge-only items directly from Jellyfin
            var bridgeMovies = _libraryManager.GetExistingItems<JellyfinMovie>(bridgeLibraryPath);
            var bridgeShows = _libraryManager.GetExistingItems<JellyfinSeries>(bridgeLibraryPath);
            
            var bridgeOnlyItems = new List<IJellyfinItem>();
            bridgeOnlyItems.AddRange(bridgeMovies);
            bridgeOnlyItems.AddRange(bridgeShows);
            
            if (bridgeOnlyItems.Count == 0)
            {
                _logger.LogWarning("No Jellyseerr bridge items found in folder: {BridgePath}", bridgeLibraryPath);
                result.Success = true;
                result.Message = "📭 No Jellyseerr bridge items found to sync";
                return result;
            }
            
            _logger.LogDebug("Found {BridgeOnlyCount} Jellyseerr bridge items in folder: {BridgePath}", bridgeOnlyItems.Count, bridgeLibraryPath);
            
            // Add all bridge items to processed lists (these are all items we're working with)
            result.MoviesResult.ItemsProcessed.AddRange(bridgeMovies);
            result.ShowsResult.ItemsProcessed.AddRange(bridgeShows);
            
            // Step 2: Get all Jellyfin users and their favorites
            var allFavoritesDict = _userDataManager.GetUserFavorites<IJellyfinItem>(_userManager, _libraryManager.Inner);
            _logger.LogDebug("Retrieved favorites for {UserCount} users", allFavoritesDict.Count);
            foreach (var (user, favorites) in allFavoritesDict)
            {
                _logger.LogTrace("User '{UserName}' has {FavoriteCount} favorites: {FavoriteNames}", 
                    user.Username, favorites.Count, 
                    string.Join(", ", favorites.Select(f => f.Name)));
            }
            var allFavoritedItems = allFavoritesDict.SelectMany(kv => kv.Value.Select(item => (kv.Key, item))).ToList();
            result.MoviesResult.ItemsFound.AddRange(allFavoritedItems.Where(fav => fav.item is JellyfinMovie).Select(fav => (JellyfinMovie)fav.item));
            result.ShowsResult.ItemsFound.AddRange(allFavoritedItems.Where(fav => fav.item is JellyfinSeries).Select(fav => (JellyfinSeries)fav.item));
            
            // Step 3: Get all Jellyseerr users for request creation
            var jellyseerrUsers = await _favoriteService.GetJellyseerrUsersAsync();
            if (jellyseerrUsers == null || jellyseerrUsers.Count == 0)
            {
                _logger.LogWarning("No Jellyseerr users found");
                result.Success = false;
                result.Message = "👥 No Jellyseerr users found";
                return result;
            }

            // Step 4: Filter out items that already have requests in Jellyseerr
            var unrequestedFavorites = await _favoriteService.FilterRequestsFromFavorites(allFavoritedItems);

            // Step 5: Group bridge-only items by TMDB ID and find first user who favorited each
            var unrequestedFavoritesWithJellyseerrUser = _favoriteService.EnsureJellyseerrUser(unrequestedFavorites, jellyseerrUsers);

            // Step 6: Create requests for favorited bridge-only items
            var requestResults = await _favoriteService.RequestFavorites(unrequestedFavoritesWithJellyseerrUser);

            // Add the successful requests directly to the created lists (from tuple)
            result.MoviesResult.ItemsCreated.AddRange(
                requestResults.Where(r => r.request?.Media?.MediaType == JellyseerrModel.MediaType.MOVIE)
                              .Select(r => r.request));
            result.ShowsResult.ItemsCreated.AddRange(
                requestResults.Where(r => r.request?.Media?.MediaType == JellyseerrModel.MediaType.TV)
                              .Select(r => r.request));
            
            // Step 7: Mark requested items by creating an .ignore file in each bridge item directory
            var allFavoritedJellyfinItems = new List<IJellyfinItem>();
            allFavoritedJellyfinItems.AddRange(unrequestedFavoritesWithJellyseerrUser.Select(fav => fav.item));
            allFavoritedJellyfinItems.AddRange(allFavoritedItems.Select(fav => fav.item));

            // Delegate to FavoriteService to scan and write ignore files for matched favorites
            var removedItems = await _favoriteService.IgnoreRequestedAsync(allFavoritedJellyfinItems);

            // Save results
            result.Success = true;
            result.Message = "✅ Sync to Jellyseerr completed successfully";
            result.Details = $"Processed {bridgeOnlyItems.Count} Jellyseerr bridge items, found {allFavoritedItems.Count} favorited items, created {requestResults.Count} requests for favorited items, removed {removedItems.Count} requested items";

            // Step 8: Refresh Jellyseerr libraries with synced content
            var itemsDeleted = removedItems.Count > 0;
            bool? refreshSuccess = false;
            if (itemsDeleted)
            {
                result.MoviesResult.ItemsRemoved.AddRange(removedItems.OfType<JellyfinMovie>());
                result.ShowsResult.ItemsRemoved.AddRange(removedItems.OfType<JellyfinSeries>());

                // Normal operation - only refresh Jellyseerr libraries
                _logger.LogDebug("🔄 Refreshing Jellyseerr library with synced content... (fullRefresh: false, refreshImages: false)");
                refreshSuccess = _libraryService.RefreshJellyseerrLibrary(fullRefresh: false, refreshImages: false);

                if (refreshSuccess == true)
                {
                    _logger.LogTrace("✅ Full metadata refresh without images started successfully");
                    result.Message += $" and started full metadata refresh";
                }
                else if (refreshSuccess == false)
                {
                    _logger.LogWarning("⚠️ Full metadata refresh without images failed");
                    result.Message += " (metadata refresh failed)";
                }
            }
            
            _logger.LogDebug("Sync to Jellyseerr completed with {ResultCount} successful requests", requestResults.Count);
        }
        catch (MissingMethodException ex)
        {
            _logger.LogDebug(ex, "Using incompatible Jellyfin version. Skipping sync to Jellyseerr");
            result.Success = false;
            result.Message = "❌ Using incompatible Jellyfin version. Skipping sync to Jellyseerr";
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during sync to Jellyseerr");
            result.Success = false;
            result.Message = $"❌ Sync to Jellyseerr failed: {ex.Message}";
            result.Details = $"Exception type: {ex.GetType().Name}\nStack trace: {ex.StackTrace}";
        }
        
        return result;
    }
}

