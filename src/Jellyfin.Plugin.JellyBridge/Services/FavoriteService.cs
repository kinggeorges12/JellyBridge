using Jellyfin.Plugin.JellyBridge.BridgeModels;
using Jellyfin.Plugin.JellyBridge.JellyseerrModel;
using Jellyfin.Plugin.JellyBridge.Utils;
using Jellyfin.Plugin.JellyBridge.JellyfinModels;
using Microsoft.Extensions.Logging;
using System.IO;
using Jellyfin.Plugin.JellyBridge.Configuration;

namespace Jellyfin.Plugin.JellyBridge.Services;

/// <summary>
/// Service for managing favorites synchronization between Jellyfin and Jellyseerr.
/// </summary>
public class FavoriteService
{
    private readonly DebugLogger<FavoriteService> _logger;
    private readonly ApiService _apiService;
    private readonly BridgeService _bridgeService;
    private readonly MetadataService _metadataService;
    private readonly JellyfinIUserDataManager _userDataManager;
    private readonly JellyfinILibraryManager _libraryManager;
    private readonly JellyfinIUserManager _userManager;

    public FavoriteService(ILogger<FavoriteService> logger, ApiService apiService, BridgeService bridgeService, MetadataService metadataService, JellyfinIUserDataManager userDataManager, JellyfinILibraryManager libraryManager, JellyfinIUserManager userManager)
    {
        _logger = new DebugLogger<FavoriteService>(logger);
        _apiService = apiService;
        _bridgeService = bridgeService;
        _metadataService = metadataService;
        _userDataManager = userDataManager;
        _libraryManager = libraryManager;
        _userManager = userManager;
    }
    #region ToJellyseerr

    /// <summary>
    /// Get all user favorites from Jellyfin and filter to only items in the JellyBridge folder.
    /// </summary>
    /// <returns>The filtered favorites in a flat list of (user, item) pairs</returns>
    public List<(JellyfinUser user, IJellyfinItem item)> GetUserFavorites()
    {
        // Get all Jellyfin users and their favorites
        var allFavoritesDict = _userDataManager.GetUserFavorites<IJellyfinItem>(_userManager, _libraryManager);
        _logger.LogDebug("Retrieved favorites for {UserCount} users", allFavoritesDict.Count);
        foreach (var (user, favorites) in allFavoritesDict)
        {
            _logger.LogTrace("User '{UserName}' has {FavoriteCount} favorites: {FavoriteNames}", 
                user.Username, favorites.Count, 
                string.Join(", ", favorites.Select(f => f.Name)));
        }

        // Flatten and filter to only items in the JellyBridge folder
        var flattened = allFavoritesDict
            .SelectMany(kv => kv.Value.Select(item => (kv.Key, item)))
            .ToList();

        var filtered = flattened.Where(fav =>
        {
            var path = fav.item?.Path;
            return !string.IsNullOrEmpty(path) && FolderUtils.IsPathInSyncDirectory(path);
        }).ToList();

        _logger.LogDebug("Filtered favorites to {Count} items in JellyBridge folder (from {Total})",
            filtered.Count, flattened.Count);

        return filtered;
    }

    #region Created

