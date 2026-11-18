using Jellyfin.Plugin.JellyBridge.BridgeModels;
using Jellyfin.Plugin.JellyBridge.JellyseerrModel;
using Jellyfin.Plugin.JellyBridge.JellyseerrModel.Server;
using Jellyfin.Plugin.JellyBridge.JellyfinModels;
using Jellyfin.Plugin.JellyBridge.Utils;
using Jellyfin.Plugin.JellyBridge.Configuration;
using Microsoft.Extensions.Logging;
using System.Globalization;
using System.IO;
using System.Security;

namespace Jellyfin.Plugin.JellyBridge.Services;

/// <summary>
/// Service for discovering and importing content between Jellyfin and Jellyseerr.
/// </summary>
public class DiscoverService
{
    private readonly DebugLogger<DiscoverService> _logger;
    private readonly PlaceholderVideoGenerator _placeholderVideoGenerator;
    private readonly ApiService _apiService;
    private readonly MetadataService _metadataService;
    private readonly BridgeService _bridgeService;


    public DiscoverService(ILogger<DiscoverService> logger, PlaceholderVideoGenerator placeholderVideoGenerator, ApiService apiService, MetadataService metadataService, BridgeService bridgeService)
    {
        _logger = new DebugLogger<DiscoverService>(logger);
        _placeholderVideoGenerator = placeholderVideoGenerator;
        _apiService = apiService;
        _metadataService = metadataService;
        _bridgeService = bridgeService;
    }

    #region Process
    

    /// <summary>
    /// Fetches the list that fills a Discover library: the trending list for a Trending library, otherwise
    /// Discover with the list's own sort order and release-date window (New releases, Coming soon, Top rated) or
    /// plain popularity (Popular). A Movies- or Series-only library gets no titles of the other kind: Discover
    /// lists skip those endpoint calls, and Trending is filtered to the library's kind while fetching.
    /// Task 8 (Mohammad's rule, whole-library shape of the 8b fix 5): Pages counts the titles you see —
    /// Pages × 20 visible titles for the WHOLE library. The fetch pulls the network streams round-robin until
    /// that target, the lists end, or the 5 × Pages per-stream page cap is reached; a title counts when the
    /// sync would NOT hide it (see <see cref="FilterVisibleAsync"/>), titles past the target are never written,
    /// and ListRank follows the pull order.
    /// </summary>
    /// <param name="library">The library whose list and type to fetch for.</param>
    /// <returns>The visible movies and shows to write, plus the titles the list holds but the fetch judged
    /// HIDDEN (.ignore or a real-library match): 8b fixes 3/4 need them so the sync keeps their folder
    /// (the stale-trending removal never deletes a hidden title, retention never ages it out) without ever
    /// rewriting or ranking it.</returns>
    public async Task<(List<JellyseerrMovie>, List<JellyseerrShow>, List<IJellyseerrItem>)> FetchListAsync(DiscoverLibraryConfig library)
    {
        // Inside the library's scope this reads the library's own Pages with the global fallback (TrendingPages
        // for a Trending library); 0 means unlimited — fetch until the lists end, no visible target.
        var pages = Plugin.GetConfigOrDefault<int>(nameof(PluginConfiguration.MaxDiscoverPages));
        var state = new VisibleFetchState();
        List<IJellyseerrItem> visible, hidden;
        if (library.ListType == DiscoverListType.Trending)
        {
            (visible, hidden) = await FetchTrendingMediaAsync(library.LibraryType, state, pages);
        }
        else
        {
            var (listMovieParameters, listTvParameters) = GetListParameters(library.ListType);
            (visible, hidden) = await FetchDiscoverMediaAsync(library.ListType, listMovieParameters, listTvParameters,
                library.LibraryType, state, pages);
        }

        // One INF line per library, Mohammad's format: "Trending Movies: 20 visible titles from 2 Seerr pages
        // (8 skipped: 5 shows, 3 hidden)" — only the reasons that actually occurred are named.
        var skipped = state.WrongType + state.Duplicates + state.Blocked + state.BelowRating + state.Hidden;
        var reasons = new List<string>();
        if (state.WrongType > 0)
        {
            reasons.Add($"{state.WrongType} {(library.LibraryType == DiscoverLibraryType.Movies ? "shows" : "movies")}");
        }
        if (state.Duplicates > 0)
        {
            reasons.Add($"{state.Duplicates} duplicates");
        }
        if (state.Blocked > 0)
        {
            reasons.Add($"{state.Blocked} blocklisted");
        }
        if (state.BelowRating > 0)
        {
            reasons.Add($"{state.BelowRating} below the rating floor");
        }
        if (state.Hidden > 0)
        {
            reasons.Add($"{state.Hidden} hidden");
        }
        _logger.LogInformation("{LibraryName}: {VisibleCount} visible titles from {SeerrPages} Seerr pages ({SkippedText})",
            library.Name, visible.Count, state.SeerrPages,
            skipped == 0 ? "0 skipped" : $"{skipped} skipped: {string.Join(", ", reasons)}");

        // Hidden titles can repeat across streams and pages; keep one entry per title.
        var distinctHidden = new List<IJellyseerrItem>();
        var hiddenSeen = new HashSet<(JellyseerrModel.MediaType, int)>();
        foreach (var item in hidden)
        {
            if (hiddenSeen.Add((item.MediaType, item.Id)))
            {
                distinctHidden.Add(item);
            }
        }

        return (visible.OfType<JellyseerrMovie>().ToList(), visible.OfType<JellyseerrShow>().ToList(), distinctHidden);
    }

    /// <summary>
    /// The list's own discover parameters (sort order and release-date window), verified against the live Seerr on
    /// 2026-11-17. Coming soon filters <c>discover/movies</c>/<c>discover/tv</c> by date instead of using the
    /// <c>upcoming</c> endpoints, because those reject the network parameters. ApiService merges these over the
    /// endpoints' popularity.desc default, so a sortBy here wins.
    /// </summary>
    /// <param name="listType">The list to build parameters for.</param>
    /// <returns>Extra parameters per endpoint; empty for Popular.</returns>
    private static (Dictionary<string, object> Movies, Dictionary<string, object> Tv) GetListParameters(DiscoverListType listType)
    {
        var today = DateTime.UtcNow.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        return listType switch
        {
            DiscoverListType.NewReleases => (
                new Dictionary<string, object> { ["sortBy"] = "primary_release_date.desc", ["primaryReleaseDateLte"] = today },
                new Dictionary<string, object> { ["sortBy"] = "first_air_date.desc", ["firstAirDateLte"] = today }),
            DiscoverListType.ComingSoon => (
                new Dictionary<string, object> { ["primaryReleaseDateGte"] = today },
                new Dictionary<string, object> { ["firstAirDateGte"] = today }),
            DiscoverListType.TopRated => (
                new Dictionary<string, object> { ["sortBy"] = "vote_average.desc" },
                new Dictionary<string, object> { ["sortBy"] = "vote_average.desc" }),
            _ => (new Dictionary<string, object>(), new Dictionary<string, object>()),
        };
    }

