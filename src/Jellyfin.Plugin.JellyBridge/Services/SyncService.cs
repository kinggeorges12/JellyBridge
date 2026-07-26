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
    private readonly RefreshService _refreshService;
    private readonly DiscoverService _discoverService;
    private readonly FavoriteService _favoriteService;
    private readonly MetadataService _metadataService;
    

    public SyncService(
        ILogger<SyncService> logger,
        ApiService apiService,
        BridgeService bridgeService,
        RefreshService refreshService,
        DiscoverService discoverService,
        FavoriteService favoriteService,
        MetadataService metadataService)
    {
        _logger = new DebugLogger<SyncService>(logger);
        _apiService = apiService;
        _bridgeService = bridgeService;
        _refreshService = refreshService;
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

            _logger.LogDebug("Step 1: Starting Jellyseerr discovery to find movies and TV shows for all networks");
            // Step 1: Fetch movies and TV shows for all networks
            var (discoverMovies, discoverShows) = await _discoverService.FetchDiscoverMediaAsync();
            _logger.LogDebug("Step 1: Retrieved {MovieCount} movies, {ShowCount} TV shows from Jellyseerr",
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

            _logger.LogDebug("Step 2: Processing {MovieCount} movies and {ShowCount} shows from Jellyseerr", 
                discoverMovies.Count, discoverShows.Count);

            // Combine movies and shows into a single list
            var discoverMedia = new List<IJellyseerrItem>();
            discoverMedia.AddRange(discoverMovies.Cast<IJellyseerrItem>());
            discoverMedia.AddRange(discoverShows.Cast<IJellyseerrItem>());

            // Step 2: Filter duplicates for networks
            var uniqueDiscoverMedia = await _bridgeService.FilterDuplicatesByLibrary(discoverMedia);
            _logger.LogInformation("Step 2: Filtered {OriginalCount} items to {UniqueCount} unique items across networks", 
                discoverMedia.Count, uniqueDiscoverMedia.Count);

            // Step 3: Process movies and TV shows
            _logger.LogDebug("Step 3: 📺 Creating Jellyfin folders and metadata for movies and TV shows from Jellyseerr...");
            // Add all folders, then create ignore files for duplicates later
            var (addedMedia, updatedMedia) = await _metadataService.CreateFolderMetadataAsync(discoverMedia);
            // Add items to unified collections
            result.ItemsAdded.AddRange(addedMedia);
            result.ItemsUpdated.AddRange(updatedMedia);

            // Step 4: Ignore duplicated items in JellyBridge for networks with shared libraries
            var (newlyIgnoredDuplicates, existingIgnoredDuplicates) = await _discoverService.IgnoreJellyBridgeDuplicates(discoverMedia, uniqueDiscoverMedia);
            _logger.LogDebug("Step 4: Ignored {IgnoredCount} duplicate JellyBridge items ({NewlyIgnored} newly ignored, {ExistingIgnored} already ignored)", 
                newlyIgnoredDuplicates.Count + existingIgnoredDuplicates.Count, newlyIgnoredDuplicates.Count, existingIgnoredDuplicates.Count);

            result.ItemsSkipped.AddRange(existingIgnoredDuplicates);
            result.ItemsHidden.AddRange(newlyIgnoredDuplicates);

            // Step 5: Library Scan to find matches and get unmatched items
            _logger.LogDebug("Step 5: Starting library scan to find matches and get unmatched items");
            // Run library scan to find matches and get unmatched items
            // Matched items get a .ignore file created in their folder
            // Unmatched items get a placeholder file created in their folder
            // When UseNetworkFolders and AddDuplicateContent are enabled, unmatched items calculates on network directory.
            (var matchedItems, var unmatchedItems) = await _bridgeService.BuildJellyfinMatchesAsync(uniqueDiscoverMedia);
            _logger.LogDebug("Step 5: Library scan produced {MatchCount} matches and {UnmatchedCount} unmatched items", matchedItems.Count, unmatchedItems.Count);
            // Remove any unmatched items that already have an ignore file in their folder
            unmatchedItems = _bridgeService.FilterAlreadyIgnoredItems(unmatchedItems);
            _logger.LogDebug("Step 6: Filtered ignored items; remaining unmatched = {UnmatchedCount}", unmatchedItems.Count);

            // Check if user wants to remove matches from libraries outside of the JellyBridge discover library
            Task<(List<IJellyseerrItem> newIgnored, List<IJellyseerrItem> existingIgnored)> ignoreTask;
            var excludeFromMainLibraries = Plugin.GetConfigOrDefault<bool>(nameof(PluginConfiguration.ExcludeFromMainLibraries));
            if (excludeFromMainLibraries) {
                // Step 7: Create ignore files for matched items and add them to ignored items
                _logger.LogDebug("Step 7: 🔄 Creating ignore files for {MatchCount} items already in Jellyfin library",
                    matchedItems.Count);
                ignoreTask = _bridgeService.IgnoreMatchAsync(matchedItems);
            }
            else
            {
                // Step 7: If including main libraries, unignore those matched items
                _logger.LogDebug("Step 7: 🔄 Removing ignore files for {MatchCount} items already in Jellyfin library",
                    matchedItems.Count);
                ignoreTask = _discoverService.DeleteIgnoreFilesAsync(matchedItems);
            }

            // Step 8: Create placeholder videos for unmatched movies
            var unmatchedMovies = unmatchedItems.OfType<JellyseerrMovie>().ToList();
            _logger.LogDebug("Step 8: 🎬 Creating placeholder videos for {UnmatchedMovieCount} unmatched movies not in Jellyfin library...", 
                unmatchedMovies.Count);
            var placeholderMovieTask = _discoverService.CreatePlaceholderVideosAsync(unmatchedMovies);

            // Step 8: Create season folders for unmatched TV shows
            var unmatchedShows = unmatchedItems.OfType<JellyseerrShow>().ToList();
            _logger.LogDebug("Step 8: 📺 Creating season folders for {UnmatchedShowCount} TV shows not in Jellyfin library...", unmatchedShows.Count);
            var placeholderShowTask = _discoverService.CreatePlaceholderVideosAsync(unmatchedShows);
            
            await Task.WhenAll(ignoreTask, placeholderMovieTask, placeholderShowTask);

            // Step 7 (again): Gather ignore results and add to appropriate collections
            var (newIgnored, existingIgnored) = await ignoreTask;
            result.ItemsSkipped.AddRange(existingIgnored);
            result.ItemsHidden.AddRange(newIgnored);

            // Step 9: Filter and ignore items with invalid network IDs
            _logger.LogDebug("Step 9: 🔍 Filtering and ignoring items with invalid network IDs...");
            var (newIgnoredNetwork, existingIgnoredNetwork) = await _discoverService.IgnoreInvalidNetworkItemsAsync();
            result.ItemsSkipped.AddRange(existingIgnoredNetwork);
            result.ItemsHidden.AddRange(newIgnoredNetwork);

            // Step 9: Provide refresh plan back to caller; orchestration occurs after both syncs complete
            // Only set refresh plan if there are added items or hidden items
            var hasAddedItems = result.ItemsAdded.Count > 0;
            var hasHiddenItems = result.ItemsHidden.Count > 0;
            
            if (hasAddedItems || hasHiddenItems)
            {
                _logger.LogDebug("Step 9: Refreshing Jellyfin libraries");
                result.Refresh = new RefreshPlan
                {
                    // Added items trigger create refresh, hidden items trigger remove refresh
                    CreateRefresh = hasAddedItems,
                    RemoveRefresh = hasHiddenItems,
                    RefreshImages = hasAddedItems
                };
            }
            
            // Step 10: Save results
            result.Success = true;
            result.Message = "✅ Sync from Jellyseerr to Jellyfin completed successfully";

            _logger.LogTrace("✅ Sync from Jellyseerr to Jellyfin completed successfully - Movies: {MovieAdded} added, {MovieUpdated} updated, {MovieHidden} hidden | Shows: {ShowAdded} added, {ShowUpdated} updated, {ShowHidden} hidden", 
                result.AddedMovies.Count, result.UpdatedMovies.Count, result.HiddenMovies.Count, result.AddedShows.Count, result.UpdatedShows.Count, result.HiddenShows.Count);
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
            
            // Add items to unified collection
            result.ItemsProcessed.AddRange(bridgeFavoritedItems.Select(fav => fav.item));
            
            // Step 2: Get all Jellyseerr users for request creation
            _logger.LogDebug("Step 2: Fetching Jellyseerr users for request creation");
            var jellyseerrUsersTask = _favoriteService.GetJellyseerrUsersAsync();

            // Step 3: Filter out items that already have requests in Jellyseerr
            _logger.LogDebug("Step 3: Filtering favorites that already have Jellyseerr requests");
            var filteredFavoritesTask = _favoriteService.FilterRequestsFromFavorites(bridgeFavoritedItems);
            
            await Task.WhenAll(jellyseerrUsersTask, filteredFavoritesTask);
            var jellyseerrUsers = await jellyseerrUsersTask;
            var (requestedFavorites, unrequestedFavorites) = await filteredFavoritesTask;
            _logger.LogDebug("Step 3: Found {UnrequestedCount} favorites pending request", unrequestedFavorites.Count);

            // Add found items to unified collection
            result.ItemsFound.AddRange(requestedFavorites.Select(fav => fav.item));

            // Step 4: Group bridge-only items by TMDB ID and find first user who favorited each
            _logger.LogDebug("Step 4: Mapping favorites to Jellyseerr users");
            var unrequestedFavoritesWithJellyseerrUser = _favoriteService.EnsureJellyseerrUser(unrequestedFavorites, jellyseerrUsers);

            // Step 5: Create requests for favorited bridge-only items
            _logger.LogDebug("Step 5: Creating Jellyseerr requests for mapped favorites");
            var (requestResults, blockedItems) = await _favoriteService.RequestFavorites(unrequestedFavoritesWithJellyseerrUser);

            // Add requests to unified collection
            result.ItemsCreated.AddRange(requestResults.Select(r => r.request).Where(r => r != null));
            
            // Add blocked items to unified collection
            result.ItemsBlocked.AddRange(blockedItems);
            
            // Step 6: For requested items, unmark as favorite for the user and create an .ignore file in each bridge item directory
            _logger.LogDebug("Step 6: Unfavoriting requested items and creating ignore files");
            var (newlyIgnoredItems, clearedItems) = await _favoriteService.ClearAndIgnoreRequestedAsync();
            result.ItemsHidden.AddRange(newlyIgnoredItems);
            result.ItemsCleared.AddRange(clearedItems);
            _logger.LogDebug("Step 6: Hidden {NewlyIgnored} items, cleared {ClearedCount} items", newlyIgnoredItems.Count, clearedItems.Count);

            //Step 8: Check requests again and remove .ignore files for items that are no longer in the requested items in Jellyseerr
            _logger.LogDebug("Step 7: Removing .ignore files for fully declined requests");
            var declinedItems = await _favoriteService.UnignoreDeclinedRequests();
            result.ItemsUnhidden.AddRange(declinedItems);
            _logger.LogDebug("Step 7: Unhidden {UnhiddenCount} declined items", declinedItems.Count);

            // Step 9: Provide refresh plan back to caller; orchestration occurs after both syncs complete
            // Only set refresh plan if there are added items or hidden items
            var hasHiddenItems = result.ItemsHidden.Count > 0;
            var hasUnhiddenItems = result.ItemsUnhidden.Count > 0;

            if (hasHiddenItems || hasUnhiddenItems)
            {
                _logger.LogDebug("Step 9: Refreshing Jellyfin libraries");
                result.Refresh = new RefreshPlan
                {
                    // Added items trigger create refresh, hidden items trigger remove refresh
                    CreateRefresh = hasUnhiddenItems,
                    RemoveRefresh = hasHiddenItems,
                    RefreshImages = false
                };
            }

            // Step 9: Save results
            result.Success = true;
            result.Message = "✅ Sync to Jellyseerr completed successfully";
            
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