    /// <summary>
    /// Create requests for favorited bridge-only items.
    /// These are items that exist only in the Jellyseerr bridge folder and are favorited by users.
    /// Randomly assigns users to items and only marks items as processed after successful API responses.
    /// </summary>
    public async Task<(List<(IJellyfinItem item, JellyseerrMediaRequest request)> processed, List<IJellyfinItem> blocked)> RequestFavorites(
        List<(JellyseerrUser user, IJellyfinItem item)> favoritesWithUser)
    {
        var requestResults = new List<(IJellyfinItem item, JellyseerrMediaRequest request)>();
        var blockedItems = new List<IJellyfinItem>();
        var successfullyProcessedItems = new HashSet<Guid>(); // Track items that have been successfully requested

        // Randomize the order of tuples
        var random = new Random();
        var shuffledFavorites = favoritesWithUser
            .Where(f => f.item != null)
            .OrderBy(_ => random.Next())
            .ToList();

        foreach (var (user, item) in shuffledFavorites)
        {
            // Skip if this item has already been successfully processed
            if (successfullyProcessedItems.Contains(item.Id))
            {
                continue;
            }

            // Get TMDB ID and media type
            var tmdbId = item.GetTmdbId();
            if (!tmdbId.HasValue)
            {
                _logger.LogError("Skipping item {ItemName} - no TMDB ID found", item.Name);
                continue;
            }
            
            var mediaType = IJellyseerrItem.GetMediaType(item).ToString().ToLower();

            try
            {
                var requestParams = new Dictionary<string, object>
                {
                    ["mediaType"] = mediaType,
                    ["mediaId"] = tmdbId.Value,
                    ["userId"] = user.Id,
                    ["seasons"] = "all" // Only for seasons, but doesn't stop it from working for movies
                };
                
                _logger.LogTrace("Processing Jellyseerr bridge item: {ItemName} (TMDB ID: {TmdbId}) for user {UserName}", 
                    item.Name, tmdbId.Value, user.JellyfinUsername ?? user.Username ?? "Unknown");
                
                var requestResult = await _apiService.CallEndpointAsync(JellyseerrEndpoint.CreateRequest, parameters: requestParams);
                var request = requestResult as JellyseerrMediaRequest;
                
                // Check if request is valid by verifying it has an ID (successful requests always have an ID > 0)
                if (request != null && request.Id != 0)
                {
                    // Only mark as successfully processed after receiving a valid response
                    successfullyProcessedItems.Add(item.Id);
                    requestResults.Add((item, request));
                    _logger.LogTrace("Successfully created request for {ItemName} on behalf of {UserName}", 
                        item.Name, user.JellyfinUsername ?? user.Username ?? "Unknown");
                }
                else
                {
                    // API returned error/default object (e.g., quota exceeded, forbidden, etc.)
                    _logger.LogWarning("Failed to create request for {ItemName} on behalf of {UserName} - no valid response from Jellyseerr", 
                        item.Name, user.JellyfinUsername ?? user.Username ?? "Unknown");
                    blockedItems.Add(item);
                }
            }
            catch (Exception ex)
            {
                // If request creation fails (e.g., network error, quota exceeded exception), log and continue
                _logger.LogWarning(ex, "Failed to create request for {ItemName} on behalf of {UserName}", 
                    item.Name, user.JellyfinUsername ?? user.Username ?? "Unknown");
                // Add to blocked items so they appear in the frontend
                blockedItems.Add(item);
            }
        }
        
        if (requestResults.Count == 0)
        {
            _logger.LogDebug("No favorited Jellyseerr bridge items found or successfully requested");
        }
        else
        {
            _logger.LogDebug("Successfully created {FavoritedCount} requests for favorited Jellyseerr bridge items", requestResults.Count);
        }
        
        return (requestResults, blockedItems);
    }

    #endregion

    #region Found

    /// <summary>
    /// Group bridge-only items by TMDB ID and find first Jellyseerr user who favorited each.
    /// These are items that exist only in the Jellyseerr bridge folder (not in main Jellyfin library).
    /// </summary>
    public List<(JellyseerrUser user, IJellyfinItem item)> EnsureJellyseerrUser(
        List<(JellyfinUser user, IJellyfinItem item)> allFavorites, 
        List<JellyseerrUser> jellyseerrUsers)
    {
        var favoritesWithJellyseerrUser = new List<(JellyseerrUser user, IJellyfinItem item)>();
        
        // Create a lookup hashtable for Jellyseerr users by their Jellyfin user ID for quick access
        // Note: GetJellyseerrUsersAsync already filters duplicates, so we can safely create the dictionary
        var jellyseerrUserLookup = jellyseerrUsers
            .Where(u => !string.IsNullOrEmpty(u.JellyfinUserGuid))
            .ToDictionary(u => u.JellyfinUserGuid!, u => u);
        
        _logger.LogTrace("Jellyseerr user lookup created with {Count} users: {UserLookup}", 
            jellyseerrUserLookup.Count, 
            string.Join(", ", jellyseerrUserLookup.Select(kvp => $"{kvp.Key}->{kvp.Value.JellyfinUsername}")));
        
        // Loop through all favorites (flat tuple list: each is (user, item)); do not filter by users or dedupe items
        foreach (var (jellyfinUser, favoriteItem) in allFavorites)
        {
            if (!jellyseerrUserLookup.TryGetValue(jellyfinUser.Id.ToString(), out var jellyseerrUser))
            {
                _logger.LogWarning("Jellyfin user '{JellyfinUsername}' (ID: {JellyfinUserId}) does not have a corresponding Jellyseerr account - skipping favorite", 
                    jellyfinUser.Username, jellyfinUser.Id);
                continue;
            }
            favoritesWithJellyseerrUser.Add((jellyseerrUser, favoriteItem));
        }

        return favoritesWithJellyseerrUser;
    }