    /// <summary>
    /// The rating parameters to send to the Seerr discover endpoints. With a Minimum Rating set that's the rating and
    /// vote-count floor; Top rated and New releases also send the library's Minimum Votes on its own when no Minimum
    /// Rating is set, so a few early votes can't carry a title to the top of those lists. Popular keeps sending
    /// nothing at rating 0, exactly as before.
    /// </summary>
    /// <param name="listType">The list being fetched.</param>
    /// <returns>The parameters to merge into the network parameters; empty when there is no filter.</returns>
    private static Dictionary<string, object> GetSeerrRatingParameters(DiscoverListType listType)
    {
        var parameters = new Dictionary<string, object>();
        if (GetRatingFilter() is { } ratingFilter)
        {
            // Invariant culture, so a server set to a comma-decimal language still sends 7.5 rather than 7,5
            parameters["voteAverageGte"] = ratingFilter.MinimumRating.ToString(CultureInfo.InvariantCulture);
            parameters["voteCountGte"] = ratingFilter.MinimumVotes.ToString(CultureInfo.InvariantCulture);
        }
        else if (listType is DiscoverListType.TopRated or DiscoverListType.NewReleases)
        {
            var minimumVotes = Math.Max(0, Plugin.GetConfigOrDefault<int>(nameof(PluginConfiguration.MinimumVotes)));
            if (minimumVotes > 0)
            {
                parameters["voteCountGte"] = minimumVotes.ToString(CultureInfo.InvariantCulture);
            }
        }

        return parameters;
    }

    /// <summary>
    /// Fetches a Discover list for all networks with one pull-based merge over the whole library (8b fix 5 —
    /// Mohammad's correction: Pages × 20 visible titles TOTAL for the library, whatever its type and however
    /// many networks it uses). The streams of every network are pulled round-robin (rank 1 of every network,
    /// then rank 2, ... — the task 2b network merge), and within a network movies and shows alternate by rank
    /// exactly as task 2b ranked them (movie 1, show 1, movie 2, show 2, ...). Each stream fetches its next
    /// Seerr page only when its queue runs dry — then the whole arriving page is judged in one
    /// <see cref="FilterVisibleAsync"/> pass against the shared (mediaType, TMDB id) / folder-hash dedupe —
    /// and each pulled title takes the next rank. Fetching stops when the library holds Pages × 20 visible
    /// titles or every stream is exhausted (list end, failed page, or the 5 × Pages page cap, which stays PER
    /// stream); Pages ≤ 0 keeps unlimited fetching until the lists end. A Movies-only library gets no show
    /// streams, a Series-only library no movie streams.
    /// </summary>
    /// <param name="listType">The list being fetched; decides whether a vote floor is sent without a Minimum Rating.</param>
    /// <param name="extraMovieParameters">The list's extra parameters for the movies endpoint (sort order, date window).</param>
    /// <param name="extraTvParameters">The list's extra parameters for the TV endpoint.</param>
    /// <param name="libraryType">Movies-only libraries skip the TV calls, Series-only libraries the movie calls.</param>
    /// <param name="state">This library's fetch bookkeeping (counted titles, skip counters).</param>
    /// <param name="pages">The library's Pages; 0 or less means unlimited (no early stop).</param>
    /// <returns>The library's visible titles in pull order (ranked 1..N), plus the hidden titles still on the list</returns>
    private async Task<(List<IJellyseerrItem> Visible, List<IJellyseerrItem> Hidden)> FetchDiscoverMediaAsync(
        DiscoverListType listType,
        Dictionary<string, object> extraMovieParameters,
        Dictionary<string, object> extraTvParameters,
        DiscoverLibraryType libraryType,
        VisibleFetchState state,
        int pages)
    {
        // The current library's networks (LibraryScope.GetNetworks), not the global list — the fetcher runs
        // inside LibraryScope.Enter, and reading config.NetworkMap directly would bypass the scope.
        var networkMap = Utils.LibraryScope.GetNetworks();
        if (networkMap == null || !networkMap.Any())
        {
            networkMap = new List<JellyseerrNetwork>();
            _logger.LogDebug("No networks configured in NetworkMap. Searching all networks in the default region.");
            networkMap.Add(new JellyseerrNetwork { Id = 0, Name = "All", Country = "Default", DisplayPriority = 0 });
        }

        var target = pages <= 0 ? int.MaxValue : pages * 20;
        var capPages = pages <= 0 ? int.MaxValue : pages * 5;

        // One movie stream + one show stream per network (the library's kind filter first); the pull below
        // alternates between the two of a network and round-robins over the networks.
        var pulls = new List<NetworkPull>();
        foreach (var network in networkMap)
        {
            _logger.LogTrace("Fetching discover content for network: {NetworkName} (ID: {NetworkId}, Region: {Country}, Priority: {DisplayPriority})", network.Name, network.Id, network.Country, network.DisplayPriority);

            var networkParameters = new Dictionary<string, object> {};
            if (network.Id != 0)
            {
                // Add network Id and Country parameters to query parameters
                networkParameters = new Dictionary<string, object> {
                    ["watchRegion"] = network.Country,
                    ["watchProviders"] = network.Id
                };
            }

            // Let Jellyseerr leave out titles under the minimum rating, so each page is filled with titles that pass
            foreach (var (parameter, value) in GetSeerrRatingParameters(listType))
            {
                networkParameters[parameter] = value;
            }

            // The list's own sort order and date window go on top of the network and rating parameters
            var movieParameters = new Dictionary<string, object>(networkParameters);
            foreach (var (parameter, value) in extraMovieParameters ?? new Dictionary<string, object>())
            {
                movieParameters[parameter] = value;
            }

            var tvParameters = new Dictionary<string, object>(networkParameters);
            foreach (var (parameter, value) in extraTvParameters ?? new Dictionary<string, object>())
            {
                tvParameters[parameter] = value;
            }

            // Debug: Log the parameters being used
            _logger.LogTrace("Calling discover endpoints with movie parameters: {MovieParameters} and TV parameters: {TvParameters}",
                string.Join(", ", movieParameters.Select(kvp => $"{kvp.Key}={kvp.Value}")),
                string.Join(", ", tvParameters.Select(kvp => $"{kvp.Key}={kvp.Value}")));

            pulls.Add(new NetworkPull(
                libraryType != DiscoverLibraryType.Series ? new DiscoverStream(network, JellyseerrEndpoint.DiscoverMovies, movieParameters) : null,
                libraryType != DiscoverLibraryType.Movies ? new DiscoverStream(network, JellyseerrEndpoint.DiscoverTv, tvParameters) : null));
        }

        // Pull one title per network, round-robin, until the library holds Pages × 20 visible titles. Titles
        // already judged visible and sitting in a queue occupy part of the remaining budget, so the streams
        // together never fetch or count past the target (task 8: past the target is never counted or written).
        var merged = new List<IJellyseerrItem>();
        var hidden = new List<IJellyseerrItem>();
        var queuedVisible = 0;
        var rank = 0;
        var livePulls = true;
        while (merged.Count < target && livePulls)
        {
            var pulledThisRound = false;
            foreach (var pull in pulls)
            {
                if (merged.Count >= target)
                {
                    break;
                }

                var budget = target == int.MaxValue ? int.MaxValue : Math.Max(0, target - merged.Count - queuedVisible);
                var item = await PullNextVisibleAsync(state, pull, capPages, budget, hidden);
                if (item == null)
                {
                    continue; // this network is exhausted for now
                }

                queuedVisible--;
                item.ListRank = ++rank;
                merged.Add(item);
                pulledThisRound = true;
            }

            livePulls = pulledThisRound;
        }

        // Only log warning if this network returned nothing from either endpoint
        foreach (var pull in pulls)
        {
            if (pull.VisibleCount == 0)
            {
                var network = (pull.Movies ?? pull.Shows)!.Network;
                _logger.LogWarning("No movies or shows returned for network: {NetworkName} (ID: {NetworkId}) [Country: {Country}]", network.Name, network.Id, network.Country);
            }
        }

        return (merged, hidden);
    }

