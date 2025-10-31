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

    public FavoriteService(ILogger<FavoriteService> logger, ApiService apiService, BridgeService bridgeService, MetadataService metadataService, JellyfinIUserDataManager userDataManager, JellyfinILibraryManager libraryManager)
    {
        _logger = new DebugLogger<FavoriteService>(logger);
        _apiService = apiService;
        _bridgeService = bridgeService;
        _metadataService = metadataService;
        _userDataManager = userDataManager;
        _libraryManager = libraryManager;
    }
    #region ToJellyseerr

    /// <summary>
    /// Filter favorites by removing items that are not in the JellyBridge folder.
    /// </summary>
    /// <param name="favoritesByUser">The favorites by user</param>
    /// <returns>The filtered favorites in a flat list of (user, item) pairs</returns>
    public List<(JellyfinUser user, IJellyfinItem item)> PreprocessFavorites(
        Dictionary<JellyfinUser, List<IJellyfinItem>> favoritesByUser)
    {
        if (favoritesByUser == null || favoritesByUser.Count == 0)
        {
            return new List<(JellyfinUser, IJellyfinItem)>();
        }

        var flattened = favoritesByUser
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
    /// </summary>
    public async Task<List<(IJellyfinItem item, JellyseerrMediaRequest request)>> RequestFavorites(
        List<(JellyseerrUser user, IJellyfinItem item)> favoritesWithUser)
    {
        var requestResults = new List<(IJellyfinItem item, JellyseerrMediaRequest request)>();
        var createAllFavorites = Plugin.GetConfigOrDefault<bool>(nameof(PluginConfiguration.CreateAllUserFavorites));

        // Default: use all tuples; if setting is false, reduce to first user per unique item
        List<(JellyseerrUser user, IJellyfinItem item)> allFavorites = new List<(JellyseerrUser, IJellyfinItem)>();
        if (createAllFavorites)
        {
            allFavorites = favoritesWithUser;
        }
        else
        {
            var seen = new HashSet<Guid>();
            foreach (var (jUser, jItem) in favoritesWithUser)
            {
                if (jItem == null) continue;
                if (seen.Contains(jItem.Id)) continue;
                seen.Add(jItem.Id);
                allFavorites.Add((jUser, jItem));
            }
        }

        foreach (var (user, item) in allFavorites)
        {
            try
            {
                var tmdbId = item.GetTmdbId();
                if (!tmdbId.HasValue)
                {
                    _logger.LogError("Skipping item {ItemName} - no TMDB ID found", item.Name);
                    continue;
                }
                
                var mediaType = IJellyseerrItem.GetMediaType(item).ToString().ToLower();
                
                // Create request for the user who favorited this item
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
                        item.Name, tmdbId.Value, user.JellyfinUsername);
                    
                    var requestResult = await _apiService.CallEndpointAsync(JellyseerrEndpoint.CreateRequest, parameters: requestParams);
                    var request = requestResult as JellyseerrMediaRequest;
                    if (request != null)
                    {
                        requestResults.Add((item, request));
                        _logger.LogTrace("Successfully created request for {ItemName} on behalf of {UserName}", 
                            item.Name, user.JellyfinUsername);
                    }
                    else
                    {
                        _logger.LogError("Received no response from Jellyseerr for item request {ItemName} on behalf of {UserName}", 
                            item.Name, user.JellyfinUsername);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to create request for {ItemName} on behalf of {UserName}", 
                        item.Name, user.JellyfinUsername);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to process Jellyseerr bridge item: {ItemName}", item.Name);
            }
        }
        
        if (requestResults.Count == 0)
        {
            _logger.LogDebug("No favorited Jellyseerr bridge items found");
        }
        else
        {
            _logger.LogDebug("Found {FavoritedCount} favorited Jellyseerr bridge items and created requests", requestResults.Count);
        }
        
        return requestResults;
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
        
        // Create a lookup for Jellyseerr users by their Jellyfin user ID for quick access
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
    /// </summary>
    public async Task<List<JellyseerrUser>> GetJellyseerrUsersAsync()
    {
        try
        {
            var usersResult = await _apiService.CallEndpointAsync(JellyseerrEndpoint.UserList);
            if (usersResult is List<JellyseerrUser> users)
            {
                _logger.LogDebug("Fetched {UserCount} users from Jellyseerr", users.Count);
                return users;
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
    public async Task<List<(JellyfinUser user, IJellyfinItem item)>> FilterRequestsFromFavorites(
        List<(JellyfinUser user, IJellyfinItem item)> allFavorites)
    {
        try
        {
            var requestsResult = await _apiService.CallEndpointAsync(JellyseerrEndpoint.ReadRequests);
            if (requestsResult == null)
            {
                _logger.LogError("Jellyseerr requests endpoint returned null response");
                return allFavorites; // Fail-safe: don't drop any favorites if API response is null
            }

            var jellyseerrRequests = (List<JellyseerrMediaRequest>)requestsResult;

            _logger.LogDebug("Fetched {RequestCount} existing requests from Jellyseerr", jellyseerrRequests.Count);

            // Build lookups by (MediaType, TmdbId)
            var requestedLookup = new HashSet<(JellyseerrModel.MediaType type, int tmdbId)>(
                jellyseerrRequests
                    .Where(r => r != null && r.Status != JellyseerrModel.MediaRequestStatus.DECLINED && r.Media != null)
                    .Select(r => (r.Media.MediaType, r.Media.TmdbId)));

            var removedLogList = new List<string>();
            // Only retain pairs whose item is NOT already requested
            var filtered = allFavorites.Where(fav => {
                var tmdb = fav.item.GetTmdbId();
                var type = IJellyseerrItem.GetMediaType(fav.item);
                var isRequested = tmdb.HasValue && requestedLookup.Contains((type, tmdb.Value));
                if (isRequested)
                {
                    removedLogList.Add(tmdb.HasValue
                        ? $"{fav.item.Name} (TMDB {tmdb.Value}, {type})"
                        : $"{fav.item.Name} ({type})");
                }
                return !isRequested;
            }).ToList();
            if (removedLogList.Count > 0)
            {
                _logger.LogTrace("Filtered {Count} requested favorite items: {Items}", removedLogList.Count, string.Join(", ", removedLogList));
            }
            _logger.LogDebug("Found {UnrequestedCount} unrequested favorite items, filtered from {TotalCount} total", filtered.Count, allFavorites.Count);
            return filtered;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to filter favorites using Jellyseerr requests");
            // On failure, return the original list to avoid dropping user selections
            return allFavorites;
        }
    }

    #endregion

    // Removed legacy user-data removal code in favor of .ignore marker approach

    #region Removed

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

            var declined = allRequests.Where(r => r.Status == JellyseerrModel.MediaRequestStatus.DECLINED).ToList();
            if (declined.Count == 0)
            {
                return declinedItems;
            }

            _logger.LogDebug("Found {DeclinedCount} declined Jellyseerr requests; removing .ignore markers", declined.Count);

            var (moviesMeta, showsMeta) = await _metadataService.ReadMetadataAsync();
            var allMeta = new List<IJellyseerrItem>();
            allMeta.AddRange(moviesMeta);
            allMeta.AddRange(showsMeta);

            declinedItems = FindJellyseerrFavorites(allMeta, declined);
            foreach (var item in declinedItems)
            {
                try
                {
                    var dir = _metadataService.GetJellyseerrItemDirectory(item);
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
        return declinedItems;
    }

    /// <summary>
    /// For each favorited item that was requested in Jellyseerr, create an .ignore marker and unmark it as favorite for the user.
    /// </summary>
    public async Task<List<IJellyfinItem>> UnmarkAndIgnoreRequestedAsync(List<(JellyfinUser user, IJellyfinItem item)> favorites)
    {
        var affectedItems = new List<IJellyfinItem>();

        // Respect configuration: if removal from favorites is disabled, do nothing
        var removeRequested = Plugin.GetConfigOrDefault<bool>(nameof(PluginConfiguration.RemoveRequestedFromFavorites));
        if (!removeRequested)
        {
            _logger.LogTrace("RemoveRequestedFromFavorites disabled; skipping unfavorite and ignore creation.");
            return affectedItems;
        }

        try
        {

            // Build lookup of itemId -> users who favorited it
            var itemIdToUsers = favorites
                .GroupBy(f => f.item.Id)
                .ToDictionary(g => g.Key, g => g.Select(x => x.user).ToList());

            // Use LibraryScanAsync to find matches between provided Jellyfin items and Jellyseerr metadata
            var jellyfinItems = favorites.Select(f => f.item).ToList();
            var (matches, _) = await _bridgeService.LibraryScanAsync(jellyfinItems);

            // Create ignore files for those matches
            await _bridgeService.CreateIgnoreFilesAsync(matches);

            // Unfavorite for each user that had this item favorited (via wrappers only)
            foreach (var match in matches)
            {
                var jfItem = match.JellyfinItem;
                affectedItems.Add(jfItem);

                if (!itemIdToUsers.TryGetValue(jfItem.Id, out var users))
                {
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
                        var updated = _userDataManager.TrySetFavorite(jfUser, jfItem, false, _libraryManager);
                        if (updated){
                            _logger.LogTrace("Unfavorited '{ItemName}' for user '{UserName}'", jfItem.Name, jfUser.Username);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to unfavorite '{ItemName}' for a user", jfItem?.Name);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed processing favorited items for unmark-and-ignore");
        }
        return affectedItems;
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
        List<IJellyseerrItem> items,
        List<JellyseerrMediaRequest> requests)
    {
        var results = new List<IJellyseerrItem>();
        if (items == null || items.Count == 0 || requests == null || requests.Count == 0)
        {
            return results;
        }

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