using Jellyfin.Plugin.JellyBridge.BridgeModels;
using Jellyfin.Plugin.JellyBridge.JellyseerrModel;
using Jellyfin.Plugin.JellyBridge.Utils;
using Microsoft.Extensions.Logging;
using MediaBrowser.Controller.Entities;
using JellyfinUser = Jellyfin.Data.Entities.User;

namespace Jellyfin.Plugin.JellyBridge.Services;

/// <summary>
/// Service for managing favorites synchronization between Jellyfin and Jellyseerr.
/// </summary>
public class FavoriteService
{
    private readonly DebugLogger<FavoriteService> _logger;
    private readonly ApiService _apiService;

    public FavoriteService(ILogger<FavoriteService> logger, ApiService apiService)
    {
        _logger = new DebugLogger<FavoriteService>(logger);
        _apiService = apiService;
    }
    #region ToJellyseerr

    #region Created

    /// <summary>
    /// Create requests for favorited bridge-only items.
    /// These are items that exist only in the Jellyseerr bridge folder and are favorited by users.
    /// </summary>
    public async Task<List<JellyseerrMediaRequest>> RequestFavorites(
        List<(BaseItem item, JellyseerrUser firstUser)> uniqueItemsWithFirstUser)
    {
        var requestResults = new List<JellyseerrMediaRequest>();
        
        // Step 5 & 6: Process unique bridge-only items with their first favorited user
        
        foreach (var (item, firstUser) in uniqueItemsWithFirstUser)
        {
            try
            {
                var tmdbId = JellyfinHelper.GetTmdbId(item);
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
                        ["userId"] = firstUser.Id,
                        ["seasons"] = "all" // Only for seasons, but doesn't stop it from working for movies
                    };
                    
                    _logger.LogTrace("Processing Jellyseerr bridge item: {ItemName} (TMDB ID: {TmdbId}) for user {UserName}", 
                        item.Name, tmdbId.Value, firstUser.JellyfinUsername);
                    
                    var requestResult = await _apiService.CallEndpointAsync(JellyseerrEndpoint.CreateRequest, parameters: requestParams);
                    var request = requestResult as JellyseerrMediaRequest;
                    if (request != null)
                    {
                        requestResults.Add(request);
                        _logger.LogTrace("Successfully created request for {ItemName} on behalf of {UserName}", 
                            item.Name, firstUser.JellyfinUsername);
                    }
                    else
                    {
                        _logger.LogError("Received no response from Jellyseerr for item request {ItemName} on behalf of {UserName}", 
                            item.Name, firstUser.JellyfinUsername);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to create request for {ItemName} on behalf of {UserName}", 
                        item.Name, firstUser.JellyfinUsername);
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
    public List<(BaseItem item, JellyseerrUser firstUser)> EnsureFirstJellyseerrUser(
        List<BaseItem> bridgeOnlyItems, 
        Dictionary<JellyfinUser, List<BaseItem>> allFavorites, 
        List<JellyseerrUser> jellyseerrUsers)
    {
        var uniqueItemsWithFirstUser = new List<(BaseItem item, JellyseerrUser firstUser)>();
        var processedItemIds = new HashSet<Guid>(); // Track which items we've already processed by ID
        
        // Create a lookup for Jellyseerr users by their Jellyfin user ID for quick access
        var jellyseerrUserLookup = jellyseerrUsers
            .Where(u => !string.IsNullOrEmpty(u.JellyfinUserGuid))
            .ToDictionary(u => u.JellyfinUserGuid!, u => u);
        
        _logger.LogTrace("Jellyseerr user lookup created with {Count} users: {UserLookup}", 
            jellyseerrUserLookup.Count, 
            string.Join(", ", jellyseerrUserLookup.Select(kvp => $"{kvp.Key}->{kvp.Value.JellyfinUsername}")));
        
        // Loop through favorites to find matching bridge-only items
        foreach (var (jellyfinUser, favoriteItems) in allFavorites)
        {
            // Check if this Jellyfin user has a corresponding Jellyseerr account
            if (!jellyseerrUserLookup.TryGetValue(jellyfinUser.Id.ToString(), out var jellyseerrUser))
            {
                _logger.LogWarning("Jellyfin user '{JellyfinUsername}' (ID: {JellyfinUserId}) does not have a corresponding Jellyseerr account - skipping favorites", 
                    jellyfinUser.Username, jellyfinUser.Id);
                continue; // Skip users without Jellyseerr accounts
            }
            
            // Check each favorited item to see if it matches a bridge-only item
            foreach (var favoriteItem in favoriteItems)
            {
                // Find bridge-only items that match this favorited item
                foreach (var bridgeItem in bridgeOnlyItems)
                {
                    if (!JellyfinHelper.ItemsMatch(favoriteItem, bridgeItem)) continue;
                    
                    // Only process each unique item once (by ID)
                    if (processedItemIds.Contains(bridgeItem.Id))
                    {
                        _logger.LogDebug("Item {ItemName} was already favorited by another user - skipping favorite for user {UserName}", 
                            bridgeItem.Name, jellyseerrUser.JellyfinUsername);
                        continue;
                    }
                    
                    // Found the first Jellyseerr user for this bridge-only item
                    processedItemIds.Add(bridgeItem.Id);
                    uniqueItemsWithFirstUser.Add((bridgeItem, jellyseerrUser));
                    
                    _logger.LogTrace("Found first Jellyseerr user {UserName} (Jellyfin: {JellyfinUser}) for item {ItemName}", 
                        jellyseerrUser.JellyfinUsername, jellyfinUser.Username, bridgeItem.Name);
                    break; // Move to next favorite item
                }
            }
        }
        
        return uniqueItemsWithFirstUser;
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
    /// Get existing requests from Jellyseerr to avoid duplicates.
    /// </summary>
    private async Task<List<JellyseerrMediaRequest>> GetExistingRequestsAsync()
    {
            try
            {
                var requestsResult = await _apiService.CallEndpointAsync(JellyseerrEndpoint.ReadRequests);
                if (requestsResult is List<JellyseerrMediaRequest> requests)
                {
                _logger.LogDebug("Fetched {RequestCount} existing requests from Jellyseerr", requests.Count);
                return requests;
            }
            else
            {
                    _logger.LogWarning("Failed to fetch requests from Jellyseerr - unexpected result type");
                return new List<JellyseerrMediaRequest>();
            }
                }
                catch (Exception ex)
                {
                _logger.LogError(ex, "Failed to fetch requests from Jellyseerr");
            return new List<JellyseerrMediaRequest>();
        }
    }

    #endregion

    #endregion

}