    /// <summary>
    /// Fetches Jellyseerr's trending list (movies and shows; people are skipped), as one mixed stream (task 8):
    /// page by page until the library holds Pages × 20 visible titles, the list ends (empty page or page
    /// ≥ totalPages) or the cap of 5 × Pages pages is reached. ListRank numbers the visible titles 1, 2, 3...
    /// in visible order — titles of the other kind and hidden ones are skipped while fetching, so they occupy
    /// no rank. 8b fix 3: the hidden titles still on the list come back with the visible ones, so the sync's
    /// stale-trending removal does not delete them.
    /// </summary>
    /// <param name="libraryType">Movies-only libraries count no shows, Series-only libraries no movies.</param>
    /// <param name="state">This library's fetch bookkeeping (counted titles, skip counters).</param>
    /// <param name="pages">The library's Pages; 0 or less means unlimited.</param>
    /// <returns>The visible trending titles in Seerr's order (ListRank + the Trending tag), and the hidden ones</returns>
    private async Task<(List<IJellyseerrItem> Visible, List<IJellyseerrItem> Hidden)> FetchTrendingMediaAsync(DiscoverLibraryType libraryType, VisibleFetchState state, int pages)
    {
        var target = pages <= 0 ? int.MaxValue : pages * 20;
        var capPages = pages <= 0 ? int.MaxValue : pages * 5;
        var visible = new List<IJellyseerrItem>();
        var hidden = new List<IJellyseerrItem>();
        // TMDB's trending pages can repeat a title; the first position counts
        var seen = new HashSet<(string?, int)>();
        var rank = 0;
        for (var page = 1; visible.Count < target && page <= capPages; page++)
        {
            var (items, totalPages) = await _apiService.CallEndpointPageAsync(JellyseerrEndpoint.DiscoverTrending, page, parameters: null);
            state.SeerrPages++;
            var pageItems = new List<IJellyseerrItem>();
            var pageResults = 0;
            foreach (var result in items.OfType<System.Text.Json.JsonElement>())
            {
                pageResults++;
                var mediaType = result.TryGetProperty("mediaType", out var type) ? type.GetString() : null;
                IJellyseerrItem? item = mediaType switch
                {
                    "movie" => JellyBridgeJsonSerializer.Deserialize<JellyseerrMovie>(result.GetRawText()),
                    "tv" => JellyBridgeJsonSerializer.Deserialize<JellyseerrShow>(result.GetRawText()),
                    _ => null,
                };
                if (item == null || item.Id <= 0 || !seen.Add((mediaType, item.Id)))
                {
                    continue;
                }

                // The library type filter — counted as a skip in the log line (Trending Movies skips shows)
                if (libraryType == DiscoverLibraryType.Movies && item.MediaType != JellyseerrModel.MediaType.MOVIE
                    || libraryType == DiscoverLibraryType.Series && item.MediaType != JellyseerrModel.MediaType.TV)
                {
                    state.WrongType++;
                    continue;
                }

                pageItems.Add(item);
            }

            if (pageResults == 0)
            {
                // The list ends here — no point asking for more pages of nothing
                break;
            }

            var (pageVisible, pageHidden) = await FilterVisibleAsync(state, pageItems, target - visible.Count);
            hidden.AddRange(pageHidden);
            foreach (var item in pageVisible)
            {
                item.ListRank = ++rank;
                item.NetworkTag = DiscoverLibraryConfig.TrendingTag;
                visible.Add(item);
            }

            if (totalPages > 0 && page >= totalPages)
            {
                break;
            }
        }

        _logger.LogDebug("Fetched {VisibleCount} visible trending titles for the library", visible.Count);
        return (visible, hidden);
    }

    /// <summary>
    /// Pulls the next visible title from one network (8b fix 5): the network's movies and shows streams
    /// alternate by rank (the task 2b order), an empty or absent stream skips its turn. When the preferred
    /// stream's queue is empty it fetches its next Seerr page and judges the whole arriving page in one
    /// <see cref="FilterVisibleAsync"/> pass. Returns null when both streams of the network are exhausted.
    /// </summary>
    /// <param name="state">This library's fetch bookkeeping (counted titles, skip counters).</param>
    /// <param name="pull">The network's stream pair.</param>
    /// <param name="capPages">The per-stream cap of 5 × Pages Seerr pages (int.MaxValue when unlimited).</param>
    /// <param name="budget">Titles the whole library may still take; a stream fetches no further page without it.</param>
    /// <param name="hiddenOut">Receives the titles still on the list the fetch judged hidden (8b fix 3/4).</param>
    /// <returns>The network's next visible title, or null when the network is exhausted</returns>
    private async Task<IJellyseerrItem?> PullNextVisibleAsync(VisibleFetchState state, NetworkPull pull, int capPages, int budget, List<IJellyseerrItem> hiddenOut)
    {
        for (var turn = 0; turn < 2; turn++)
        {
            var stream = pull.NextIsMovie ? pull.Movies : pull.Shows;
            if (stream is { Queue.Count: > 0 })
            {
                pull.NextIsMovie = !pull.NextIsMovie;
                return stream.Queue.Dequeue();
            }

            if (stream is { Done: false } && budget > 0)
            {
                await FillStreamAsync(state, stream, capPages, budget, hiddenOut);
                if (stream.Queue.Count > 0)
                {
                    pull.NextIsMovie = !pull.NextIsMovie;
                    return stream.Queue.Dequeue();
                }
            }

            pull.NextIsMovie = !pull.NextIsMovie; // this side is empty/dead — the other side gets the next turn
        }

        return null;
    }