    #endregion

    #region Processed

    /// <summary>
    /// Get all Jellyseerr users to map with Jellyfin users.
    /// Filters out duplicate JellyfinUserGuid values, keeping only the first occurrence.
    /// </summary>
    public async Task<List<JellyseerrUser>> GetJellyseerrUsersAsync()
    {
        try
        {
            var usersResult = await _apiService.CallEndpointAsync(JellyseerrEndpoint.UserList);
            if (usersResult is List<JellyseerrUser> users)
            {
                _logger.LogDebug("Fetched {UserCount} users from Jellyseerr", users.Count);
                
                // Filter out duplicate JellyfinUserGuid values, keeping only the first occurrence
                var uniqueUsers = users
                    .Where(u => !string.IsNullOrEmpty(u.JellyfinUserGuid))
                    .GroupBy(u => u.JellyfinUserGuid!)
                    .Select(g => 
                    {
                        if (g.Count() > 1)
                        {
                            _logger.LogWarning("Found {Count} Jellyseerr users with duplicate JellyfinUserGuid '{Guid}': {Usernames}. Using the first one.",
                                g.Count(), 
                                g.Key,
                                string.Join(", ", g.Select(u => u.JellyfinUsername)));
                        }
                        return g.First();
                    })
                    .ToList();
                
                _logger.LogDebug("Filtered to {UniqueCount} unique users with JellyfinUserGuid", uniqueUsers.Count);
                return uniqueUsers;
            }
            else
            {
                _logger.LogWarning("Failed to fetch users from Jellyseerr - unexpected result type");
                return new List<JellyseerrUser>();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch users from Jellyseerr");
            return new List<JellyseerrUser>();
        }
    }

    /// <summary>
    /// Filter favorites by removing items that already have active (non-declined) Jellyseerr requests.
    /// Declined requests do NOT count as existing and are treated as unrequested.
    /// </summary>
    public async Task<(
        List<(JellyfinUser user, IJellyfinItem item)> requested,
        List<(JellyfinUser user, IJellyfinItem item)> pending)> FilterRequestsFromFavorites(
        List<(JellyfinUser user, IJellyfinItem item)> allFavorites)
    {
        var requested = new List<(JellyfinUser user, IJellyfinItem item)>();
        var pending = new List<(JellyfinUser user, IJellyfinItem item)>();
        try
        {
            var requestsResult = await _apiService.CallEndpointAsync(JellyseerrEndpoint.ReadRequests);
            if (requestsResult == null)
            {
                _logger.LogError("Jellyseerr requests endpoint returned null response");
                return (requested, pending);
            }

            var jellyseerrRequests = (List<JellyseerrMediaRequest>)requestsResult;

            _logger.LogDebug("Fetched {RequestCount} existing requests from Jellyseerr", jellyseerrRequests.Count);

            // Build lookups by (MediaType, TmdbId)
            var requestedLookup = new HashSet<(JellyseerrModel.MediaType type, int tmdbId)>(
                jellyseerrRequests
                    .Where(r => r != null && r.Status != JellyseerrModel.MediaRequestStatus.DECLINED && r.Media != null)
                    .Select(r => (r.Media.MediaType, r.Media.TmdbId)));
            var requestedLog = new List<string>();

            foreach (var fav in allFavorites)
            {
                var tmdb = fav.item.GetTmdbId();
                var type = IJellyseerrItem.GetMediaType(fav.item);
                var isRequested = tmdb.HasValue && requestedLookup.Contains((type, tmdb.Value));
                if (isRequested)
                {
                    requested.Add(fav);
                    requestedLog.Add(tmdb.HasValue
                        ? $"{fav.item.Name} (TMDB {tmdb.Value}, {type})"
                        : $"{fav.item.Name} ({type})");
                }
                else
                {
                    pending.Add(fav);
                }
            }

            if (requestedLog.Count > 0)
            {
                _logger.LogTrace("Identified {Count} already-requested favorite items: {Items}", requestedLog.Count, string.Join(", ", requestedLog));
            }
            _logger.LogDebug("Split favorites into {Requested} requested and {Pending} pending (total {Total})", requested.Count, pending.Count, allFavorites.Count);
            return (requested, pending);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to filter favorites using Jellyseerr requests");
        }
        return (requested, pending);
    }

    #endregion

    // Removed legacy user-data removal code in favor of .ignore marker approach

    #region Removed


    /// <summary>
    /// For each favorited item that was requested in Jellyseerr, create an .ignore marker and unmark it as favorite for the user.
    /// Uses GetUserFavorites to find all existing favorites, creates ignore files for all matched Jellyfin items,
    /// and unfavorites items only if they are in the bridge folder or not in any folder.
    /// Also marks items as unplayed (unwatched) for all users who had favorited them.
    /// Returns the list of newly ignored Jellyseerr items.
    /// </summary>
public async Task<(List<IJellyseerrItem> newIgnored, List<IJellyseerrItem> cleared)> ClearAndIgnoreRequestedAsync()
    {
        var newIgnored = new List<IJellyseerrItem>();
        var cleared = new List<IJellyseerrItem>();

        var removeRequested = Plugin.GetConfigOrDefault<bool>(nameof(PluginConfiguration.RemoveRequestedFromFavorites));

        try
        {
            // Get all existing favorites
            var allFavorites = GetUserFavorites();
			_logger.LogDebug("Fetched {FavoriteCount} favorites for processing", allFavorites.Count);
            var (requestedFavorites, _) = await FilterRequestsFromFavorites(allFavorites);
			_logger.LogDebug("Identified {RequestedCount} favorites with existing Jellyseerr requests", requestedFavorites.Count);
            // Build lookup of itemId -> users who favorited it
            var itemIdToUsers = requestedFavorites
                .GroupBy(f => f.item.Id)
                .ToDictionary(g => g.Key, g => g.Select(x => x.user).ToList());

            // Find matches between Jellyfin items and Jellyseerr metadata
            var jellyfinItems = requestedFavorites.Select(f => f.item).ToList();
			var (matches, _) = await _bridgeService.LibraryScanAsync(jellyfinItems);
			_logger.LogDebug("Library scan produced {MatchCount} matches for ignore/unfavorite", matches.Count);

            // Create ignore files for all matched Jellyfin items (always done, regardless of RemoveRequestedFromFavorites setting)
			var (newIgnoredMatches, existingIgnored) = await _bridgeService.IgnoreMatchAsync(matches);
			newIgnored.AddRange(newIgnoredMatches);
			_logger.LogTrace("Created/updated ignore files for matched items ({NewlyIgnored} newly ignored, {ExistingIgnored} already ignored)", 
                newIgnoredMatches.Count, existingIgnored.Count);

            // Collect tasks for unfavoriting and marking as unplayed
            var unfavoriteTasks = new List<(Task<bool>? favoriteTask, Task<JellyfinWrapperResult> playStatusTask, JellyfinUser user, IJellyfinItem item, IJellyseerrItem jellyseerrItem)>();

            // Process all items for marking as unplayed, but only unfavorite items in bridge folder or with no path
            foreach (var match in matches)
            {
                var jfItem = match.JellyfinItem;
                var itemPath = jfItem.Path;
                
                // Check if item should be unfavorited: in bridge folder OR not in any folder
                var isInBridgeFolder = !string.IsNullOrEmpty(itemPath) && FolderUtils.IsPathInSyncDirectory(itemPath);
                var isNotInAnyFolder = string.IsNullOrEmpty(itemPath);
                
                // Only unfavorite items that are in bridge folder or have no path
                if (!isInBridgeFolder && !isNotInAnyFolder)
                {
                    // Skip unfavoriting items that are in other folders (not bridge, not null/empty)
                    continue;
                }

                if (!itemIdToUsers.TryGetValue(jfItem.Id, out var users))
                {
                    _logger.LogTrace("Skipping processing for '{ItemName}' - no users found who favorited this item", jfItem.Name);
                    continue;
                }

                foreach (var jfUser in users)
                {
                    try
                    {
                        if (jfItem == null)
                        {
                            continue;
                        }
                        // Create tasks for unfavoriting (only if enabled) and marking as unplayed (always)
                        Task<bool>? favoriteTask = null;
                        if (removeRequested)
                        {
                            favoriteTask = _userDataManager.TryUnfavoriteAsync(_libraryManager, jfUser, jfItem);
                        }
                        var playStatusTask = _userDataManager.MarkItemPlayStatusAsync(jfUser, jfItem, markAsPlayed: false);
                        unfavoriteTasks.Add((favoriteTask, playStatusTask, jfUser, jfItem, match.JellyseerrItem));
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to create tasks for processing '{ItemName}' for a user", jfItem?.Name);
                    }
                }
            }

            // Await all tasks at once
            try
            {
                var allTasks = unfavoriteTasks.SelectMany(t => 
                {
                    if (t.favoriteTask != null)
                    {
                        return new Task[] { t.favoriteTask, t.playStatusTask };
                    }
                    return new Task[] { t.playStatusTask };
                });
                await Task.WhenAll(allTasks).ConfigureAwait(false);
            }
            catch (AggregateException ex)
            {
                _logger.LogWarning(ex, "Some unfavorite/play status tasks failed. Processing all results individually.");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Exception while awaiting unfavorite/play status tasks. Processing all results individually.");
            }

            // Process results - return value is based on whether favorite was updated
            foreach (var (favoriteTask, playStatusTask, user, item, jellyseerrItem) in unfavoriteTasks)
            {
                try
                {
                    // Check favorite task status (only if RemoveRequestedFromFavorites is enabled and task was created)
                    if (favoriteTask != null)
                    {
                        if (!favoriteTask.IsFaulted && !favoriteTask.IsCanceled)
                        {
                            var favoriteUpdated = favoriteTask.Result;
                            if (favoriteUpdated)
                            {
                                _logger.LogTrace("Unfavorited '{ItemName}' for user '{UserName}'", 
                                    item.Name, user.Username);
                                cleared.Add(jellyseerrItem);
                            }
                            else
                            {
                                _logger.LogTrace("Favorite was not updated for '{ItemName}' for user '{UserName}' (may already be unfavorited)", 
                                    item.Name, user.Username);
                            }
                        }
                        else
                        {
                            _logger.LogWarning(favoriteTask.Exception?.GetBaseException() ?? new Exception("Task was canceled"), 
                                favoriteTask.Exception?.GetBaseException()?.Message ?? "Task was canceled");
                        }
                    }
                    
                    // Check play status task status
                    if (!playStatusTask.IsFaulted && !playStatusTask.IsCanceled)
                    {
                        var playStatusResult = playStatusTask.Result;
                        if (playStatusResult.Success)
                        {
                            _logger.LogTrace("Marked '{ItemName}' as unplayed for user '{UserName}'", 
                                item.Name, user.Username);
                        }
                        else
                        {
                            _logger.LogTrace("Play status was not updated for '{ItemName}' for user '{UserName}' (may already be unplayed): {Message}", 
                                item.Name, user.Username, playStatusResult.Message);
                        }
                    }
                    else
                    {
                        _logger.LogWarning(playStatusTask.Exception?.GetBaseException() ?? new Exception("Task was canceled"), 
                            playStatusTask.Exception?.GetBaseException()?.Message ?? "Task was canceled");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to process result for unfavoriting '{ItemName}' for user '{UserName}'", item?.Name, user?.Username);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed processing favorited items");
        }
		
        return (newIgnored, cleared);
    }

    /// <summary>
    /// Remove .ignore markers for Jellyseerr requests that have been declined.
    /// Uses FindJellyseerrFavorite to resolve the matching metadata item for each request.
    /// </summary>
    public async Task<List<IJellyseerrItem>> UnignoreDeclinedRequests()
    {
        var removedIgnoreCount = 0;
        var declinedItems = new List<IJellyseerrItem>();
        try
        {
            var requestsObj = await _apiService.CallEndpointAsync(JellyseerrEndpoint.ReadRequests);
            var allRequests = requestsObj as List<JellyseerrMediaRequest> ?? new List<JellyseerrMediaRequest>();
			_logger.LogDebug("Fetched {RequestCount} total requests for decline-check", allRequests.Count);

            // Group all requests by media identity (Type + TMDB id)
			var requestsByMedia = allRequests
                .Where(r => r != null && r.Media != null && r.Media.TmdbId > 0)
                .GroupBy(r => (Type: r.Type!, TmdbId: r.Media!.TmdbId))
                .ToList();
			_logger.LogTrace("Grouped into {GroupCount} media groups for decline evaluation", requestsByMedia.Count);

            // Only consider media where ALL requests are declined
            var fullyDeclinedGroups = requestsByMedia
                .Where(g => g.All(r => r.Status == JellyseerrModel.MediaRequestStatus.DECLINED))
                .ToList();

			if (fullyDeclinedGroups.Count == 0)
            {
				_logger.LogTrace("No fully-declined media groups found; nothing to unignore");
                return declinedItems;
            }

            var representativeDeclinedRequests = fullyDeclinedGroups.Select(g => g.First()).ToList();

            _logger.LogDebug(
                "Found {DeclinedMediaCount} media with all requests declined; removing .ignore markers",
                representativeDeclinedRequests.Count);

            declinedItems = await FindJellyseerrFavorites(representativeDeclinedRequests);
            foreach (var item in declinedItems)
            {
                try
                {
                    var dir = _metadataService.GetJellyBridgeItemDirectory(item);
                    var ignorePath = Path.Combine(dir, BridgeService.IgnoreFileName);
                    if (File.Exists(ignorePath))
                    {
                        File.Delete(ignorePath);
                        removedIgnoreCount++;
                        _logger.LogTrace("Removed .ignore for declined request: {Dir}", dir);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed removing .ignore for declined favorite {Name}", item?.MediaName);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error removing .ignore files for declined Jellyseerr requests");
        }

        if (removedIgnoreCount > 0)
        {
            _logger.LogDebug("Removed {RemovedCount} .ignore files for declined requests", removedIgnoreCount);
        }
		_logger.LogDebug("Unignore declined requests complete. DeclinedItems={DeclinedCount}", declinedItems.Count);
        return declinedItems;
    }

    private async Task<List<IJellyseerrItem>> FindJellyseerrFavorites(
        List<JellyseerrMediaRequest> requests)
    {
		var (moviesMeta, showsMeta) = await _metadataService.ReadMetadataAsync();
        var allMeta = new List<IJellyseerrItem>();
        allMeta.AddRange(moviesMeta);
        allMeta.AddRange(showsMeta);
        return FindJellyseerrFavorites(requests, allMeta);
    }

    /// <summary>
    /// Find the corresponding Jellyseerr metadata item (movie or show) for a given request,
    /// using the request's TMDB ID and media type. The request's Media object may be unpopulated,
    /// so we rely on request.Type and request.Media.TmdbId when available.
    /// </summary>
    /// <param name="items">All discovered Jellyseerr metadata items</param>
    /// <param name="requests">Jellyseerr media requests to resolve</param>
    /// <returns>List of matching Jellyseerr items</returns>
    private List<IJellyseerrItem> FindJellyseerrFavorites(
        List<JellyseerrMediaRequest> requests,
        List<IJellyseerrItem>? items = null)
    {
		_logger.LogDebug("Reading Jellyseerr metadata for {RequestCount} requests", requests?.Count ?? 0);
        var results = new List<IJellyseerrItem>();
        if (items == null || items.Count == 0 || requests == null || requests.Count == 0)
        {
            return results;
        }

		// Log metadata item counts available for matching
		var movieCount = items.OfType<JellyseerrMovie>().Count();
		var showCount = items.OfType<JellyseerrShow>().Count();
		_logger.LogDebug("Loaded metadata items: Movies={MovieCount}, Shows={ShowCount}, Total={Total}",
			movieCount,
			showCount,
			movieCount + showCount);

        try
        {
            // Build lookups for faster matching
            var moviesByTmdb = items.OfType<JellyseerrMovie>().ToDictionary(m => m.Id, m => (IJellyseerrItem)m);
            var showsByTmdb = items.OfType<JellyseerrShow>().ToDictionary(s => s.Id, s => (IJellyseerrItem)s);

            foreach (var req in requests)
            {
                var tmdbId = req?.Media?.TmdbId;
                var mediaType = req?.Type;
                if (!tmdbId.HasValue || tmdbId.Value <= 0 || mediaType == null)
                {
                    continue;
                }

                if (mediaType == JellyseerrModel.MediaType.MOVIE && moviesByTmdb.TryGetValue(tmdbId.Value, out var movie))
                {
                    results.Add(movie);
                }
                else if (mediaType == JellyseerrModel.MediaType.TV && showsByTmdb.TryGetValue(tmdbId.Value, out var show))
                {
                    results.Add(show);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "FindJellyseerrFavorite failed for batch of {Count} requests", requests.Count);
        }

        return results;
    }

    #endregion

    #endregion

}