using Jellyfin.Plugin.JellyBridge.BridgeModels;
using Jellyfin.Plugin.JellyBridge.JellyseerrModel;
using Jellyfin.Plugin.JellyBridge.Utils;
using Jellyfin.Plugin.JellyBridge.JellyfinModels;
using Microsoft.Extensions.Logging;
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

        var useSeasonPlaceholders = Plugin.GetConfigOrDefault<bool>(nameof(PluginConfiguration.UseSeasonPlaceholders));
        var filtered = flattened.Where(fav =>
        {
            // Seasons can only be requested when season placeholders are enabled
            if (fav.item is JellyfinSeason && !useSeasonPlaceholders)
            {
                return false;
            }

            var path = fav.item?.Path;
            var inBridgeFolder = !string.IsNullOrEmpty(path) && FolderUtils.IsPathInSyncDirectory(path);
            // A missing season of a show in any library can also be favorited to request it (handled once, with the Discover library)
            var isMissingSeason = fav.item is JellyfinSeason virtualSeason && virtualSeason.IsVirtual && LibraryScope.Current?.ListType != DiscoverListType.Trending;
            if (!inBridgeFolder && !isMissingSeason)
            {
                return false;
            }
            // Only real seasons (> 0) can be requested; Specials (Season 00) holds the series promo
            if (fav.item is JellyfinSeason season && (!season.SeasonNumber.HasValue || season.SeasonNumber.Value <= 0))
            {
                _logger.LogDebug("Ignoring favorite on season '{SeasonName}' of '{SeriesName}' - only seasons > 0 can be requested",
                    season.Name, season.SeriesName);
                return false;
            }
            return true;
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
        var userPermissionRequest4k = Plugin.GetConfigOrDefault<bool>(nameof(PluginConfiguration.UserPermissionRequest4k));
        var requestResults = new List<(IJellyfinItem item, JellyseerrMediaRequest request)>();
        var blockedItems = new List<IJellyfinItem>();
        var successfullyProcessedItems = new HashSet<Guid>(); // Track items that have been successfully requested

        // Randomize the order of tuples
        var random = new Random();
        var shuffledFavorites = favoritesWithUser
            .OrderBy(_ => random.Next())
            .ToList();

        // Season favorites are requested after series/movies, combined per user and show
        var seasonFavorites = shuffledFavorites.Where(f => f.item is JellyfinSeason).ToList();
        shuffledFavorites = shuffledFavorites.Where(f => f.item is not JellyfinSeason).ToList();

        foreach (var (user, item) in shuffledFavorites)
        {
            if(user == null || item == null)
            {
                _logger.LogWarning("Skipping null user or item in favorites list");
                continue;
            }

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
            
            var username = user.JellyfinUsername ?? user.Username ?? "ID " + user.Id.ToString();
            var mediaType = IJellyseerrItem.GetMediaType(item).ToString().ToLower();
            var mediaPermission4k = IJellyseerrItem.GetMediaPermission4k(item);

            try
            {
                var requestParams = new Dictionary<string, object>
                {
                    ["mediaType"] = mediaType,
                    ["mediaId"] = tmdbId.Value,
                    ["userId"] = user.Id,
                };

                if(userPermissionRequest4k)
                {
                    requestParams["is4k"] = PermissionHelper.HasPermission(mediaPermission4k, user?.Permissions ?? 0);
                }
                
                _logger.LogTrace("Processing Jellyseerr bridge item: {ItemName} (TMDB ID: {TmdbId}) for user {UserName}", 
                    item.Name, tmdbId.Value, username);
                
                var requestResult = await _apiService.CallEndpointAsync(JellyseerrEndpoint.CreateRequest, parameters: requestParams);
                var request = requestResult as JellyseerrMediaRequest;
                
                // Check if request is valid by verifying it has an ID (successful requests always have an ID > 0)
                if (request != null && request.Id != 0)
                {
                    // Only mark as successfully processed after receiving a valid response
                    successfullyProcessedItems.Add(item.Id);
                    requestResults.Add((item, request));
                    _logger.LogTrace("Successfully created request for {ItemName} on behalf of {UserName}", 
                        item.Name, username);
                }
                else
                {
                    // API returned error/default object (e.g., quota exceeded, forbidden, etc.)
                    _logger.LogWarning("Failed to create request for {ItemName} on behalf of {UserName} - no valid response from Jellyseerr", 
                        item.Name, username);
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
        
        await RequestSeasonFavoritesAsync(seasonFavorites, userPermissionRequest4k, requestResults, blockedItems, successfullyProcessedItems);

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

    /// <summary>
    /// Request favorited seasons, combined into one request per user and show.
    /// </summary>
    private async Task RequestSeasonFavoritesAsync(
        List<(JellyseerrUser user, IJellyfinItem item)> seasonFavorites,
        bool userPermissionRequest4k,
        List<(IJellyfinItem item, JellyseerrMediaRequest request)> requestResults,
        List<IJellyfinItem> blockedItems,
        HashSet<Guid> successfullyProcessedItems)
    {
        if (seasonFavorites.Count == 0)
        {
            return;
        }

        // Season favorites only reach here with season placeholders on, where series requests always cover all seasons
        var seriesRequestedNow = requestResults
            .Where(r => r.item is JellyfinSeries)
            .Select(r => r.item.GetTmdbId())
            .Where(id => id.HasValue)
            .Select(id => id!.Value)
            .ToHashSet();

        var assignedSeasonItems = new HashSet<Guid>();
        var groups = new Dictionary<(int userId, int tmdbId), (JellyseerrUser user, List<JellyfinSeason> seasons)>();

        foreach (var (user, item) in seasonFavorites)
        {
            if (user == null || item is not JellyfinSeason season)
            {
                continue;
            }
            if (successfullyProcessedItems.Contains(season.Id) || !assignedSeasonItems.Add(season.Id))
            {
                continue;
            }

            var tmdbId = season.GetTmdbId();
            var seasonNumber = season.SeasonNumber;
            if (!tmdbId.HasValue || !seasonNumber.HasValue || seasonNumber.Value <= 0)
            {
                _logger.LogError("Skipping season {SeasonName} of {SeriesName} - no series TMDB ID or season number found",
                    season.Name, season.SeriesName);
                continue;
            }

            if (seriesRequestedNow.Contains(tmdbId.Value))
            {
                _logger.LogDebug("Season {SeasonNumber} of {SeriesName} is covered by the series request made in this sync",
                    seasonNumber.Value, season.SeriesName);
                continue;
            }

            var key = (user.Id, tmdbId.Value);
            if (!groups.TryGetValue(key, out var group))
            {
                group = (user, new List<JellyfinSeason>());
                groups[key] = group;
            }
            group.seasons.Add(season);
        }

        foreach (var ((_, tmdbId), (user, seasons)) in groups)
        {
            var username = user.JellyfinUsername ?? user.Username ?? "ID " + user.Id.ToString();
            var seasonNumbers = seasons.Select(s => s.SeasonNumber!.Value).Distinct().OrderBy(n => n).ToList();
            var seriesName = seasons[0].SeriesName;
            try
            {
                var requestParams = new Dictionary<string, object>
                {
                    ["mediaType"] = "tv",
                    ["mediaId"] = tmdbId,
                    ["userId"] = user.Id,
                    ["seasons"] = seasonNumbers,
                };
                if (userPermissionRequest4k)
                {
                    requestParams["is4k"] = PermissionHelper.HasPermission(JellyseerrModel.Permission.REQUEST_4K_TV, user.Permissions);
                }

                _logger.LogDebug("Requesting seasons [{Seasons}] of {SeriesName} (TMDB ID: {TmdbId}) for user {UserName}",
                    string.Join(", ", seasonNumbers), seriesName, tmdbId, username);

                var request = await _apiService.CallEndpointAsync(JellyseerrEndpoint.CreateRequest, parameters: requestParams) as JellyseerrMediaRequest;
                if (request != null && request.Id != 0)
                {
                    foreach (var season in seasons)
                    {
                        successfullyProcessedItems.Add(season.Id);
                        requestResults.Add((season, request));
                    }
                    _logger.LogInformation("Requested seasons [{Seasons}] of {SeriesName} on behalf of {UserName} (request {RequestId})",
                        string.Join(", ", seasonNumbers), seriesName, username, request.Id);
                }
                else
                {
                    _logger.LogWarning("Failed to request seasons [{Seasons}] of {SeriesName} on behalf of {UserName} - no valid response from Jellyseerr",
                        string.Join(", ", seasonNumbers), seriesName, username);
                    blockedItems.AddRange(seasons);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to request seasons [{Seasons}] of {SeriesName} on behalf of {UserName}",
                    string.Join(", ", seasonNumbers), seriesName, username);
                blockedItems.AddRange(seasons);
            }
        }
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

            // Seasons covered by active TV requests, per TMDB id (-1 = request without season list)
            var requestedSeasons = new Dictionary<int, HashSet<int>>();
            foreach (var r in jellyseerrRequests.Where(r => r != null && r.Status != JellyseerrModel.MediaRequestStatus.DECLINED
                && r.Media != null && r.Media.MediaType == JellyseerrModel.MediaType.TV))
            {
                if (!requestedSeasons.TryGetValue(r.Media.TmdbId, out var set))
                {
                    set = new HashSet<int>();
                    requestedSeasons[r.Media.TmdbId] = set;
                }
                if (r.Seasons == null || r.Seasons.Count == 0)
                {
                    set.Add(-1);
                }
                else
                {
                    foreach (var s in r.Seasons)
                    {
                        set.Add(s.SeasonNumber);
                    }
                }
            }

            foreach (var fav in allFavorites)
            {
                var tmdb = fav.item.GetTmdbId();
                var type = IJellyseerrItem.GetMediaType(fav.item);
                var isRequested = tmdb.HasValue && requestedLookup.Contains((type, tmdb.Value));
                if (fav.item is JellyfinSeason favSeason)
                {
                    // A season is requested only when an active request covers that season number
                    var seasonNumber = favSeason.SeasonNumber;
                    isRequested = tmdb.HasValue && seasonNumber.HasValue
                        && requestedSeasons.TryGetValue(tmdb.Value, out var seasonSet)
                        && (seasonSet.Contains(seasonNumber.Value) || seasonSet.Contains(-1));
                }
                else if (isRequested && fav.item is JellyfinSeries && tmdb.HasValue
                    && requestedSeasons.TryGetValue(tmdb.Value, out var showSeasons) && !showSeasons.Contains(-1))
                {
                    // A series that only has season-level requests stays pending while visible season placeholders are unrequested
                    var openSeasons = GetVisibleSeasonNumbers(fav.item.Path).Where(n => !showSeasons.Contains(n)).ToList();
                    if (openSeasons.Count > 0)
                    {
                        isRequested = false;
                        _logger.LogTrace("Series {ItemName} has season requests but seasons [{Open}] are not requested yet",
                            fav.item.Name, string.Join(", ", openSeasons));
                    }
                }
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

    /// <summary>
    /// Season numbers (> 0) of the season placeholder folders under a series folder that are not hidden.
    /// </summary>
    private static List<int> GetVisibleSeasonNumbers(string? seriesPath)
    {
        var result = new List<int>();
        try
        {
            if (string.IsNullOrEmpty(seriesPath) || !Directory.Exists(seriesPath))
            {
                return result;
            }
            foreach (var dir in Directory.GetDirectories(seriesPath))
            {
                var number = PlaceholderVideoGenerator.ParseSeasonFolderNumber(dir);
                if (number.HasValue && number.Value > 0 && !File.Exists(Path.Combine(dir, BridgeService.IgnoreFileName)))
                {
                    result.Add(number.Value);
                }
            }
        }
        catch (Exception)
        {
            // Treat unreadable folders as having no season placeholders
        }
        return result;
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
public async Task<(List<IJellyseerrItem> newIgnored, List<IJellyseerrItem> cleared, List<IJellyfinItem> hiddenSeasons)> ClearAndIgnoreRequestedAsync()
    {
        var newIgnored = new List<IJellyseerrItem>();
        var cleared = new List<IJellyseerrItem>();
        var hiddenSeasons = new List<IJellyfinItem>();

        var removeRequested = Plugin.GetConfigOrDefault<bool>(nameof(PluginConfiguration.RemoveRequestedFromFavorites));

        try
        {
            // Get all existing favorites
            var allFavorites = GetUserFavorites();
			_logger.LogDebug("Fetched {FavoriteCount} favorites for processing", allFavorites.Count);
            var (requestedFavorites, _) = await FilterRequestsFromFavorites(allFavorites);
			_logger.LogDebug("Identified {RequestedCount} favorites with existing Jellyseerr requests", requestedFavorites.Count);

            // Requested seasons only hide their own season folder, never the whole show
            var requestedSeasonFavorites = requestedFavorites.Where(f => f.item is JellyfinSeason).ToList();
            requestedFavorites = requestedFavorites.Where(f => f.item is not JellyfinSeason).ToList();
            hiddenSeasons.AddRange(await HideRequestedSeasonsAsync(requestedSeasonFavorites, removeRequested));
            // Build lookup of itemId -> users who favorited it
            var itemIdToUsers = requestedFavorites
                .GroupBy(f => f.item.Id)
                .ToDictionary(g => g.Key, g => g.Select(x => x.user).ToList());

            // Find matches between Jellyfin items and Jellyseerr metadata
            var jellyfinItems = requestedFavorites.Select(f => f.item).ToList();
			var (matches, _) = await _bridgeService.BuildJellyseerrMatchesAsync(jellyfinItems);
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
		
        return (newIgnored, cleared, hiddenSeasons);
    }

    /// <summary>
    /// Marker written into .ignore files that hide a requested season (used to undo them for declined requests).
    /// </summary>
    public const string SeasonIgnoreMarker = "JellyBridge season request";

    /// <summary>
    /// Hide requested seasons with an .ignore in their season folder; the show and its other seasons stay visible.
    /// </summary>
    private async Task<List<IJellyfinItem>> HideRequestedSeasonsAsync(
        List<(JellyfinUser user, IJellyfinItem item)> requestedSeasonFavorites, bool removeRequested)
    {
        var hidden = new List<IJellyfinItem>();
        foreach (var group in requestedSeasonFavorites.GroupBy(f => f.item.Id))
        {
            if (group.First().item is not JellyfinSeason season)
            {
                continue;
            }
            try
            {
                // A requested missing season has no folder to hide; just clear the favorite
                if (season.IsVirtual)
                {
                    if (removeRequested)
                    {
                        foreach (var (user, _) in group)
                        {
                            await _userDataManager.TryUnfavoriteAsync(_libraryManager, user, season).ConfigureAwait(false);
                        }
                    }
                    continue;
                }

                var seasonDir = season.Path;
                if (string.IsNullOrEmpty(seasonDir) || !Directory.Exists(seasonDir) || !FolderUtils.IsPathInSyncDirectory(seasonDir))
                {
                    continue;
                }

                foreach (var (user, _) in group)
                {
                    if (removeRequested)
                    {
                        await _userDataManager.TryUnfavoriteAsync(_libraryManager, user, season).ConfigureAwait(false);
                    }
                    await _userDataManager.MarkItemPlayStatusAsync(user, season, markAsPlayed: false).ConfigureAwait(false);
                }

                var ignorePath = Path.Combine(seasonDir, BridgeService.IgnoreFileName);
                if (!File.Exists(ignorePath))
                {
                    var content = string.Join(Environment.NewLine,
                        SeasonIgnoreMarker,
                        $"series={season.SeriesName}",
                        $"tmdbId={season.GetTmdbId()}",
                        $"season={season.SeasonNumber}");
                    await _bridgeService.CreateIgnoreFileAsync(ignorePath, content);
                    hidden.Add(season);
                    _logger.LogDebug("Hid requested season {SeasonNumber} of {SeriesName}: {IgnorePath}",
                        season.SeasonNumber, season.SeriesName, ignorePath);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed hiding requested season {SeasonName} of {SeriesName}", season.Name, season.SeriesName);
            }
        }
        return hidden;
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

            // Unhide season folders whose requests were all declined
            declinedItems.AddRange(await UnhideDeclinedSeasonsAsync(allRequests));

			if (fullyDeclinedGroups.Count == 0)
            {
				_logger.LogTrace("No fully-declined media groups found; nothing to unignore");
                return declinedItems;
            }

            var representativeDeclinedRequests = fullyDeclinedGroups.Select(g => g.First()).ToList();

            _logger.LogDebug(
                "Found {DeclinedMediaCount} media with all requests declined; removing .ignore markers",
                representativeDeclinedRequests.Count);

            var declinedShowItems = await FindJellyseerrFavorites(representativeDeclinedRequests);
            declinedItems.AddRange(declinedShowItems.Where(i => !declinedItems.Any(d => d.GetType() == i.GetType() && d.Id == i.Id)));
            foreach (var item in declinedShowItems)
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

    /// <summary>
    /// Unhide seasons when every request covering them was declined.
    /// </summary>
    private async Task<List<IJellyseerrItem>> UnhideDeclinedSeasonsAsync(List<JellyseerrMediaRequest> allRequests)
    {
        var unhiddenShows = new List<IJellyseerrItem>();
        var seasonStatuses = new Dictionary<(int tmdbId, int season), List<JellyseerrModel.MediaRequestStatus>>();
        foreach (var r in allRequests.Where(r => r?.Media != null && r.Media.TmdbId > 0 && r.Type == JellyseerrModel.MediaType.TV))
        {
            foreach (var s in r.Seasons ?? new List<JellyseerrModel.SeasonRequest>())
            {
                var key = (r.Media.TmdbId, s.SeasonNumber);
                if (!seasonStatuses.TryGetValue(key, out var list))
                {
                    list = new List<JellyseerrModel.MediaRequestStatus>();
                    seasonStatuses[key] = list;
                }
                list.Add(r.Status);
            }
        }
        var declinedSeasons = seasonStatuses
            .Where(kv => kv.Value.All(status => status == JellyseerrModel.MediaRequestStatus.DECLINED))
            .Select(kv => kv.Key)
            .ToHashSet();
        if (declinedSeasons.Count == 0)
        {
            return unhiddenShows;
        }

        var (_, shows) = await _metadataService.ReadMetadataAsync();
        var declinedTmdbIds = declinedSeasons.Select(k => k.tmdbId).ToHashSet();
        foreach (var show in shows.Where(s => declinedTmdbIds.Contains(s.Id)))
        {
            try
            {
                var showDir = _metadataService.GetJellyBridgeItemDirectory(show);
                if (!Directory.Exists(showDir))
                {
                    continue;
                }
                foreach (var dir in Directory.GetDirectories(showDir))
                {
                    var number = PlaceholderVideoGenerator.ParseSeasonFolderNumber(dir);
                    if (!number.HasValue || !declinedSeasons.Contains((show.Id, number.Value)))
                    {
                        continue;
                    }
                    var ignorePath = Path.Combine(dir, BridgeService.IgnoreFileName);
                    if (File.Exists(ignorePath) && (await File.ReadAllTextAsync(ignorePath)).Contains(SeasonIgnoreMarker))
                    {
                        File.Delete(ignorePath);
                        if (!unhiddenShows.Contains(show))
                        {
                            unhiddenShows.Add(show);
                        }
                        _logger.LogDebug("Removed season .ignore for declined request: {Dir}", dir);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed unhiding declined seasons of {Name}", show.MediaName);
            }
        }
        return unhiddenShows;
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

}