    /// <summary>
    /// Fetches one Seerr page of the stream (its next page when the queue ran dry, 8b fix 5), judges the
    /// whole arriving page in one visible-filter pass against the shared dedupe, and queues the visible
    /// titles. The page cap of 5 × Pages stays PER stream; an empty page or page ≥ totalPages ends the
    /// stream (a failed page ends it too — ApiService logs why).
    /// </summary>
    private async Task FillStreamAsync(VisibleFetchState state, DiscoverStream stream, int capPages, int budget, List<IJellyseerrItem> hiddenOut)
    {
        if (stream.NextPage > capPages)
        {
            stream.Done = true;
            return;
        }

        var (items, totalPages) = await _apiService.CallEndpointPageAsync(stream.Endpoint, stream.NextPage, stream.Parameters);
        state.SeerrPages++;
        stream.NextPage++;

        var pageItems = items.Cast<IJellyseerrItem>().ToList();
        if (pageItems.Count == 0)
        {
            // The list ends here — no point asking for more pages of nothing
            stream.Done = true;
            return;
        }

        var (visible, hidden) = await FilterVisibleAsync(state, pageItems, budget);
        foreach (var item in visible)
        {
            // Set the network tag for this item
            item.NetworkTag = stream.Network.Name;
            item.NetworkId = stream.Network.Id;
            stream.Queue.Enqueue(item);
        }

        hiddenOut.AddRange(hidden);
        stream.VisibleCount += visible.Count;

        if (totalPages > 0 && stream.NextPage - 1 >= totalPages)
        {
            stream.Done = true;
        }
    }

    /// <summary>
    /// Keeps the titles the current sync would NOT hide (task 8: Pages counts the titles you see). Reuses the
    /// sync's own hide predicates: the BLACKLISTED status, the (mediaType, TMDB id) and folder-hash dedupe the
    /// fetcher already does, the existing .ignore file in the title's folder (favorited or requested through
    /// JellyBridge), and the already-in-a-real-Jellyfin-library match that feeds the sync's ignore step — which
    /// hides only when ExcludeFromMainLibraries is on (with it off the sync deletes those .ignore files, so the
    /// title stays visible). A title joins the counted sets only once it is visible, so a hidden title's twin
    /// on a later page is re-checked instead of silently counted. 8b fix 3: the titles skipped as hidden are
    /// returned too — the sync still treats them as ON the list (trending removal, retention). 8b fix 6: a
    /// page-local pending check keeps within-page duplicates out of the match batch, and the original page
    /// order is restored before returning (the match batch must not reorder the page).
    /// </summary>
    /// <param name="state">This library's fetch bookkeeping (counted titles, skip counters).</param>
    /// <param name="items">The page's freshly fetched titles, in Seerr's order.</param>
    /// <param name="budget">How many more titles the whole library fetch may still take; once the budget is
    /// used up the rest of the page is left uncounted and unwritten.</param>
    /// <returns>The titles that count as visible in the page's original order, and the titles hidden</returns>
    private async Task<(List<IJellyseerrItem> Visible, List<IJellyseerrItem> Hidden)> FilterVisibleAsync(VisibleFetchState state, List<IJellyseerrItem> items, int budget)
    {
        var visible = new List<(int Order, IJellyseerrItem Item)>();
        var hidden = new List<IJellyseerrItem>();
        var needMatch = new List<(int Order, IJellyseerrItem Item)>();
        // 8b fix 6: a title heading for the match step is not in the counted sets yet, so track the
        // page-local keys too — otherwise the same title twice on one page matched and counted twice.
        var pendingKeys = new HashSet<(JellyseerrModel.MediaType, int)>();
        var pendingFolderHashes = new HashSet<int>();
        for (var order = 0; order < items.Count; order++)
        {
            var item = items[order];
            if (visible.Count >= budget)
            {
                // The library reached its Pages × 20 target mid-page: the titles after it are never counted,
                // never skipped — the fetch simply stops here (task 8).
                break;
            }
            if (item.MediaInfo?.Status == MediaStatus.BLACKLISTED)
            {
                state.Blocked++;
                continue;
            }

            if (!MeetsRatingFilter(item))
            {
                // The sync's cleanup deletes titles below the Minimum Rating / Votes floor
                // (CleanupService.DeleteExpiredMedia); a title the Seerr query let through below the floor
                // would not stay, so it does not count toward the target.
                state.BelowRating++;
                continue;
            }

            if (state.CountedIds.Contains((item.MediaType, item.Id)) || state.CountedFolderHashes.Contains(item.GetFolderHashCode()))
            {
                state.Duplicates++;
                continue;
            }

            var ignored = _bridgeService.IsItemIgnored(item);
            if (state.ExcludeMainLibraries)
            {
                if (ignored)
                {
                    // A .ignore in the title's folder: favorited/requested through JellyBridge — hidden.
                    state.Hidden++;
                    hidden.Add(item);
                    continue;
                }

                if (!pendingKeys.Add((item.MediaType, item.Id)) || !pendingFolderHashes.Add(item.GetFolderHashCode()))
                {
                    state.Duplicates++;
                    continue;
                }

                needMatch.Add((order, item));
            }
            else if (!ignored)
            {
                // Including main libraries: no .ignore, so the title stays visible whichever way the scan matches.
                AcceptVisible(state, item, visible, order);
            }
            else
            {
                // With main libraries included, a real-library match even gets its .ignore deleted — it counts
                // as visible; only an .ignore whose title is NOT in a real library stays hidden.
                if (!pendingKeys.Add((item.MediaType, item.Id)) || !pendingFolderHashes.Add(item.GetFolderHashCode()))
                {
                    state.Duplicates++;
                    continue;
                }

                needMatch.Add((order, item));
            }
        }

        if (needMatch.Count > 0)
        {
            state.RealLibraryItems ??= await _bridgeService.GetRealLibraryItemsAsync();
            var matches = await _bridgeService.ScanLibraryAsync(state.RealLibraryItems, needMatch.Select(m => m.Item).ToList());
            var matchedHashes = matches.Select(match => match.JellyseerrItem.GetItemHashCode()).ToHashSet();
            foreach (var (order, item) in needMatch)
            {
                if (visible.Count >= budget)
                {
                    break;
                }
                var isMatch = matchedHashes.Contains(item.GetItemHashCode());
                if (state.ExcludeMainLibraries ? isMatch : !isMatch)
                {
                    state.Hidden++;
                    hidden.Add(item);
                    continue;
                }

                AcceptVisible(state, item, visible, order);
            }
        }

        // 8b fix 6: restore the page's original order — the .ignore'd titles that went through the match
        // batch used to land after the directly accepted ones, reordering the ranks within the page.
        return (visible.OrderBy(v => v.Order).Select(v => v.Item).ToList(), hidden);
    }

