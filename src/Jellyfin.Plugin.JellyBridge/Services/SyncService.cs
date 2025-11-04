using Microsoft.Extensions.Logging;
using Jellyfin.Plugin.JellyBridge.Configuration;
using Jellyfin.Plugin.JellyBridge.JellyseerrModel;
using Jellyfin.Plugin.JellyBridge.JellyseerrModel.Server;
using Jellyfin.Plugin.JellyBridge.BridgeModels;
using Jellyfin.Plugin.JellyBridge.Utils;
using Jellyfin.Plugin.JellyBridge;
using Jellyfin.Plugin.JellyBridge.JellyfinModels;
using System.Linq;

namespace Jellyfin.Plugin.JellyBridge.Services;

/// <summary>
/// Service for syncing Jellyseerr data with Jellyfin libraries.
/// </summary>
public partial class SyncService
{
    private readonly DebugLogger<SyncService> _logger;
    private readonly ApiService _apiService;
    private readonly BridgeService _bridgeService;
    private readonly LibraryService _libraryService;
    private readonly DiscoverService _discoverService;
    private readonly FavoriteService _favoriteService;
    private readonly MetadataService _metadataService;
    

    public SyncService(
        ILogger<SyncService> logger,
        ApiService apiService,
        BridgeService bridgeService,
        LibraryService libraryService,
        DiscoverService discoverService,
        FavoriteService favoriteService,
        MetadataService metadataService)
    {
        _logger = new DebugLogger<SyncService>(logger);
        _apiService = apiService;
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

            // Step 0: Test connection first
            var status = (SystemStatus)await _apiService.CallEndpointAsync(JellyseerrEndpoint.Status);
            if (status == null)
            {
                _logger.LogWarning("Failed to connect to Jellyseerr, skipping sync from Jellyseerr to Jellyfin");
                result.Success = false;
                result.Message = "🔌 Failed to connect to Jellyseerr API";
                return result;
            }

            // Step 1: Fetch movies and TV shows for all networks
            var discoverMovies = await _discoverService.FetchDiscoverMediaAsync<JellyseerrMovie>();
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

            // Combine movies and shows into a single list
            var discoverMedia = new List<IJellyseerrItem>();
            discoverMedia.AddRange(discoverMovies.Cast<IJellyseerrItem>());
            discoverMedia.AddRange(discoverShows.Cast<IJellyseerrItem>());

            // Step 2: Filter duplicates for networks
            var uniqueDiscoverMedia = await _discoverService.FilterDuplicateMedia(discoverMedia);

            // Step 3: Process movies and TV shows
            _logger.LogTrace("Step 3: 📺 Creating Jellyfin folders and metadata for movies and TV shows from Jellyseerr...");
            var (addedMedia, updatedMedia) = await _metadataService.CreateFolderMetadataAsync(uniqueDiscoverMedia);
            // Get the results and set them immediately
            result.AddedMovies.AddRange(addedMedia.OfType<JellyseerrMovie>().ToList());
            result.UpdatedMovies.AddRange(updatedMedia.OfType<JellyseerrMovie>().ToList());
            result.AddedShows.AddRange(addedMedia.OfType<JellyseerrShow>().ToList());
            result.UpdatedShows.AddRange(updatedMedia.OfType<JellyseerrShow>().ToList());

            // Step 3.5: Ignore duplicated items for networks with shared libraries
            var ignoredDuplicates = await _discoverService.IgnoreDuplicateLibraryItems(discoverMedia, uniqueDiscoverMedia);
            _logger.LogDebug("Step 3.5: Ignored {IgnoredCount} duplicate library items", ignoredDuplicates.Count);

            result.IgnoredMovies.AddRange(ignoredDuplicates.OfType<JellyseerrMovie>().ToList());
            result.IgnoredShows.AddRange(ignoredDuplicates.OfType<JellyseerrShow>().ToList());

            // Step 4: Library Scan to find matches and get unmatched items
            List<JellyMatch> matchedItems = new List<JellyMatch>();
            List<IJellyseerrItem> unmatchedItems = new List<IJellyseerrItem>();
            var excludeFromMainLibraries = Plugin.GetConfigOrDefault<bool>(nameof(PluginConfiguration.ExcludeFromMainLibraries));
            if (excludeFromMainLibraries) {
                _logger.LogDebug("Step 4: Starting library scan to find matches and get unmatched items");
                // Run library scan to find matches and get unmatched items
                // When UseNetworkFolders and AddDuplicateContent are enabled, unmatched items calculates on network directory.
                (var allMatchedItems, var allUnmatchedItems) = await _bridgeService.LibraryScanAsync(uniqueDiscoverMedia);
                _logger.LogDebug("Step 4: Library scan produced {MatchCount} matches and {UnmatchedCount} unmatched items", allMatchedItems.Count, unmatchedItems.Count);
                // Remove matches that point to items already inside the JellyBridge sync directory
                (matchedItems, unmatchedItems) = await _discoverService.FilterSyncedLibraryItems(allMatchedItems, allUnmatchedItems);
                _logger.LogDebug("Step 4: Filtered synced items: {SyncedCount}", allUnmatchedItems.Count);
                // Remove any unmatched items that already have an ignore file in their folder
                unmatchedItems = _discoverService.FilterIgnoredItems(unmatchedItems);
                _logger.LogDebug("Step 4: Filtered ignored items; remaining unmatched = {UnmatchedCount}", unmatchedItems.Count);
            } else {
                // Step 4.5: If not excluding from main libraries, set unmatched items to discover media
                unmatchedItems = uniqueDiscoverMedia;
                _logger.LogDebug("Step 4.5: Including main libraries in JellyBridge");
                
                // Delete all existing .ignore files when including main libraries
                var deletedCount = await _discoverService.DeleteAllIgnoreFilesAsync();
                _logger.LogTrace("Step 4.5: Deleted {DeletedCount} .ignore files from JellyBridge", deletedCount);
            } 

            // Step 5: Create ignore files for matched items and add them to ignored items
            _logger.LogDebug("Step 5: 🔄 Creating ignore files for {MatchCount} items already in Jellyfin library",
                matchedItems.Count);
            
            // Add matched items to ignored items list
            var matchedJellyseerrItems = matchedItems.Select(m => m.JellyseerrItem).ToList();
            var matchedIgnoredMovies = matchedJellyseerrItems.OfType<JellyseerrMovie>().ToList();
            var matchedIgnoredShows = matchedJellyseerrItems.OfType<JellyseerrShow>().ToList();
            result.IgnoredMovies.AddRange(matchedIgnoredMovies);
            result.IgnoredShows.AddRange(matchedIgnoredShows);
            
            var ignoreTask = _bridgeService.CreateIgnoreFilesAsync(matchedItems);

            // Step 6: Create placeholder videos for unmatched movies
            var unmatchedMovies = unmatchedItems.OfType<JellyseerrMovie>().ToList();
            _logger.LogDebug("Step 6: 🎬 Creating placeholder videos for {UnmatchedMovieCount} unmatched movies not in Jellyfin library...", 
                unmatchedMovies.Count);
            var placeholderMovieTask = _discoverService.CreatePlaceholderVideosAsync(unmatchedMovies);

            // Step 7: Create season folders for unmatched TV shows
            var unmatchedShows = unmatchedItems.OfType<JellyseerrShow>().ToList();
            _logger.LogDebug("Step 7: 📺 Creating season folders for {UnmatchedShowCount} TV shows not in Jellyfin library...", unmatchedShows.Count);
            var placeholderShowTask = _discoverService.CreateSeasonFoldersForShows(unmatchedShows);
            
            await Task.WhenAll(ignoreTask, placeholderMovieTask, placeholderShowTask);

            // Step 8: Clean up old metadata before refreshing library
            _logger.LogDebug("Step 8: 🧹 Cleaning up old metadata from Jellyseerr bridge folder...");
            var (deletedMovies, deletedShows) = await _discoverService.CleanupMetadataAsync();
            
            // Update MoviesResult and ShowsResult with deleted items
            result.DeletedMovies.AddRange(deletedMovies);
            result.DeletedShows.AddRange(deletedShows);

            // Step 9: Provide refresh plan back to caller; orchestration occurs after both syncs complete
            var itemsDeleted = deletedMovies.Count > 0 || deletedShows.Count > 0;
            result.Refresh = new RefreshPlan
            {
                FullRefresh = itemsDeleted,
                RefreshImages = true
            };
            
            // Step 10: Save results
            result.Success = true;
            result.Message = "✅ Sync from Jellyseerr to Jellyfin completed successfully";
            result.Details = result.ToString();

            _logger.LogTrace("✅ Sync from Jellyseerr to Jellyfin completed successfully - Movies: {MovieAdded} added, {MovieUpdated} updated, {MovieDeleted} deleted | Shows: {ShowAdded} added, {ShowUpdated} updated, {ShowDeleted} deleted", 
                result.MoviesResult.Created, result.MoviesResult.Updated, result.MoviesResult.Deleted, result.ShowsResult.Created, result.ShowsResult.Updated, result.ShowsResult.Deleted);
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
            _logger.LogDebug("Starting sync from Jellyfin to Jellyseerr...");

            // Step 0: Test connection first
            var status = (SystemStatus)await _apiService.CallEndpointAsync(JellyseerrEndpoint.Status);
            if (status == null)
            {
                _logger.LogWarning("Failed to connect to Jellyseerr, skipping sync from Jellyseerr to Jellyfin");
                result.Success = false;
                result.Message = "🔌 Failed to connect to Jellyseerr API";
                return result;
            }
            
            // Step 1: Get all Jellyfin users and their favorites from JellyBridge folder
            _logger.LogDebug("Step 1: Getting Jellyfin favorites from JellyBridge folder");
            var bridgeFavoritedItems = _favoriteService.GetUserFavorites();
            _logger.LogDebug("Step 1: Retrieved {Count} favorited bridge items", bridgeFavoritedItems.Count);
            
            result.ProcessedMovies.AddRange(bridgeFavoritedItems.Where(fav => fav.item is JellyfinMovie).Select(fav => (JellyfinMovie)fav.item));
            result.ProcessedShows.AddRange(bridgeFavoritedItems.Where(fav => fav.item is JellyfinSeries).Select(fav => (JellyfinSeries)fav.item));
            
            // Step 2: Get all Jellyseerr users for request creation
            _logger.LogDebug("Step 2: Fetching Jellyseerr users for request creation");
            var jellyseerrUsersTask = _favoriteService.GetJellyseerrUsersAsync();

            // Step 3: Filter out items that already have requests in Jellyseerr
            _logger.LogDebug("Step 3: Filtering favorites that already have Jellyseerr requests");
            var filteredFavoritesTask = _favoriteService.FilterRequestsFromFavorites(bridgeFavoritedItems);
            
            await Task.WhenAll(jellyseerrUsersTask, filteredFavoritesTask);
            var jellyseerrUsers = await jellyseerrUsersTask;
            var (_, unrequestedFavorites) = await filteredFavoritesTask;
            _logger.LogDebug("Step 3: Found {UnrequestedCount} favorites pending request", unrequestedFavorites.Count);

            // Step 4: Group bridge-only items by TMDB ID and find first user who favorited each
            _logger.LogDebug("Step 4: Mapping favorites to Jellyseerr users");
            var unrequestedFavoritesWithJellyseerrUser = _favoriteService.EnsureJellyseerrUser(unrequestedFavorites, jellyseerrUsers);

            // Step 5: Create requests for favorited bridge-only items
            _logger.LogDebug("Step 5: Creating Jellyseerr requests for mapped favorites");
            var (requestResults, blockedItems) = await _favoriteService.RequestFavorites(unrequestedFavoritesWithJellyseerrUser);

            // Add the successful requests directly to the created lists (from tuple)
            result.CreatedMovies.AddRange(
                requestResults.Where(r => r.request?.Media?.MediaType == JellyseerrModel.MediaType.MOVIE)
                              .Select(r => r.request));
            result.CreatedShows.AddRange(
                requestResults.Where(r => r.request?.Media?.MediaType == JellyseerrModel.MediaType.TV)
                              .Select(r => r.request));
            
            // Add blocked items to the result
            result.BlockedMovies.AddRange(
                blockedItems.Where(item => item is JellyfinMovie).Cast<JellyfinMovie>());
            result.BlockedShows.AddRange(
                blockedItems.Where(item => item is JellyfinSeries).Cast<JellyfinSeries>());
            
            // Step 6: For requested items, unmark as favorite for the user and create an .ignore file in each bridge item directory
            _logger.LogDebug("Step 6: Unfavoriting requested items and creating ignore files");
            var removedItems = await _favoriteService.UnmarkAndIgnoreRequestedAsync();

            //Step 8: Check requests again and remove .ignore files for items that are no longer in the requested items in Jellyseerr
            _logger.LogDebug("Step 7: Removing .ignore files for fully declined requests");
            var declinedItems = await _favoriteService.UnignoreDeclinedRequests();

            // Step 8: Provide refresh plan to caller based on removals
            _logger.LogDebug("Step 8: Refreshing Jellyfin libraries");
            var itemsDeleted = removedItems.Count > 0 || (declinedItems.Count > 0);
            if (itemsDeleted)
            {
                result.RemovedMovies.AddRange(removedItems.OfType<JellyfinMovie>());
                result.RemovedShows.AddRange(removedItems.OfType<JellyfinSeries>());
                result.RemovedMovies.AddRange(declinedItems.OfType<JellyfinMovie>());
                result.RemovedShows.AddRange(declinedItems.OfType<JellyfinSeries>());
                result.Refresh = new RefreshPlan
                {
                    FullRefresh = true,
                    RefreshImages = false
                };
            }

            // Step 9: Save results
            result.Success = true;
            result.Message = "✅ Sync to Jellyseerr completed successfully";
            result.Details = result.ToString();
            
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

    /// <summary>
    /// Applies the post-sync refresh operations based on the two sync results.
    /// - Calls RefreshBridgeLibrary with computed parameters
    /// - Then scans all libraries and awaits completion
    /// </summary>
    public async Task ApplyRefreshAsync(SyncJellyfinResult? syncToResult = null, SyncJellyseerrResult? syncFromResult = null)
    {
        try
        {
            var fullRefresh = (syncToResult?.Refresh?.FullRefresh == true) || (syncFromResult?.Refresh?.FullRefresh == true);
            var refreshImages = (syncToResult?.Refresh?.RefreshImages == true) || (syncFromResult?.Refresh?.RefreshImages == true);

            _logger.LogDebug("Applying refresh plan - FullRefresh: {FullRefresh}, RefreshImages: {RefreshImages}", fullRefresh, refreshImages);
            _logger.LogDebug("Awaiting scan of all Jellyfin libraries...");
            // refreshUserData defaults to true - will perform light refresh to reload user data
            await _libraryService.RefreshBridgeLibrary(fullRefresh: fullRefresh, refreshImages: refreshImages);
            _logger.LogDebug("Scan of all libraries completed");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error applying post-sync refresh operations");
        }
    }

}

