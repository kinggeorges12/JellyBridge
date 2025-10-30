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
    private readonly NewBridgeService _bridgeService;
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
        NewBridgeService bridgeService,
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

            // Run library scan to find matches and get unmatched items
            var (matchedItems, unmatchedItems) = await _bridgeService.LibraryScanAsync(discoverMovies, discoverShows);

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
            var allFavorites = _userDataManager.GetUserFavorites<IJellyfinItem>(_userManager, _libraryManager.Inner);
            _logger.LogDebug("Retrieved favorites for {UserCount} users", allFavorites.Count);
            
            // Log all favorites for debugging
            foreach (var (user, favorites) in allFavorites)
            {
                _logger.LogTrace("User '{UserName}' has {FavoriteCount} favorites: {FavoriteNames}", 
                    user.Username, favorites.Count, 
                    string.Join(", ", favorites.Select(f => f.Name)));
            }
            
            // Add all favorited items to found lists (these are items that were favorited by users)
            var allFavoritedItems = allFavorites.Values.SelectMany(favs => favs).ToList();
            result.MoviesResult.ItemsFound.AddRange(allFavoritedItems.OfType<JellyfinMovie>());
            result.ShowsResult.ItemsFound.AddRange(allFavoritedItems.OfType<JellyfinSeries>());
            
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
            var unrequestedFavoriteItems = await _favoriteService.FilterRequestsFromFavorites(allFavorites);

            // Step 5: Group bridge-only items by TMDB ID and find first user who favorited each
            var uniqueItemsWithJellyseerrUser = _favoriteService.EnsureFirstJellyseerrUser(bridgeOnlyItems, unrequestedFavoriteItems, jellyseerrUsers);
            _logger.LogDebug("Found {UniqueCount} unique Jellyseerr bridge items from Jellyseerr user favorites (from {TotalCount} total)", 
                uniqueItemsWithJellyseerrUser.Count, bridgeOnlyItems.Count);
            
            // Step 6: Create requests for favorited bridge-only items
            List<(IJellyfinItem item, JellyseerrMediaRequest request)> requestResults = await _favoriteService.RequestFavorites(uniqueItemsWithJellyseerrUser);

            // Add the successful requests directly to the created lists (from tuple)
            result.MoviesResult.ItemsCreated.AddRange(
                requestResults.Where(r => r.request?.Media?.MediaType == JellyseerrModel.MediaType.MOVIE)
                              .Select(r => r.request));
            result.ShowsResult.ItemsCreated.AddRange(
                requestResults.Where(r => r.request?.Media?.MediaType == JellyseerrModel.MediaType.TV)
                              .Select(r => r.request));
            
            // Step 7: Mark requested items by creating an .ignore file in each bridge item directory
            var removedItems = await _favoriteService.IgnoreRequestedAsync(requestResults);

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
                _logger.LogDebug("🔄 Refreshing Jellyseerr library with synced content... (FullRefresh: {FullRefresh}, ItemsDeleted: {Deleted})", itemsDeleted, itemsDeleted);
                refreshSuccess = _libraryService.RefreshJellyseerrLibrary(fullRefresh: itemsDeleted, refreshImages: false);

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