    /// <summary>
    /// Counts a title as visible: it reserves its (mediaType, TMDB id) and folder hash for the target and joins
    /// the fetched list, keeping the page position it had (order fix of 8b).
    /// </summary>
    private static void AcceptVisible(VisibleFetchState state, IJellyseerrItem item, List<(int Order, IJellyseerrItem Item)> visible, int order)
    {
        state.CountedIds.Add((item.MediaType, item.Id));
        state.CountedFolderHashes.Add(item.GetFolderHashCode());
        visible.Add((order, item));
    }

    /// <summary>
    /// One endpoint stream of one network for the pull-based whole-library fetch (8b fix 5): its query
    /// parameters, the queue of visible titles already judged from fetched pages, and how far it got (next
    /// Seerr page, done flag).
    /// </summary>
    private sealed class DiscoverStream
    {
        internal DiscoverStream(JellyseerrNetwork network, JellyseerrEndpoint endpoint, Dictionary<string, object> parameters)
        {
            Network = network;
            Endpoint = endpoint;
            Parameters = parameters;
        }

        /// <summary>The network this stream queries (for the item tags and the empty-network warning).</summary>
        internal readonly JellyseerrNetwork Network;

        /// <summary>The endpoint (movies or TV) of this stream.</summary>
        internal readonly JellyseerrEndpoint Endpoint;

        /// <summary>The endpoint's query parameters (network, rating and list filters).</summary>
        internal readonly Dictionary<string, object> Parameters;

        /// <summary>Visible titles judged from fetched pages, waiting for their pull.</summary>
        internal readonly Queue<IJellyseerrItem> Queue = new();

        /// <summary>The next Seerr page to fetch (1-based).</summary>
        internal int NextPage = 1;

        /// <summary>True when the stream is exhausted: list end, page cap reached or a failed page.</summary>
        internal bool Done;

        /// <summary>Titles this stream judged visible (queued or pulled) — decides the empty-network warning.</summary>
        internal int VisibleCount;
    }

    /// <summary>
    /// One network's pair of streams for the pull-based fetch, with the task 2b alternation within the network
    /// (movie 1, show 1, movie 2, show 2 ...; a single-kind library just drains its one stream).
    /// </summary>
    private sealed class NetworkPull
    {
        internal NetworkPull(DiscoverStream? movies, DiscoverStream? shows)
        {
            Movies = movies;
            Shows = shows;
        }

        /// <summary>The network's movie stream, null for a Series library.</summary>
        internal readonly DiscoverStream? Movies;

        /// <summary>The network's show stream, null for a Movies library.</summary>
        internal readonly DiscoverStream? Shows;

        /// <summary>Which side takes the next pull; flips on every pull so movies and shows alternate.</summary>
        internal bool NextIsMovie = true;

        /// <summary>Titles both streams judged visible — decides the empty-network warning.</summary>
        internal int VisibleCount => (Movies?.VisibleCount ?? 0) + (Shows?.VisibleCount ?? 0);
    }

    /// <summary>
    /// Per-library fetch bookkeeping for the pages-count-visible rule (task 8): the titles already counted
    /// toward the Pages × 20 target (so a duplicate never counts twice), this library's real-Jellyfin titles
    /// for the already-available predicate (built lazily — they cannot change during one fetch), and the skip
    /// counters of the per-library log line.
    /// </summary>
    private sealed class VisibleFetchState
    {
        /// <summary>Titles already counted, by (media type, TMDB id).</summary>
        internal readonly HashSet<(JellyseerrModel.MediaType, int)> CountedIds = new();

        /// <summary>Folder hashes already counted (the localized-duplicate predicate).</summary>
        internal readonly HashSet<int> CountedFolderHashes = new();

        /// <summary>The current library's titles outside the bridge folders; null until first needed.</summary>
        internal List<JellyfinModels.IJellyfinItem>? RealLibraryItems;

        /// <summary>Seerr pages fetched for the whole library (every stream).</summary>
        internal int SeerrPages;

        /// <summary>Titles skipped: the other media kind (a Movies/Series library's Trending pages).</summary>
        internal int WrongType;

        /// <summary>Titles skipped: another copy already counted in this fetch.</summary>
        internal int Duplicates;

        /// <summary>Titles skipped: BLACKLISTED in Jellyseerr, so they can't be requested.</summary>
        internal int Blocked;
        /// <summary>Titles skipped: below the Minimum Rating / Votes floor (the cleanup predicate).</summary>
        internal int BelowRating;

        /// <summary>Titles skipped: hidden — an existing .ignore, or a real-library match while ExcludeFromMainLibraries is on.</summary>
        internal int Hidden;

        /// <summary>Whether real-Jellyfin-library matches end up hidden (the sync's ignore-step predicate).</summary>
        internal readonly bool ExcludeMainLibraries =
            Plugin.GetConfigOrDefault<bool>(nameof(PluginConfiguration.ExcludeFromMainLibraries));
    }

    #endregion

    /// <summary>
    /// The Minimum Rating and Minimum Votes to import Discover titles with, or null when no Minimum Rating is set.
    /// The Trending library is never filtered, so it keeps matching Jellyseerr's trending page.
    /// </summary>
    public static (double MinimumRating, int MinimumVotes)? GetRatingFilter()
    {
        var minimumRating = Plugin.GetConfigOrDefault<double>(nameof(PluginConfiguration.MinimumRating));
        if (minimumRating <= 0 || LibraryScope.Current?.ListType == DiscoverListType.Trending)
        {
            return null;
        }
        return (Math.Min(minimumRating, 10), Math.Max(0, Plugin.GetConfigOrDefault<int>(nameof(PluginConfiguration.MinimumVotes))));
    }

    /// <summary>
    /// Whether a title's TMDB rating and vote count meet the Minimum Rating and Minimum Votes (always true without a Minimum Rating).
    /// </summary>
    public static bool MeetsRatingFilter(IJellyseerrItem item)
    {
        if (GetRatingFilter() is not { } ratingFilter)
        {
            return true;
        }
        var (rating, votes) = item switch
        {
            JellyseerrMovie movie => (movie.VoteAverage, movie.VoteCount),
            JellyseerrShow show => (show.VoteAverage, show.VoteCount),
            _ => (double.MaxValue, int.MaxValue)
        };
        return rating >= ratingFilter.MinimumRating && votes >= ratingFilter.MinimumVotes;
    }

    #region Created

