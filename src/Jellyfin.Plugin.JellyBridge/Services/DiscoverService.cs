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
    /// Task 8 (Mohammad's rule): Pages counts the titles you see. Every stream the library fetches — one per
    /// media kind it holds times each network, one mixed stream for Trending — keeps fetching Seerr pages until
    /// it has Pages × 20 visible titles, the list ends, or the cap of 5 × Pages pages per stream is reached. A
    /// fetched title counts toward the target only when the sync would NOT hide it (see
    /// <see cref="FilterVisibleAsync"/>); titles past the target are never fetched further nor written, and
    /// ListRank follows the visible order.
    /// </summary>
    /// <param name="library">The library whose list and type to fetch for.</param>
    /// <returns>Tuple containing lists of movies and shows fetched for the library</returns>
    public async Task<(List<JellyseerrMovie>, List<JellyseerrShow>)> FetchListAsync(DiscoverLibraryConfig library)
    {
        // Inside the library's scope this reads the library's own Pages with the global fallback (TrendingPages
        // for a Trending library); 0 means unlimited — fetch until the list ends, no visible target.
        var pages = Plugin.GetConfigOrDefault<int>(nameof(PluginConfiguration.MaxDiscoverPages));
        var state = new VisibleFetchState();
        List<IJellyseerrItem> visible;
        if (library.ListType == DiscoverListType.Trending)
        {
            visible = await FetchTrendingMediaAsync(library.LibraryType, state, pages);
        }
        else
        {
            var (listMovieParameters, listTvParameters) = GetListParameters(library.ListType);
            visible = await FetchDiscoverMediaAsync(library.ListType, listMovieParameters, listTvParameters,
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

        return (visible.OfType<JellyseerrMovie>().ToList(), visible.OfType<JellyseerrShow>().ToList());
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
    /// Fetches discover data for all networks, calling both movies and TV endpoints for each network, with the
    /// current library's list parameters merged in. Every endpoint call is one stream that keeps fetching Seerr
    /// pages until it contributed Pages × 20 visible titles (task 8); the visible titles are then merged
    /// round-robin and numbered in visible order.
    /// </summary>
    /// <param name="listType">The list being fetched; decides whether a vote floor is sent without a Minimum Rating.</param>
    /// <param name="extraMovieParameters">The list's extra parameters for the movies endpoint (sort order, date window).</param>
    /// <param name="extraTvParameters">The list's extra parameters for the TV endpoint.</param>
    /// <param name="libraryType">Movies-only libraries skip the TV calls, Series-only libraries the movie calls.</param>
    /// <param name="state">This library's fetch bookkeeping (counted titles, skip counters).</param>
    /// <param name="pages">The library's Pages; 0 or less means unlimited (no early stop).</param>
    /// <returns>The library's visible fetched titles, ranked in visible order</returns>
    private async Task<List<IJellyseerrItem>> FetchDiscoverMediaAsync(
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
        // This library's titles per network, movies and shows interleaved by page position (task 2b list rank)
        var perNetwork = new List<List<IJellyseerrItem>>();

        if (networkMap == null || !networkMap.Any())
        {
            networkMap = new List<JellyseerrNetwork>();
            _logger.LogDebug("No networks configured in NetworkMap. Searching all networks in the default region.");
            networkMap.Add(new JellyseerrNetwork { Id = 0, Name = "All", Country = "Default", DisplayPriority = 0 });
        }
        
        foreach (var network in networkMap)
        {
            _logger.LogTrace("Fetching discover content for network: {NetworkName} (ID: {NetworkId}, Region: {Country}, Priority: {DisplayPriority})", network.Name, network.Id, network.Country, network.DisplayPriority);
            
            var networkParameters = new Dictionary<string, object> {};
            if(network.Id != 0)
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
            
            // One stream per media type the library holds (a Series-only library's folder holds no movies, a
            // Movies-only one no shows): fetch Seerr page by page until the stream has its Pages × 20 visible
            // titles, the list ends, or the 5 × Pages page cap is reached (task 8).
            var movies = libraryType != DiscoverLibraryType.Series
                ? await FetchVisibleStreamAsync<JellyseerrMovie>(state, JellyseerrEndpoint.DiscoverMovies, movieParameters, pages)
                : new List<JellyseerrMovie>();
            var shows = libraryType != DiscoverLibraryType.Movies
                ? await FetchVisibleStreamAsync<JellyseerrShow>(state, JellyseerrEndpoint.DiscoverTv, tvParameters, pages)
                : new List<JellyseerrShow>();
            
            // Debug: Log the response data
            _logger.LogTrace("API calls returned {MovieCount} movies and {ShowCount} shows for {NetworkName}", movies.Count, shows.Count, network.Name);
            
            // Collect this network's titles (task 8 already skipped the blocklisted ones — they can't be requested)
            var networkItems = InterleaveByPosition(movies, shows);
            foreach (var item in networkItems)
            {
                // Set the network tag for this item
                item.NetworkTag = network.Name;
                item.NetworkId = network.Id;
            }
            perNetwork.Add(networkItems);
            
            // Only log warning if this network returned nothing from either endpoint
            if (networkItems.Count == 0)
            {
                _logger.LogWarning("No movies or shows returned for network: {NetworkName} (ID: {NetworkId}) [Country: {Country}]", network.Name, network.Id, network.Country);
            }
        
            _logger.LogTrace("Retrieved {MovieCount} movies and {ShowCount} shows for {NetworkName}", movies.Count, shows.Count, network.Name);
        }
        
        // Number the titles 1, 2, 3... in the visible order: merge the networks round-robin (rank 1 of every
        // network, then rank 2, ...), so every network's top pick beats another network's lower ones. Dedupe by
        // media type and TMDB id keeps the first, best-ranked copy — with the task 8 fetch each stream already
        // skipped counted duplicates, so this is the safety net and every rank lands 1..N.
        var merged = new List<IJellyseerrItem>();
        var seenTitles = new HashSet<(JellyseerrModel.MediaType, int)>();
        var rank = 0;
        var longestNetwork = perNetwork.Count == 0 ? 0 : perNetwork.Max(list => list.Count);
        for (var position = 0; position < longestNetwork; position++)
        {
            foreach (var networkItems in perNetwork)
            {
                if (position >= networkItems.Count)
                {
                    continue;
                }
                var item = networkItems[position];
                if (!seenTitles.Add((item.MediaType, item.Id)))
                {
                    continue;
                }

                item.ListRank = ++rank;
                merged.Add(item);
            }
        }

        return merged;
    }

    /// <summary>
    /// Merges one network's movie and show results into one list that alternates page positions (movie 1, show 1,
    /// movie 2, show 2, ...), so the list's rank mixes the two kinds the way its pages do.
    /// </summary>
    /// <param name="movies">The network's movies, in Seerr's order.</param>
    /// <param name="shows">The network's shows, in Seerr's order.</param>
    /// <returns>One merged list alternating movies and shows.</returns>
    private static List<IJellyseerrItem> InterleaveByPosition(List<JellyseerrMovie> movies, List<JellyseerrShow> shows)
    {
        var merged = new List<IJellyseerrItem>(movies.Count + shows.Count);
        for (var position = 0; position < Math.Max(movies.Count, shows.Count); position++)
        {
            if (position < movies.Count)
            {
                merged.Add(movies[position]);
            }
            if (position < shows.Count)
            {
                merged.Add(shows[position]);
            }
        }

        return merged;
    }

    /// <summary>
    /// Fetches Jellyseerr's trending list (movies and shows; people are skipped), as one mixed stream (task 8):
    /// page by page until the library's kind holds Pages × 20 visible titles, the list ends (empty page or page
    /// ≥ totalPages) or the per-stream cap of 5 × Pages pages is reached. ListRank numbers the visible titles
    /// 1, 2, 3... in visible order — titles of the other kind and hidden ones are skipped while fetching, so
    /// they occupy no rank.
    /// </summary>
    /// <param name="libraryType">Movies-only libraries count no shows, Series-only libraries no movies.</param>
    /// <param name="state">This library's fetch bookkeeping (counted titles, skip counters).</param>
    /// <param name="pages">The library's Pages; 0 or less means unlimited.</param>
    /// <returns>The visible trending titles in Seerr's order, each with its ListRank and the Trending tag</returns>
    private async Task<List<IJellyseerrItem>> FetchTrendingMediaAsync(DiscoverLibraryType libraryType, VisibleFetchState state, int pages)
    {
        var target = pages <= 0 ? int.MaxValue : pages * 20;
        var capPages = pages <= 0 ? int.MaxValue : pages * 5;
        var visible = new List<IJellyseerrItem>();
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

            var pageVisible = await FilterVisibleAsync(state, pageItems, target - visible.Count);
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
        return visible;
    }

    /// <summary>
    /// Fetches one stream (one endpoint for one network, task 8) page by page until it holds Pages × 20 visible
    /// titles, the list ends (empty page or page ≥ totalPages) or the cap of 5 × Pages pages is reached.
    /// Pages ≤ 0 means unlimited: fetch until the list ends.
    /// </summary>
    /// <typeparam name="T">JellyseerrMovie or JellyseerrShow — the stream's kind.</typeparam>
    /// <param name="state">This library's fetch bookkeeping (counted titles, skip counters).</param>
    /// <param name="endpoint">The discover endpoint for this stream.</param>
    /// <param name="parameters">The endpoint's query parameters (network, rating and list filters).</param>
    /// <param name="pages">The library's Pages; 0 or less means unlimited.</param>
    /// <returns>The stream's visible titles in Seerr's order</returns>
    private async Task<List<T>> FetchVisibleStreamAsync<T>(VisibleFetchState state, JellyseerrEndpoint endpoint,
        Dictionary<string, object> parameters, int pages)
        where T : IJellyseerrItem
    {
        var target = pages <= 0 ? int.MaxValue : pages * 20;
        var capPages = pages <= 0 ? int.MaxValue : pages * 5;
        var kept = new List<T>();
        for (var page = 1; kept.Count < target && page <= capPages; page++)
        {
            var (items, totalPages) = await _apiService.CallEndpointPageAsync(endpoint, page, parameters);
            state.SeerrPages++;
            var fetched = items.OfType<T>().ToList();
            if (fetched.Count == 0)
            {
                // The list ends here — no point asking for more pages of nothing
                break;
            }

            var visible = await FilterVisibleAsync(state, fetched.Cast<IJellyseerrItem>().ToList(), target - kept.Count);
            kept.AddRange(visible.Cast<T>());
            if (totalPages > 0 && page >= totalPages)
            {
                break;
            }
        }

        return kept;
    }

    /// <summary>
    /// Keeps the titles the current sync would NOT hide (task 8: Pages counts the titles you see). Reuses the
    /// sync's own hide predicates: the BLACKLISTED status, the (mediaType, TMDB id) and folder-hash dedupe the
    /// fetcher already does, the existing .ignore file in the title's folder (favorited or requested through
    /// JellyBridge), and the already-in-a-real-Jellyfin-library match that feeds the sync's ignore step — which
    /// hides only when ExcludeFromMainLibraries is on (with it off the sync deletes those .ignore files, so the
    /// title stays visible). A title joins the counted sets only once it is visible, so a hidden title's twin
    /// on a later page is re-checked instead of silently counted.
    /// </summary>
    /// <param name="state">This library's fetch bookkeeping (counted titles, skip counters).</param>
    /// <param name="items">The page's freshly fetched titles, in Seerr's order.</param>
    /// <param name="budget">How many more titles the caller's stream may take (its Pages × 20 minus what it
    /// already kept); once the budget is used up the rest of the page is left uncounted and unwritten.</param>
    /// <returns>The titles that count as visible, in Seerr's order</returns>
    private async Task<List<IJellyseerrItem>> FilterVisibleAsync(VisibleFetchState state, List<IJellyseerrItem> items, int budget)
    {
        var visible = new List<IJellyseerrItem>();
        var needMatch = new List<IJellyseerrItem>();
        foreach (var item in items)
        {
            if (visible.Count >= budget)
            {
                // The stream reached its Pages × 20 target mid-page: the titles after it are never counted,
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
                    continue;
                }

                needMatch.Add(item);
            }
            else if (!ignored)
            {
                // Including main libraries: no .ignore, so the title stays visible whichever way the scan matches.
                AcceptVisible(state, item, visible);
            }
            else
            {
                // With main libraries included, a real-library match even gets its .ignore deleted — it counts
                // as visible; only an .ignore whose title is NOT in a real library stays hidden.
                needMatch.Add(item);
            }
        }

        if (needMatch.Count > 0)
        {
            state.RealLibraryItems ??= await _bridgeService.GetRealLibraryItemsAsync();
            var matches = await _bridgeService.ScanLibraryAsync(state.RealLibraryItems, needMatch);
            var matchedHashes = matches.Select(match => match.JellyseerrItem.GetItemHashCode()).ToHashSet();
            foreach (var item in needMatch)
            {
                if (visible.Count >= budget)
                {
                    break;
                }
                var isMatch = matchedHashes.Contains(item.GetItemHashCode());
                if (state.ExcludeMainLibraries ? isMatch : !isMatch)
                {
                    state.Hidden++;
                    continue;
                }

                AcceptVisible(state, item, visible);
            }
        }

        return visible;
    }

    /// <summary>
    /// Counts a title as visible: it reserves its (mediaType, TMDB id) and folder hash for the target and joins
    /// the fetched list.
    /// </summary>
    private static void AcceptVisible(VisibleFetchState state, IJellyseerrItem item, List<IJellyseerrItem> visible)
    {
        state.CountedIds.Add((item.MediaType, item.Id));
        state.CountedFolderHashes.Add(item.GetFolderHashCode());
        visible.Add(item);
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