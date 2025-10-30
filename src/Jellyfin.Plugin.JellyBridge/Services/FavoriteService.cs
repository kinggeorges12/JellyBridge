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

    public FavoriteService(ILogger<FavoriteService> logger, ApiService apiService, BridgeService bridgeService)
    {
        _logger = new DebugLogger<FavoriteService>(logger);
        _apiService = apiService;
        _bridgeService = bridgeService;
    }
    #region ToJellyseerr

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
    /// Filter favorites by removing items that already have Jellyseerr requests.
    /// Returns: flat list of (user, item) pairs (removes favorites that have already been requested in Jellyseerr).
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

            // Build a lookup of existing requests by (MediaType, TmdbId) for O(1) checks
            var requestedLookup = new HashSet<(JellyseerrModel.MediaType type, int tmdbId)>(
                jellyseerrRequests
                    .Select(r => r.Media)
                    .Select(m => (m.MediaType, m.TmdbId)));

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
    /// Create .ignore files in the bridge item directories that were successfully requested.
    /// </summary>
    public async Task<List<IJellyfinItem>> IgnoreRequestedAsync(List<IJellyfinItem> jellyfinItems)
    {
        var writtenItems = new List<IJellyfinItem>();
        try
        {
            // Use LibraryScanAsync to find matches between provided Jellyfin items and Jellyseerr metadata
            var (matches, _) = await _bridgeService.LibraryScanAsync(jellyfinItems);

            // Create ignore files for those matches
            await _bridgeService.CreateIgnoreFilesAsync(matches);
            writtenItems = matches.Select(m => (IJellyfinItem)m.JellyfinItem).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process favorited items for .ignore creation via scan");
        }
        return writtenItems;
    }


    /// <summary>
    /// Find the corresponding Jellyseerr metadata item (movie or show) for a given request,
    /// using the request's TMDB ID and media type. The request's Media object may be unpopulated,
    /// so we rely on request.Type and request.Media.TmdbId when available.
    /// </summary>
    /// <param name="movies">All discovered Jellyseerr movies metadata</param>
    /// <param name="shows">All discovered Jellyseerr shows metadata</param>
    /// <param name="request">The Jellyseerr media request</param>
    /// <returns>The matching Jellyseerr item, or null if not found</returns>
    private IJellyseerrItem? FindJellyseerrFavorite(
        List<JellyseerrMovie> movies,
        List<JellyseerrShow> shows,
        JellyseerrMediaRequest? request)
    {
        if (request is null)
        {
            _logger.LogTrace("FindJellyseerrFavorite: request is null");
            return null;
        }

        var tmdbId = request.Media?.TmdbId;
        var mediaType = request.Type;

        if (!tmdbId.HasValue || tmdbId.Value <= 0)
        {
            _logger.LogTrace("FindJellyseerrFavorite: missing TMDB id on request (Type={Type})", mediaType);
            return null;
        }

        try
        {
            if (mediaType == JellyseerrModel.MediaType.MOVIE)
            {
                var match = movies.FirstOrDefault(m => m.Id == tmdbId.Value);
                if (match != null) return match;
            }
            else if (mediaType == JellyseerrModel.MediaType.TV)
            {
                var match = shows.FirstOrDefault(s => s.Id == tmdbId.Value);
                if (match != null) return match;
            }
            else
            {
                _logger.LogTrace("FindJellyseerrFavorite: unsupported media type {Type}", mediaType);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "FindJellyseerrFavorite failed for TMDB {TmdbId} ({Type})", tmdbId, mediaType);
        }

        _logger.LogTrace("FindJellyseerrFavorite: no match for TMDB {TmdbId} ({Type})", tmdbId, mediaType);
        return null;
    }

    #endregion

    #endregion

}