    /// <summary>
    /// Redraws placeholders that aren't their title's current promo (a default promo whose artwork has since arrived, or a
    /// title promo for older text or an older look), including titles this sync didn't fetch. Returns the number replaced.
    /// </summary>
    public async Task<int> RedrawStalePromosAsync(IEnumerable<IJellyseerrItem> items)
    {
        if (!Plugin.GetConfigOrDefault<bool>(nameof(PluginConfiguration.UseTitlePromos)))
        {
            return 0;
        }
        var upgraded = 0;
        foreach (var item in items)
        {
            try
            {
                upgraded += await _placeholderVideoGenerator.RedrawStalePromosAsync(_metadataService.GetJellyBridgeItemDirectory(item));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not redraw the promo of {ItemName}", item.MediaName);
            }
        }
        return upgraded;
    }
    
    /// <summary>
    /// Creates placeholder videos for the provided unmatched items.
    /// </summary>
    public async Task<List<TJellyseerr>> CreatePlaceholderVideosAsync<TJellyseerr>(
        List<TJellyseerr> unmatchedItems) 
        where TJellyseerr : IJellyseerrItem
    {
        var processedItems = new List<TJellyseerr>();
        var tasks = new List<Task>();
        var seenFolders = new HashSet<string>();
        
        _logger.LogDebug("Processing {UnmatchedCount} unmatched items for placeholder creation", 
            unmatchedItems.Count);
        
        foreach (var item in unmatchedItems)
        {
            try
            {
                // Get the folder path for this item
                var folderPath = _metadataService.GetJellyBridgeItemDirectory(item);
                var normalizedFolder = FolderUtils.GetNormalizedPath(folderPath);
                if (FolderUtils.FolderExistsThrowNull(normalizedFolder) && !seenFolders.Add(normalizedFolder))
                {
                    _logger.LogTrace("Skipping duplicate placeholder for {ItemName} at {FolderPath}", item.MediaName, normalizedFolder);
                    continue;
                }
                
                // Create placeholder video based on media type
                if (item is JellyseerrMovie)
                {
                    tasks.Add(_placeholderVideoGenerator.GeneratePlaceholderMovieAsync(folderPath));
                }
                else if (item is JellyseerrShow)
                {
                    tasks.Add(_placeholderVideoGenerator.GeneratePlaceholderSeasonAsync(folderPath));
                }
                
                processedItems.Add(item);
                _logger.LogTrace("Created placeholder video for {ItemName}", item.MediaName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating placeholder video for {ItemName}", item.MediaName);
            }
        }
        
        // Await all placeholder video tasks
        try
        {
            await Task.WhenAll(tasks);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "One or more tasks failed.");
        }
        
        _logger.LogDebug("Completed - Processed {ProcessedCount} items", 
            processedItems.Count);
        
        return processedItems;
    }

    /// <summary>
    /// Filters duplicate media items from each library using the folder hash code.
    /// Returns a list containing only unique items based on their hash code.
    /// </summary>
    /// <param name="allItems">List of media items to filter</param>
    /// <param name="uniqueItems">List of unique media items</param>
    /// <returns>Tuple of (newly ignored items, existing ignored items)</returns>
    public async Task<(List<IJellyseerrItem> NewlyIgnored, List<IJellyseerrItem> ExistingIgnored)> IgnoreJellyBridgeDuplicates(List<IJellyseerrItem> allItems, List<IJellyseerrItem> uniqueItems)
    {
        var newlyIgnored = new List<IJellyseerrItem>();
        var existingIgnored = new List<IJellyseerrItem>();
        var duplicates = new List<IJellyseerrItem>();
        
        // Filter out localized language duplicates by hash
        var uniqueFolderHashes = new HashSet<int>(uniqueItems.Select(item => item.GetFolderHashCode()));

        foreach (var item in allItems)
        {
            if (!uniqueFolderHashes.Contains(item.GetFolderHashCode()))
            {
                duplicates.Add(item);
            }
        }

        var ignoreFileTasks = new List<Task>();

        foreach (var duplicate in duplicates)
        {
            var bridgeFolderPath = _metadataService.GetJellyBridgeItemDirectory(duplicate);
            var ignoreFilePath = Path.Combine(bridgeFolderPath, BridgeService.IgnoreFileName);
            try
            {
                if (File.Exists(ignoreFilePath))
                {
                    existingIgnored.Add(duplicate);
                    _logger.LogTrace("Ignore file already exists for {ItemName} in {BridgeFolder}", duplicate.MediaName, bridgeFolderPath);
                }
                else
                {
                    // Serialize using the actual runtime type - the converter handles this automatically
                    var itemJson = JellyBridgeJsonSerializer.Serialize(duplicate);
                    ignoreFileTasks.Add(_bridgeService.CreateIgnoreFileAsync(ignoreFilePath, itemJson));
                    newlyIgnored.Add(duplicate);
                    _logger.LogTrace("Created ignore file for {ItemName} in {BridgeFolder}", duplicate.MediaName, bridgeFolderPath);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating ignore file for {ItemName}", duplicate.MediaName);
            }
        }

        try
        {
            await Task.WhenAll(ignoreFileTasks);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "One or more tasks failed.");
        }
        return (newlyIgnored, existingIgnored);
    }

    #endregion
    
    #region Seasons

