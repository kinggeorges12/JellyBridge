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
    private readonly PlaceholderUserDataService _placeholderUserDataService;
    private readonly SortService _sortService;
    private readonly CleanupService _cleanupService;

    /// <summary>
    /// UTC time the last successful discover sync finished — scheduled task or either sync endpoint
    /// (<see cref="SyncFromJellyseerr"/> and <see cref="SyncLibraryAsync"/> both set it). GET
    /// /JellyBridge/Libraries returns it as lastSync so the settings page also sees endpoint syncs
    /// (5c review fix 6: the scheduled task's last result misses those).
    /// </summary>
    public static DateTime? LastSyncCompleted { get; private set; }

    public SyncService(
        ILogger<SyncService> logger,
        ApiService apiService,
        BridgeService bridgeService,
        RefreshService refreshService,
        DiscoverService discoverService,
        FavoriteService favoriteService,
        MetadataService metadataService,
        PlaceholderUserDataService placeholderUserDataService,
        SortService sortService,
        CleanupService cleanupService)
    {
        _placeholderUserDataService = placeholderUserDataService;
        _logger = new DebugLogger<SyncService>(logger);
        _apiService = apiService;
        _bridgeService = bridgeService;
        _refreshService = refreshService;
        _discoverService = discoverService;
        _favoriteService = favoriteService;
        _metadataService = metadataService;
        _sortService = sortService;
        _cleanupService = cleanupService;
    }

    /// <summary>
    /// Check if a sync operation is currently running.
    /// </summary>
    public bool IsSyncRunning => Plugin.IsOperationRunning;

    /// <summary>
    /// Sync folder structure and JSON metadata files from Jellyseerr to Jellyfin, for the Discover library and, when turned
    /// on, the Trending library.
    /// Note: Caller is responsible for locking.
    /// </summary>
    public async Task<SyncJellyseerrResult> SyncFromJellyseerr()
    {
        if (LibraryMigrationService.IsMigrationInProgress())
        {
            _logger.LogWarning("Refusing the discover sync: JellyBridge library migration in progress.");
            return new SyncJellyseerrResult { Success = false, Message = "Library migration in progress" };
        }

        // One pass per enabled library, inside the loop's scope; FetchListAsync picks the fetcher for the library's list.
        var perLibrary = await Utils.LibraryLoop.ForEachLibraryAsync(_logger, "sync", SyncOneLibraryAsync,
            (library, ex) => new SyncJellyseerrResult { Success = false, Message = $"❌ {library.Name} failed: {ex.Message}" });

        var result = new SyncJellyseerrResult { Success = true };
        if (perLibrary.Count == 0)
        {
            result.Success = false;
            result.Message = "No enabled Discover libraries";
            return result;
        }

        var segments = new List<string>();
        foreach (var (library, libraryResult) in perLibrary)
        {
            segments.Add($"{library.Name}: {libraryResult.Message}");
            result.Success &= libraryResult.Success;
            result.ItemsAdded.AddRange(libraryResult.ItemsAdded);
            result.ItemsUpdated.AddRange(libraryResult.ItemsUpdated);
            result.ItemsSkipped.AddRange(libraryResult.ItemsSkipped);
            result.ItemsHidden.AddRange(libraryResult.ItemsHidden);
            result.Refresh = MergeRefresh(result.Refresh, libraryResult.Refresh);
        }

        result.Message = string.Join("; ", segments);
        if (result.Success)
        {
            LastSyncCompleted = DateTime.UtcNow;
        }

        return result;
    }

    /// <summary>
    /// Sync one named library — the settings page's per-library Sync button (task 5a): the same per-library body
    /// <see cref="SyncFromJellyseerr"/> runs in the loop, then this library's favorites hide/ignore step, then the
    /// refresh plan. Note: Caller is responsible for locking.
    /// </summary>
    /// <param name="name">The library name, matched ignoring case.</param>
    /// <returns>The library's sync result; an unknown name fails without touching anything.</returns>
    public async Task<SyncJellyseerrResult> SyncLibraryAsync(string name)
    {
        if (LibraryMigrationService.IsMigrationInProgress())
        {
            _logger.LogWarning("Refusing the single-library sync: JellyBridge library migration in progress.");
            return new SyncJellyseerrResult { Success = false, Message = "Library migration in progress" };
        }

        var library = DiscoverLibraries.GetAll()
            .FirstOrDefault(l => string.Equals(l.Name, name, StringComparison.OrdinalIgnoreCase));
        if (library == null)
        {
            return new SyncJellyseerrResult { Success = false, Message = $"No Discover library named '{name}'." };
        }

        var syncResult = await Utils.LibraryLoop.ForLibraryAsync(_logger, "sync", library, SyncOneLibraryAsync,
            (lib, ex) => new SyncJellyseerrResult { Success = false, Message = $"❌ {lib.Name} failed: {ex.Message}" });

        // This library's favorites hide/ignore pass, the same step the favorites sync runs per library; a failure
        // there is logged, but the discover sync's own result is what the endpoint reports.
        var hideResult = await Utils.LibraryLoop.ForLibraryAsync(
            _logger, "favorites hide/ignore", library,
            _ => HideIgnoredStepAsync(),
            (lib, ex) => new SyncJellyfinResult { Success = false, Message = $"{lib.Name} failed: {ex.Message}" });
        if (!hideResult.Success)
        {
            _logger.LogWarning("Favorites hide/ignore failed for library {Library}: {Message}", library.Name, hideResult.Message);
        }

        // Task 7: a single-library sync refreshes only this library's folder (targeted, like the REST
        // POST /Items/<libraryId>/Refresh) instead of the global plan — the global "Scan All Libraries" plan
        // is skipped while Jellyfin's Scan Media Library task is already running, which left new libraries
        // empty. The list-order play counts do not need the refresh: SyncOneLibraryAsync already wrote them
        // through the user data manager; the Update options below just reload user data like the old refresh did.
        var refreshPlan = MergeRefresh(syncResult.Refresh, hideResult.Refresh);
        if (refreshPlan != null)
        {
            // Make Jellyfin see .ignore files created/removed since its last scan, as ApplyRefreshAsync does.
            _refreshService.ClearDotIgnoreCache();
            var refreshDirectory = library.GetDirectory(DiscoverLibraries.GetRootDirectory());
            await _refreshService.QueueLibraryRefreshAsync(refreshDirectory,
                createMode: refreshPlan.CreateRefresh, removeMode: refreshPlan.RemoveRefresh, refreshImages: refreshPlan.RefreshImages);
        }
        if (syncResult.Success)
        {
            LastSyncCompleted = DateTime.UtcNow;
        }

        return syncResult;
    }

    /// <summary>
    /// Sync one library: fetch its list and run the shared sync steps, then the list-specific steps — a Trending
    /// library removes the titles that are no longer trending, and a non-Legacy library whose "Order in Jellyfin"
    /// is "Same as the list" gets the play counts that sort it in list order (a Legacy library keeps today's
    /// behaviour: only the sort task touches its play counts, with the global SortOrder). Runs inside the library
    /// loop's scope.
    /// </summary>
    /// <param name="library">The library being synced.</param>
    private async Task<SyncJellyseerrResult> SyncOneLibraryAsync(DiscoverLibraryConfig library)
    {
        // Titles this sync fetches get a fresh CreatedDate; a list-change cleanup (below) must tell fetched from
        // not-fetched by that, so it pins its cut-off to just before the fetch starts.
        var syncStart = DateTimeOffset.Now;
        var fetched = new List<IJellyseerrItem>();
        var result = await SyncFromJellyseerrAsync(async () =>
        {
            // FetchListAsync routes Trending to the trending fetcher and applies the library's Movies/Series filter
            var (movies, shows) = await _discoverService.FetchListAsync(library);
            fetched.AddRange(movies);
            fetched.AddRange(shows);
            return (movies, shows);
        });
        if (!result.Success)
        {
            return result;
        }

        if (!library.Legacy)
        {
            // Keep the stored list ranks always fresh: a title that dropped off this list loses its rank in
            // metadata.json, so the next scheduled sort task (which trusts the stored ranks) puts it at the
            // fallback like this sync does instead of restoring the stale rank.
            var clearedRanks = await _metadataService.ClearStaleListRanksAsync(fetched);
            if (clearedRanks > 0)
            {
                _logger.LogDebug("Cleared the stored list rank of {Count} titles no longer on '{LibraryName}'", clearedRanks, library.Name);
            }
        }

        if (library.ListType == DiscoverListType.Trending)
        {
            var removed = await RemoveStaleTrendingAsync(fetched);
            result.ItemsHidden.AddRange(removed);
            if (removed.Count > 0)
            {
                result.Refresh = MergeRefresh(result.Refresh, new RefreshPlan { RemoveRefresh = true });
            }
        }
        else if (!library.Legacy && library.ListTypeChangedSinceLastSync)
        {
            // Decision 9: the List changed since the last sync — the old list's titles go now, not after the
            // retention days. Run this library's cleanup once with the retention cut-off pinned to the sync
            // start: titles this sync (re)fetched have a fresh CreatedDate and stay; the rest are "expired".
            // The search-added exemption lives in the cleanup itself.
            var listCleanup = await _cleanupService.CleanupListChangeAsync(syncStart);
            if (!listCleanup.Success)
            {
                result.Success = false;
                result.Message = $"❌ {library.Name}: list-change cleanup failed: {listCleanup.Message}";
                return result;
            }

            if (listCleanup.ItemsDeleted.Count > 0)
            {
                _logger.LogInformation("List change: removed {Count} titles no longer on '{LibraryName}' (retention skipped once)",
                    listCleanup.ItemsDeleted.Count, library.Name);
                result.ItemsHidden.AddRange(listCleanup.ItemsDeleted);
                result.Refresh = MergeRefresh(result.Refresh, new RefreshPlan { RemoveRefresh = true });
            }
        }

        if (!library.Legacy && library.OrderOrDefault == DiscoverLibraryOrder.AsList)
        {
            // Titles Jellyfin hasn't scanned yet get their position on the next sync. The stored ranks are fresh
            // (ClearStaleListRanksAsync above), so the sync and the sort task order the same way.
            await _sortService.SortByListAsync();
        }

        // Record the list this sync ran against (decision 9 bookkeeping) so the next sync sees an unchanged list.
        if (!library.Legacy && library.LastSyncedListType != library.ListType)
        {
            library.LastSyncedListType = library.ListType;
            Plugin.Instance?.SaveConfiguration();
        }

        return result;
    }

    /// <summary>
    /// Delete the Trending library's titles that are no longer trending. A title someone favorited stays until the favorites
    /// sync has requested it (it is hidden then).
    /// </summary>
    private async Task<List<IJellyseerrItem>> RemoveStaleTrendingAsync(List<IJellyseerrItem> trending)
    {
        var current = trending.Select(item => (item.GetType(), item.Id)).ToHashSet();
        var favoritePaths = _favoriteService.GetUserFavorites()
            .Select(favorite => favorite.item?.Path)
            .Where(path => !string.IsNullOrEmpty(path))
            .ToList();
        var (movies, shows) = await _metadataService.ReadMetadataAsync();
        var removed = new List<IJellyseerrItem>();
        foreach (var item in movies.Cast<IJellyseerrItem>().Concat(shows))
        {
            if (current.Contains((item.GetType(), item.Id)))
            {
                continue;
            }

            // Decision 9: search-added titles (SearchService.SearchNetworkId) are never removed by any
            // list-based removal — they were never on a list. Only the rating cleanup may remove them.
            if (item.NetworkId == SearchService.SearchNetworkId)
            {
                continue;
            }

            try
            {
                var folder = _metadataService.GetJellyBridgeItemDirectory(item);
                var hidden = File.Exists(Path.Combine(folder, BridgeService.IgnoreFileName));
                if (!hidden && favoritePaths.Any(path => FolderUtils.IsPathInDirectory(path, folder)))
                {
                    continue;
                }

                Directory.Delete(folder, true);
                removed.Add(item);
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Could not remove {ItemName} from the Trending library: {Message}", item.MediaName, ex.Message);
            }
        }

        _logger.LogDebug("Removed {Count} titles that are no longer trending", removed.Count);
        return removed;
    }

    private static RefreshPlan? MergeRefresh(RefreshPlan? first, RefreshPlan? second)
    {
        if (first == null || second == null)
        {
            return first ?? second;
        }

        return new RefreshPlan
        {
            CreateRefresh = first.CreateRefresh || second.CreateRefresh,
            RemoveRefresh = first.RemoveRefresh || second.RemoveRefresh,
            RefreshImages = first.RefreshImages || second.RefreshImages
        };
    }

    private async Task<SyncJellyseerrResult> SyncFromJellyseerrAsync(Func<Task<(List<JellyseerrMovie>, List<JellyseerrShow>)>> fetchMedia)
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
            var (discoverMovies, discoverShows) = await fetchMedia();
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
            _logger.LogDebug("Step 2: Filtered {OriginalCount} items to {UniqueCount} unique items across networks", 
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

            // Step 8b: Add a placeholder for each requestable season, or remove them when season placeholders are off
            var useSeasonPlaceholders = Plugin.GetConfigOrDefault<bool>(nameof(PluginConfiguration.UseSeasonPlaceholders));
            var (seasonsCreated, seasonsRemoved) = await _discoverService.EnsureSeasonPlaceholdersAsync(unmatchedShows, useSeasonPlaceholders);
            _logger.LogDebug("Step 8b: Season placeholders created: {Created}, season folders removed: {Removed}", seasonsCreated, seasonsRemoved);

            // Step 8c: Keep season placeholders out of Continue Watching and Next Up
            if (useSeasonPlaceholders)
            {
                var resetUserData = _placeholderUserDataService.ResetPlaceholderPlaybackState();
                _logger.LogDebug("Step 8c: Reset playback state of {Count} placeholder user data records", resetUserData);
            }

            // Step 7 (again): Gather ignore results and add to appropriate collections
            var (newIgnored, existingIgnored) = await ignoreTask;
            result.ItemsSkipped.AddRange(existingIgnored);
            result.ItemsHidden.AddRange(newIgnored);

            // Step 9: Filter and ignore items with invalid network IDs
            // (Trending titles have no network)
            if (LibraryScope.Current?.ListType != DiscoverListType.Trending)
            {
                _logger.LogDebug("Step 9: 🔍 Filtering and ignoring items with invalid network IDs...");
                var (newIgnoredNetwork, existingIgnoredNetwork) = await _discoverService.IgnoreInvalidNetworkItemsAsync();
                result.ItemsSkipped.AddRange(existingIgnoredNetwork);
                result.ItemsHidden.AddRange(newIgnoredNetwork);
            }

            // Step 9: Provide refresh plan back to caller; orchestration occurs after both syncs complete
            // Only set refresh plan if there are added items or hidden items
            var hasAddedItems = result.ItemsAdded.Count > 0 || seasonsCreated > 0;
            var hasHiddenItems = result.ItemsHidden.Count > 0 || seasonsRemoved > 0;
            
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
    /// Sync data from Jellyfin to Jellyseerr by creating requests for favorited bridge-only items (decision 8: a
    /// title that has a copy in two libraries is requested once and hides both copies). The request steps run once,
    /// outside the library loop, over the favorites of every bridge folder (deduped by media type and TMDB id); the
    /// per-folder hide/ignore steps run once per enabled library inside the loop, so each library's own copy gets
    /// its .ignore and declined requests lose theirs.
    /// Note: Caller is responsible for locking.
    /// </summary>
    public async Task<SyncJellyfinResult> SyncToJellyseerr()
    {
        if (LibraryMigrationService.IsMigrationInProgress())
        {
            _logger.LogWarning("Refusing the favorites sync: JellyBridge library migration in progress.");
            return new SyncJellyfinResult { Success = false, Message = "Library migration in progress" };
        }

        if (DiscoverLibraries.GetEnabled().Count == 0)
        {
            var noneResult = new SyncJellyfinResult { Success = false, Message = "No enabled Discover libraries" };
            return noneResult;
        }

        var result = await RequestFavoritesStepAsync();
        if (!result.Success)
        {
            return result;
        }

        var perLibrary = await Utils.LibraryLoop.ForEachLibraryAsync(
            _logger, "favorites hide/ignore", _ => HideIgnoredStepAsync(),
            (library, ex) => new SyncJellyfinResult { Success = false, Message = $"{library.Name} failed: {ex.Message}" });

        var failures = new List<string>();
        var segments = new List<string> { $"Requests: {result.Message}" };
        result.Message = string.Empty;
        foreach (var (library, libraryResult) in perLibrary)
        {
            segments.Add($"{library.Name}: {libraryResult.Message}");
            if (!libraryResult.Success)
            {
                failures.Add($"{library.Name}: {libraryResult.Message}");
            }

            result.Success &= libraryResult.Success;
            result.ItemsHidden.AddRange(libraryResult.ItemsHidden);
            result.ItemsCleared.AddRange(libraryResult.ItemsCleared);
            result.ItemsUnhidden.AddRange(libraryResult.ItemsUnhidden);
            result.Refresh = MergeRefresh(result.Refresh, libraryResult.Refresh);
        }

        if (failures.Count > 0)
        {
            result.Message = $"⚠️ Favorite hide/ignore failed for {string.Join("; ", failures)}";
        }
        else
        {
            // One segment per library, like SyncFromJellyseerr's success message
            result.Message = string.Join("; ", segments);
        }

        return result;
    }

    /// <summary>
    /// The request steps of the favorites sync, run once for all libraries (outside the library scope): connect to
    /// Jellyseerr, collect the favorites of every bridge folder and request each favorited (media type, TMDB id)
    /// exactly once. <see cref="FavoriteService.RequestFavorites"/> skips a second copy of the same title. Also
    /// called by the libraries migration before it moves anything (the moved titles get new item ids).
    /// </summary>
    public async Task<SyncJellyfinResult> RequestFavoritesStepAsync()
    {
        var result = new SyncJellyfinResult();

        try
        {
            _logger.LogDebug("Starting favorites-to-requests step for all bridge libraries...");

            // Step 0: Test connection first
            var status = (SystemStatus)await _apiService.CallEndpointAsync(JellyseerrEndpoint.Status);
            if (status == null)
            {
                _logger.LogWarning("Failed to connect to Jellyseerr, skipping sync from Jellyfin to Jellyseerr");
                result.Success = false;
                result.Message = "🔌 Failed to connect to Jellyseerr API";
                return result;
            }

            // Step 1: Get all Jellyfin users and their favorites from every JellyBridge library folder
            _logger.LogDebug("Step 1: Getting Jellyfin favorites from all JellyBridge folders");
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

            // Step 5: Create requests for favorited bridge-only items (once per media type + TMDB id)
            _logger.LogDebug("Step 5: Creating Jellyseerr requests for mapped favorites");
            var (requestResults, blockedItems) = await _favoriteService.RequestFavorites(unrequestedFavoritesWithJellyseerrUser);

            // Add requests to unified collection
            // Several favorited seasons of a show share one request
            result.ItemsCreated.AddRange(requestResults.Select(r => r.request).Where(r => r != null).Distinct());

            // Add blocked items to unified collection
            result.ItemsBlocked.AddRange(blockedItems);

            result.Success = true;
            result.Message = $"✅ {requestResults.Count} favorited titles requested";

            _logger.LogDebug("Favorites-to-requests step completed with {ResultCount} successful requests", requestResults.Count);
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
            _logger.LogError(ex, "Error during favorites-to-requests step");
            result.Success = false;
            result.Message = $"❌ Sync to Jellyseerr failed: {ex.Message}";
            result.Details = $"Exception type: {ex.GetType().Name}\nStack trace: {ex.StackTrace}";
        }

        return result;
    }

    /// <summary>
    /// The per-folder hide/ignore steps of the favorites sync, run once per library inside the library loop: mark
    /// requested titles hidden with an .ignore in the current library's folder (plus every other library folder
    /// that holds the same title), unfavorite them, and remove the .ignore files of declined requests again.
    /// </summary>
    private async Task<SyncJellyfinResult> HideIgnoredStepAsync()
    {
        var result = new SyncJellyfinResult();

        try
        {
            // Step 6: For requested items, unmark as favorite for the user and create an .ignore file in each bridge item directory
            _logger.LogDebug("Step 6: Unfavoriting requested items and creating ignore files");
            var (newlyIgnoredItems, clearedItems, hiddenSeasons) = await _favoriteService.ClearAndIgnoreRequestedAsync();
            result.ItemsHidden.AddRange(newlyIgnoredItems);
            result.ItemsCleared.AddRange(clearedItems);
            _logger.LogDebug("Step 6: Hidden {NewlyIgnored} items and {HiddenSeasons} requested seasons, cleared {ClearedCount} items",
                newlyIgnoredItems.Count, hiddenSeasons.Count, clearedItems.Count);

            // Step 8: Check requests again and remove .ignore files for items that are no longer in the requested items in Jellyseerr
            _logger.LogDebug("Step 7: Removing .ignore files for fully declined requests");
            var declinedItems = await _favoriteService.UnignoreDeclinedRequests();
            result.ItemsUnhidden.AddRange(declinedItems);
            _logger.LogDebug("Step 7: Unhidden {UnhiddenCount} declined items", declinedItems.Count);

            // Step 9: Provide refresh plan back to caller; orchestration occurs after both syncs complete
            // Only set refresh plan if there are added items or hidden items
            var hasHiddenItems = result.ItemsHidden.Count > 0 || hiddenSeasons.Count > 0;
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
            result.Message = "✅ hide/ignore completed";

            _logger.LogDebug("Hide/ignore step completed for the current library");
        }
        catch (MissingMethodException ex)
        {
            _logger.LogDebug(ex, "Using incompatible Jellyfin version. Skipping hide/ignore step");
            result.Success = false;
            result.Message = "❌ Using incompatible Jellyfin version. Skipping sync to Jellyseerr";
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during hide/ignore step");
            result.Success = false;
            result.Message = $"❌ Sync to Jellyseerr failed: {ex.Message}";
            result.Details = $"Exception type: {ex.GetType().Name}\nStack trace: {ex.StackTrace}";
        }

        return result;
    }
}