    private const int SeasonFetchConcurrency = 4;
    private static readonly HashSet<string> VideoExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp4", ".mkv", ".avi", ".m4v", ".mov", ".ts", ".m2ts", ".webm", ".wmv", ".mpg", ".mpeg", ".iso"
    };

    /// <summary>
    /// Get Seerr TV details (seasons + per-season status). Seerr caches these itself.
    /// Returns null if Seerr did not return a valid show.
    /// </summary>
    public async Task<JellyseerrTvDetails?> GetTvDetailsAsync(int tmdbId)
    {
        var result = await _apiService.CallEndpointAsync(JellyseerrEndpoint.TvDetails,
            templates: new Dictionary<string, string> { ["tmdbId"] = tmdbId.ToString(CultureInfo.InvariantCulture) });
        if (result is JellyseerrTvDetails details && details.Id > 0)
        {
            return details;
        }

        _logger.LogWarning("Could not read seasons for TV show TMDB {TmdbId} from Jellyseerr; leaving its season placeholders unchanged", tmdbId);
        return null;
    }

    /// <summary>
    /// Ensure every show folder has one "Season NN/SNNE9999.mp4" placeholder per requestable season (NN > 0)
    /// and remove placeholder-only season folders for seasons that are no longer requestable.
    /// Season folders hidden with an .ignore file (requested through JellyBridge) are never touched here.
    /// "Season 00/S00E9999.mp4" (the series promo used for sorting) is not touched either.
    /// </summary>
    /// <returns>Number of season placeholders created and season folders removed.</returns>
    public async Task<(int created, int removed)> EnsureSeasonPlaceholdersAsync(List<JellyseerrShow> shows, bool enabled)
    {
        if (!enabled)
        {
            return (0, RemoveAllSeasonPlaceholders());
        }

        var seenFolders = new HashSet<string>();
        var tasks = new List<Task<(int created, int removed)>>();
        using var throttle = new SemaphoreSlim(SeasonFetchConcurrency);

        foreach (var show in shows)
        {
            try
            {
                var showFolder = _metadataService.GetJellyBridgeItemDirectory(show);
                if (!FolderUtils.FolderExistsThrowNull(showFolder) || !seenFolders.Add(FolderUtils.GetNormalizedPath(showFolder)))
                {
                    continue;
                }
                if (File.Exists(Path.Combine(showFolder, BridgeService.IgnoreFileName)))
                {
                    continue;
                }
                tasks.Add(EnsureShowSeasonsAsync(show, showFolder, throttle));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error preparing season placeholders for {ItemName}", show.MediaName);
            }
        }

        var created = 0;
        var removed = 0;
        try
        {
            foreach (var (c, r) in await Task.WhenAll(tasks))
            {
                created += c;
                removed += r;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "One or more season placeholder tasks failed.");
        }

        _logger.LogDebug("Season placeholders: {Created} created, {Removed} season folders removed for {ShowCount} shows",
            created, removed, tasks.Count);
        return (created, removed);
    }

    private async Task<(int created, int removed)> EnsureShowSeasonsAsync(JellyseerrShow show, string showFolder, SemaphoreSlim throttle)
    {
        var created = 0;
        var removed = 0;
        try
        {
            JellyseerrTvDetails? details;
            await throttle.WaitAsync();
            try
            {
                details = await GetTvDetailsAsync(show.Id);
            }
            finally
            {
                throttle.Release();
            }

            if (details == null)
            {
                return (0, 0);
            }

            var wanted = details.GetRequestableSeasons();

            var wantedNumbers = wanted.Select(s => s.SeasonNumber).ToHashSet();

            foreach (var season in wanted)
            {
                var seasonFolder = Path.Combine(showFolder, PlaceholderVideoGenerator.GetSeasonFolderName(season.SeasonNumber));
                if (File.Exists(Path.Combine(seasonFolder, BridgeService.IgnoreFileName)))
                {
                    continue;
                }
                var placeholderPath = Path.Combine(seasonFolder, PlaceholderVideoGenerator.GetSeasonPlaceholderFilename(season.SeasonNumber));
                var isNew = !File.Exists(placeholderPath);
                if (await _placeholderVideoGenerator.GeneratePlaceholderSeasonNumberAsync(showFolder, season.SeasonNumber, overwrite: false))
                {
                    await WriteSeasonNfosAsync(seasonFolder, season);
                    if (isNew)
                    {
                        created++;
                        _logger.LogTrace("Created season placeholder {Path}", placeholderPath);
                    }
                }
            }

            // Remove placeholder-only season folders that are no longer requestable (e.g. now available or requested elsewhere)
            foreach (var dir in Directory.GetDirectories(showFolder))
            {
                var number = PlaceholderVideoGenerator.ParseSeasonFolderNumber(dir);
                if (!number.HasValue || number.Value <= 0 || wantedNumbers.Contains(number.Value))
                {
                    continue;
                }
                if (File.Exists(Path.Combine(dir, BridgeService.IgnoreFileName)))
                {
                    continue;
                }
                if (!IsPlaceholderOnlySeasonFolder(dir, number.Value))
                {
                    _logger.LogWarning("Season folder {Dir} has no placeholder or contains other videos; not removing it", dir);
                    continue;
                }
                Directory.Delete(dir, recursive: true);
                removed++;
                _logger.LogTrace("Removed season placeholder folder {Dir} (season no longer requestable)", dir);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error ensuring season placeholders for {ItemName}", show.MediaName);
        }
        return (created, removed);
    }

    /// <summary>
    /// Remove every season placeholder folder in the JellyBridge library, used when season placeholders are off.
    /// </summary>
    private int RemoveAllSeasonPlaceholders()
    {
        var baseDirectory = FolderUtils.GetBaseDirectory();
        if (string.IsNullOrEmpty(baseDirectory) || !Directory.Exists(baseDirectory))
        {
            return 0;
        }

        var removed = 0;
        foreach (var dir in Directory.EnumerateDirectories(baseDirectory, "Season *", SearchOption.AllDirectories).ToList())
        {
            var number = PlaceholderVideoGenerator.ParseSeasonFolderNumber(dir);
            if (!number.HasValue || number.Value <= 0 || !IsPlaceholderOnlySeasonFolder(dir, number.Value))
            {
                continue;
            }

            try
            {
                Directory.Delete(dir, recursive: true);
                removed++;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not remove season placeholder folder {Dir}", dir);
            }
        }

        return removed;
    }

    private static bool IsPlaceholderOnlySeasonFolder(string seasonFolder, int seasonNumber)
    {
        var placeholderName = PlaceholderVideoGenerator.GetSeasonPlaceholderFilename(seasonNumber);
        var videos = Directory.GetFiles(seasonFolder, "*", SearchOption.AllDirectories)
            .Where(f => VideoExtensions.Contains(Path.GetExtension(f)))
            .ToList();
        return videos.Count > 0
            && videos.All(f => string.Equals(Path.GetFileName(f), placeholderName, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Write season.nfo (title + number) and an episode NFO for the placeholder so clients show a helpful title.
    /// Files are only rewritten when their content changes.
    /// </summary>
    private static async Task WriteSeasonNfosAsync(string seasonFolder, JellyseerrTvSeason season)
    {
        // Seerr/TMDB sometimes returns bare numbers ("1") as season names; use "Season N" for those
        var hasRealName = !string.IsNullOrWhiteSpace(season.Name) && !season.Name!.Trim().All(char.IsDigit);
        var title = hasRealName ? season.Name!.Trim() : $"Season {season.SeasonNumber}";
        var seasonNfo = "<?xml version=\"1.0\" encoding=\"utf-8\" standalone=\"yes\"?>\n" +
            "<season>\n" +
            $"  <title>{SecurityElement.Escape(title)}</title>\n" +
            $"  <seasonnumber>{season.SeasonNumber}</seasonnumber>\n" +
            "</season>\n";
        var episodeNfo = "<?xml version=\"1.0\" encoding=\"utf-8\" standalone=\"yes\"?>\n" +
            "<episodedetails>\n" +
            $"  <title>{SecurityElement.Escape($"Favorite this season to request {title}")}</title>\n" +
            $"  <season>{season.SeasonNumber}</season>\n" +
            "  <episode>9999</episode>\n" +
            $"  <plot>{SecurityElement.Escape($"JellyBridge placeholder. Add {title} to your favorites to request it in Jellyseerr" + (season.EpisodeCount.HasValue ? $" ({season.EpisodeCount} episodes)." : "."))}</plot>\n" +
            "</episodedetails>\n";

        await WriteIfChangedAsync(Path.Combine(seasonFolder, "season.nfo"), seasonNfo);
        await WriteIfChangedAsync(Path.Combine(seasonFolder,
            Path.GetFileNameWithoutExtension(PlaceholderVideoGenerator.GetSeasonPlaceholderFilename(season.SeasonNumber)) + ".nfo"), episodeNfo);
    }

    private static async Task WriteIfChangedAsync(string path, string content)
    {
        if (File.Exists(path) && await File.ReadAllTextAsync(path) == content)
        {
            return;
        }
        await File.WriteAllTextAsync(path, content);
    }

    #endregion

    #region Ignore

    /// <summary>
    /// Recursively deletes all .ignore files from the Jellyseerr bridge directory.
    /// </summary>
    public Task<(List<IJellyseerrItem> newUnignored, List<IJellyseerrItem> existingUnignored)> DeleteIgnoreFilesAsync(List<JellyMatch> matched)
    {
        var syncDirectory = FolderUtils.GetBaseDirectory();
        List<IJellyseerrItem> existingUnignored = new List<IJellyseerrItem>();
        
        try
        {
            _logger.LogTrace("Starting recursive deletion of .ignore files from: {SyncDirectory}", syncDirectory);
            
            foreach (var matchedItem in matched)
            {
                (var jellyseerrItem, _) = matchedItem.ToTuple();
                var bridgeFolderPath = _metadataService.GetJellyBridgeItemDirectory(jellyseerrItem);
                var ignoreFilePath = Path.Combine(bridgeFolderPath, BridgeService.IgnoreFileName);
                if (File.Exists(ignoreFilePath))
                {
                    try
                    {
                        File.Delete(ignoreFilePath);
                        existingUnignored.Add(jellyseerrItem);
                        _logger.LogDebug("Deleted .ignore file for matched item: {IgnoreFile}", ignoreFilePath);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to delete .ignore file for matched item: {IgnoreFile}", ignoreFilePath);
                    }
                }
            }

            _logger.LogTrace("Completed deletion of .ignore files. Deleted {DeletedCount} files out of {TotalCount} found", 
                existingUnignored.Count, matched.Count);
            
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during .ignore file deletion");
        }
        return Task.FromResult((new List<IJellyseerrItem>(), existingUnignored));
    }

    /// <summary>
    /// Ignores items that have invalid NetworkIds (not in NetworkMap configuration).
    /// Reads metadata internally and creates .ignore files for invalid items.
    /// Returns a tuple of (newly ignored items, existing ignored items).
    /// </summary>
    public async Task<(List<IJellyseerrItem> NewlyIgnored, List<IJellyseerrItem> ExistingIgnored)> IgnoreInvalidNetworkItemsAsync()
    {
        // Read all bridge folder metadata
        var (movies, shows) = await _metadataService.ReadMetadataAsync();
        
        // Combine movies and shows into a single list
        var items = new List<IJellyseerrItem>();
        items.AddRange(movies.Cast<IJellyseerrItem>());
        items.AddRange(shows.Cast<IJellyseerrItem>());
        
        if (items.Count == 0)
        {
            return (new List<IJellyseerrItem>(), new List<IJellyseerrItem>());
        }

        // FetchDiscoverMediaAsync searches all networks (Id 0) when no networks are configured, so those items are valid too
        var networkMap = Utils.LibraryScope.GetNetworks();
        if (networkMap == null || !networkMap.Any())
        {
            networkMap = new List<JellyseerrNetwork> { new JellyseerrNetwork { Id = 0, Name = "All", Country = "Default", DisplayPriority = 0 } };
        }
        var newlyIgnored = new List<IJellyseerrItem>();
        var existingIgnored = new List<IJellyseerrItem>();
        var ignoreFileTasks = new List<Task>();

        foreach (var item in items)
        {
            try
            {
                // Titles added from search have no network; they stay until the retention cleanup removes them
                if (item.NetworkId == SearchService.SearchNetworkId)
                {
                    continue;
                }

                // Check if NetworkId is valid
                if (!item.NetworkId.HasValue)
                {
                    // No NetworkId - mark as ignored
                    var bridgeFolderPath = _metadataService.GetJellyBridgeItemDirectory(item);
                    var ignoreFilePath = Path.Combine(bridgeFolderPath, BridgeService.IgnoreFileName);
                    
                    try
                    {
                        if (File.Exists(ignoreFilePath))
                        {
                            existingIgnored.Add(item);
                            _logger.LogTrace("Ignore file already exists for item with no NetworkId: {ItemName}", item.MediaName);
                        }
                        else
                        {
                            var itemJson = JellyBridgeJsonSerializer.Serialize(item);
                            ignoreFileTasks.Add(_bridgeService.CreateIgnoreFileAsync(ignoreFilePath, itemJson));
                            newlyIgnored.Add(item);
                            _logger.LogTrace("Ignored item with no NetworkId: {ItemName}", item.MediaName);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error creating ignore file for {ItemName}", item.MediaName);
                    }
                }
                else if (!networkMap.Any(network => network.Id == item.NetworkId.Value))
                {
                    // NetworkId not in NetworkMap - mark as ignored
                    var bridgeFolderPath = _metadataService.GetJellyBridgeItemDirectory(item);
                    var ignoreFilePath = Path.Combine(bridgeFolderPath, BridgeService.IgnoreFileName);
                    
                    try
                    {
                        if (File.Exists(ignoreFilePath))
                        {
                            existingIgnored.Add(item);
                            _logger.LogTrace("Ignore file already exists for item with invalid NetworkId {NetworkId}: {ItemName}", item.NetworkId, item.MediaName);
                        }
                        else
                        {
                            var itemJson = JellyBridgeJsonSerializer.Serialize(item);
                            ignoreFileTasks.Add(_bridgeService.CreateIgnoreFileAsync(ignoreFilePath, itemJson));
                            newlyIgnored.Add(item);
                            _logger.LogTrace("Ignored item with invalid NetworkId {NetworkId}: {ItemName}", item.NetworkId, item.MediaName);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error creating ignore file for {ItemName}", item.MediaName);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error filtering invalid network item: {ItemName}", item?.MediaName);
            }
        }

        try
        {
            await Task.WhenAll(ignoreFileTasks);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "One or more tasks failed.");
        }
        
        var totalIgnored = newlyIgnored.Count + existingIgnored.Count;
        if (totalIgnored > 0)
        {
            _logger.LogDebug("Filtered and ignored {IgnoredCount} items with invalid NetworkIds ({NewlyIgnored} newly ignored, {ExistingIgnored} already ignored)", 
                totalIgnored, newlyIgnored.Count, existingIgnored.Count);
        }

        return (newlyIgnored, existingIgnored);
    }

    #endregion
}