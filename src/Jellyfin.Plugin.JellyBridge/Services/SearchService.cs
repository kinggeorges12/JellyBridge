using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.JellyBridge.BridgeModels;
using Jellyfin.Plugin.JellyBridge.Configuration;
using Jellyfin.Plugin.JellyBridge.JellyseerrModel;
using Jellyfin.Plugin.JellyBridge.Utils;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.IO;
using MediaBrowser.Model.Providers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JellyBridge.Services;

/// <summary>
/// Seerr titles for Jellyfin's search: titles that are in no library show up as results and are added to the Discover library when opened.
/// </summary>
public class SearchService
{
    private const int MaxResults = 10;
    /// <summary>
    /// Network id for titles added from search, so the sync doesn't hide them as titles from a network that isn't selected.
    /// </summary>
    public const int SearchNetworkId = -1;

    private const string PosterBaseUrl = "https://image.tmdb.org/t/p/w342";
    private const string ArtworkBaseUrl = "https://image.tmdb.org/t/p/w780";
    private const string BackdropBaseUrl = "https://image.tmdb.org/t/p/w1280";
    private const int ArtworkConcurrency = 4;
    private const string TmdbProviderName = "TheMovieDb";
    private static readonly TimeSpan SearchCacheTime = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan OpenTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan ArtworkTimeout = TimeSpan.FromSeconds(4);

    private readonly ConcurrentDictionary<string, (DateTimeOffset At, Task<List<IJellyseerrItem>> Search)> _searches = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<Guid, IJellyseerrItem> _results = new();
    private readonly ConcurrentDictionary<Guid, SemaphoreSlim> _openLocks = new();
    private readonly ConcurrentDictionary<Guid, (byte[] Bytes, string ContentType)> _posters = new();

    private readonly DebugLogger<SearchService> _logger;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILibraryManager _libraryManager;
    private readonly IUserManager _userManager;
    private readonly IProviderManager _providerManager;
    private readonly ILibraryMonitor _libraryMonitor;
    private readonly IFileSystem _fileSystem;
    private readonly IHttpClientFactory _httpClientFactory;

    public SearchService(
        ILogger<SearchService> logger,
        IServiceScopeFactory scopeFactory,
        ILibraryManager libraryManager,
        IUserManager userManager,
        IProviderManager providerManager,
        ILibraryMonitor libraryMonitor,
        IFileSystem fileSystem,
        IHttpClientFactory httpClientFactory)
    {
        _logger = new DebugLogger<SearchService>(logger);
        _scopeFactory = scopeFactory;
        _libraryManager = libraryManager;
        _userManager = userManager;
        _providerManager = providerManager;
        _libraryMonitor = libraryMonitor;
        _fileSystem = fileSystem;
        _httpClientFactory = httpClientFactory;
    }

    /// <summary>
    /// Whether Seerr results are added to Jellyfin's search. Network folders are not supported since search results have no network.
    /// </summary>
    public static bool IsEnabled =>
        Plugin.GetConfigOrDefault<bool>(nameof(PluginConfiguration.EnableSearchResults))
        && !Plugin.GetConfigOrDefault<bool>(nameof(PluginConfiguration.UseNetworkFolders));

    /// <summary>
    /// True for the id of a search result card.
    /// </summary>
    public bool IsSearchResult(Guid id) => _results.ContainsKey(id);

    /// <summary>
    /// Start (or reuse) a Seerr search for the term.
    /// </summary>
    public Task<List<IJellyseerrItem>> StartSearch(string term)
    {
        var now = DateTimeOffset.UtcNow;
        var key = term.Trim();
        var entry = _searches.AddOrUpdate(
            key,
            _ => (now, SearchSeerrAsync(key)),
            (_, old) => now - old.At < SearchCacheTime && !old.Search.IsFaulted ? old : (now, SearchSeerrAsync(key)));
        return entry.Search;
    }

    /// <summary>
    /// True when the user can see a library that holds the JellyBridge folder.
    /// </summary>
    public bool UserCanSeeDiscover(Guid userId)
    {
        var user = _userManager.GetUserById(userId);
        if (user == null)
        {
            return false;
        }

        var baseDirectory = FolderUtils.GetBaseDirectory();
        return _libraryManager.GetUserRootFolder().GetChildren(user, true).OfType<CollectionFolder>()
            .Any(library => library.PhysicalLocations.Any(path =>
                FolderUtils.IsPathInDirectory(path, baseDirectory) || FolderUtils.IsPathInDirectory(baseDirectory, path)));
    }

    /// <summary>
    /// Result cards for the titles that are not already in the page or in any library.
    /// </summary>
    public List<BaseItemDto> CreateCards(IEnumerable<IJellyseerrItem> found, IEnumerable<BaseItemDto> page, bool movies, bool shows, string serverId)
    {
        var shown = page
            .Select(dto => dto.ProviderIds != null && dto.ProviderIds.TryGetValue(nameof(MetadataProvider.Tmdb), out var tmdb) ? tmdb : null)
            .Where(tmdb => tmdb != null)
            .ToHashSet();

        return found
            .Where(item => item is JellyseerrMovie ? movies : shows)
            .Where(item => !shown.Contains(item.Id.ToString()) && FindInLibrary(item) == null)
            .Take(MaxResults)
            .Select(item => CreateCard(item, serverId))
            .ToList();
    }

    /// <summary>
    /// Poster for a search result card, fetched from TMDB and kept in memory.
    /// </summary>
    public async Task<(byte[] Bytes, string ContentType)?> GetPosterAsync(Guid id, CancellationToken cancellationToken)
    {
        if (_posters.TryGetValue(id, out var cached))
        {
            return cached;
        }

        if (!_results.TryGetValue(id, out var item) || string.IsNullOrEmpty(PosterPath(item)))
        {
            return null;
        }

        try
        {
            using var response = await _httpClientFactory.CreateClient().GetAsync(PosterBaseUrl + PosterPath(item), cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            var poster = (await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false),
                response.Content.Headers.ContentType?.MediaType ?? "image/jpeg");
            _posters[id] = poster;
            return poster;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            _logger.LogDebug("Could not fetch poster for {ItemName}: {Message}", item.MediaName, ex.Message);
            return null;
        }
    }

    /// <summary>
    /// Add a search result to the Discover library and return the Jellyfin item id, or the existing item if it is already there.
    /// </summary>
    public async Task<Guid?> OpenAsync(Guid id, CancellationToken cancellationToken)
    {
        if (!_results.TryGetValue(id, out var item))
        {
            return null;
        }

        var openLock = _openLocks.GetOrAdd(id, _ => new SemaphoreSlim(1, 1));
        await openLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (FindInLibrary(item) is { } existing)
            {
                return existing.Id;
            }

            using var scope = _scopeFactory.CreateScope();
            var metadataService = scope.ServiceProvider.GetRequiredService<MetadataService>();
            var discoverService = scope.ServiceProvider.GetRequiredService<DiscoverService>();
            var folder = metadataService.GetJellyBridgeItemDirectory(item);
            var parentPath = Path.GetDirectoryName(folder);
            if (string.IsNullOrEmpty(parentPath) || _libraryManager.FindByPath(parentPath, isFolder: true) is not Folder parent)
            {
                _logger.LogWarning("Cannot add {ItemName} from search: {Folder} is not in a Jellyfin library", item.MediaName, parentPath);
                return null;
            }

            // One time budget for the whole open, so it stays within what clients wait for
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(OpenTimeout);
            var started = DateTimeOffset.UtcNow;

            // Keep the library monitor (if enabled) from adding the same folder while it is written
            _libraryMonitor.ReportFileSystemChangeBeginning(folder);
            try
            {
                item.NetworkId ??= SearchNetworkId;

                // An older sync may have hidden this title with a .ignore (titles from search had no network then). Search only
                // offers titles that aren't in any library and can still be requested, so that .ignore is stale
                var ignoreFile = Path.Combine(folder, BridgeService.IgnoreFileName);
                if (File.Exists(ignoreFile))
                {
                    File.Delete(ignoreFile);
                    scope.ServiceProvider.GetRequiredService<RefreshService>().ClearDotIgnoreCache();
                    _logger.LogDebug("Removed the stale .ignore of {ItemName} before adding it from search", item.MediaName);
                }

                await metadataService.CreateFolderMetadataAsync(new List<IJellyseerrItem> { item }).ConfigureAwait(false);

                // Pictures first, so the title promo made below can use them
                await SaveArtworkAsync(item, folder, deadline.Token).ConfigureAwait(false);

                // The default promo goes in first (a quick copy) so Jellyfin can build the item right away; the title promo
                // renders meanwhile and replaces it before the open call returns, so the first play already has it
                var titlePromos = Plugin.GetConfigOrDefault<bool>(nameof(PluginConfiguration.UseTitlePromos));
                await CreatePlaceholdersAsync(discoverService, item, defaultPromosOnly: titlePromos).ConfigureAwait(false);
                var titlePromoTask = titlePromos ? Task.Run(() => RefreshTitlePromosAsync(item)) : Task.CompletedTask;

                Guid created;
                try
                {
                    created = await CreateLibraryItemAsync(folder, parent, item is JellyseerrShow, deadline.Token, cancellationToken,
                        () => RefreshTitlePromosAsync(item)).ConfigureAwait(false);
                }
                finally
                {
                    await titlePromoTask.ConfigureAwait(false);
                }

                _logger.LogInformation("Added {ItemName} to the Discover library from search in {Seconds:0.0} s", item.MediaName, (DateTimeOffset.UtcNow - started).TotalSeconds);
                return created;
            }
            finally
            {
                _libraryMonitor.ReportFileSystemChangeComplete(folder, false);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Error adding {ItemName} from search", item.MediaName);
            return null;
        }
        finally
        {
            openLock.Release();
        }
    }

    private async Task<List<IJellyseerrItem>> SearchSeerrAsync(string term)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var apiService = scope.ServiceProvider.GetRequiredService<ApiService>();
            var response = await apiService.CallEndpointAsync(JellyseerrEndpoint.Search,
                parameters: new Dictionary<string, object> { ["query"] = term }).ConfigureAwait(false) as JellyseerrSearchResponse;

            var items = new List<IJellyseerrItem>();
            foreach (var result in response?.Results ?? new List<JsonElement>())
            {
                var mediaType = result.TryGetProperty("mediaType", out var type) ? type.GetString() : null;
                IJellyseerrItem? item = mediaType switch
                {
                    "movie" => JellyBridgeJsonSerializer.Deserialize<JellyseerrMovie>(result.GetRawText()),
                    "tv" => JellyBridgeJsonSerializer.Deserialize<JellyseerrShow>(result.GetRawText()),
                    _ => null,
                };

                // Only titles that can still be requested: not requested, available or blocklisted in Seerr
                var status = (item as JellyseerrMovie)?.MediaInfo?.Status ?? (item as JellyseerrShow)?.MediaInfo?.Status;
                if (item == null || item.Id <= 0 || (status != null && status != MediaStatus.UNKNOWN && status != MediaStatus.DELETED))
                {
                    continue;
                }

                _results[SearchId(item)] = item;
                items.Add(item);
            }

            _logger.LogDebug("Seerr search for {Term} returned {Count} requestable titles", term, items.Count);
            return items;
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Seerr search for {Term} failed: {Message}", term, ex.Message);
            return new List<IJellyseerrItem>();
        }
    }

    private BaseItem? FindInLibrary(IJellyseerrItem item) =>
        _libraryManager.GetItemList(new InternalItemsQuery
        {
            IncludeItemTypes = new[] { item is JellyseerrMovie ? BaseItemKind.Movie : BaseItemKind.Series },
            HasAnyProviderId = new Dictionary<string, string> { [nameof(MetadataProvider.Tmdb)] = item.Id.ToString() },
            Recursive = true,
        }).FirstOrDefault();

    private async Task<Guid> CreateLibraryItemAsync(string folder, Folder parent, bool isShow, CancellationToken deadline, CancellationToken cancellationToken,
        Func<Task>? afterBackgroundRefresh = null)
    {
        // A fresh DirectoryService, a cached listing would not include the files just written
        var directoryService = new DirectoryService(_fileSystem);
        var resolved = _libraryManager.ResolvePath(_fileSystem.GetDirectoryInfo(folder), parent, directoryService)
            ?? throw new InvalidOperationException($"Jellyfin did not recognize {folder}");
        var item = _libraryManager.GetItemById(resolved.Id) ?? resolved;
        if (_libraryManager.GetItemById(item.Id) == null)
        {
            _libraryManager.CreateItem(item, parent);
        }

        // Build the item (and a show's seasons and episodes) from the files just written, including the artwork saved next to
        // them; fetching metadata online can take longer than clients wait, so that runs in the background
        var quick = new MetadataRefreshOptions(directoryService)
        {
            MetadataRefreshMode = MetadataRefreshMode.ValidationOnly,
            ImageRefreshMode = MetadataRefreshMode.ValidationOnly,
        };
        try
        {
            if (isShow)
            {
                await _providerManager.RefreshFullItem(item, quick, deadline).ConfigureAwait(false);
            }
            else
            {
                await _providerManager.RefreshSingleItem(item, quick, deadline).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogDebug("Adding {ItemName} is taking a while, finishing in the background", item.Name);
        }
        finally
        {
            // Full refresh in the background, including a show's seasons and episodes (QueueRefresh only covers the show itself).
            // Also when the client gave up, so the item never stays without metadata.
            var full = new MetadataRefreshOptions(new DirectoryService(_fileSystem))
            {
                MetadataRefreshMode = MetadataRefreshMode.Default,
                ImageRefreshMode = MetadataRefreshMode.Default,
            };
            _ = Task.Run(async () =>
            {
                try
                {
                    // Season posters first: the w780 size is quicker than the originals the full refresh fetches. Done here
                    // rather than before returning, so opening a show is not held up by them
                    if (isShow)
                    {
                        await SaveSeasonPostersAsync(item, CancellationToken.None).ConfigureAwait(false);
                    }

                    await _providerManager.RefreshFullItem(item, full, CancellationToken.None).ConfigureAwait(false);
                    if (afterBackgroundRefresh != null)
                    {
                        await afterBackgroundRefresh().ConfigureAwait(false);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning("Background refresh of {ItemName} failed: {Message}", item.Name, ex.Message);
                }
            });
        }

        return item.Id;
    }

    /// <summary>
    /// With title artwork promos on, make the placeholder videos again once the background refresh has saved the rest of the
    /// artwork (the logo), so they don't have to wait for the next sync.
    /// </summary>
    private async Task RefreshTitlePromosAsync(IJellyseerrItem item)
    {
        if (!Plugin.GetConfigOrDefault<bool>(nameof(PluginConfiguration.UseTitlePromos)))
        {
            return;
        }

        try
        {
            using var scope = _scopeFactory.CreateScope();
            await CreatePlaceholdersAsync(scope.ServiceProvider.GetRequiredService<DiscoverService>(), item, defaultPromosOnly: false)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Could not make the title promo of {ItemName}: {Message}", item.MediaName, ex.Message);
        }
    }

    /// <summary>
    /// Make a title's placeholder videos (a show's season placeholders too). With <paramref name="defaultPromosOnly"/> they all
    /// use the default promo, which is a quick copy.
    /// </summary>
    private static async Task CreatePlaceholdersAsync(DiscoverService discoverService, IJellyseerrItem item, bool defaultPromosOnly)
    {
        // Set inside this method, so it applies to the calls below and not to the caller
        PlaceholderVideoGenerator.DefaultPromosOnly.Value = defaultPromosOnly;
        if (item is JellyseerrShow show)
        {
            await discoverService.CreatePlaceholderVideosAsync(new List<JellyseerrShow> { show }).ConfigureAwait(false);
            await discoverService.EnsureSeasonPlaceholdersAsync(new List<JellyseerrShow> { show },
                Plugin.GetConfigOrDefault<bool>(nameof(PluginConfiguration.UseSeasonPlaceholders))).ConfigureAwait(false);
        }
        else if (item is JellyseerrMovie movie)
        {
            await discoverService.CreatePlaceholderVideosAsync(new List<JellyseerrMovie> { movie }).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Give each season of a show its TMDB poster right after it is added, ahead of the slower full refresh.
    /// Uses Jellyfin's own TMDB image provider (the same pick a metadata refresh makes) but skips the metadata providers.
    /// Whatever is not done within the budget comes from the full refresh.
    /// </summary>
    private async Task SaveSeasonPostersAsync(BaseItem show, CancellationToken cancellationToken)
    {
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        budget.CancelAfter(ArtworkTimeout);
        var started = DateTimeOffset.UtcNow;
        var seasons = _libraryManager.GetItemList(new InternalItemsQuery
        {
            IncludeItemTypes = new[] { BaseItemKind.Season },
            AncestorIds = new[] { show.Id },
        })
            .Where(season => !season.HasImage(ImageType.Primary))
            .OrderBy(season => season.IndexNumber is null or 0 ? int.MaxValue : season.IndexNumber.Value)
            .ToList();
        if (seasons.Count == 0)
        {
            return;
        }

        using var throttle = new SemaphoreSlim(ArtworkConcurrency);
        var saved = 0;
        try
        {
            await Task.WhenAll(seasons.Select(async season =>
            {
                await throttle.WaitAsync(budget.Token).ConfigureAwait(false);
                try
                {
                    var images = await _providerManager.GetAvailableRemoteImages(season,
                        new RemoteImageQuery(TmdbProviderName) { ImageType = ImageType.Primary }, budget.Token).ConfigureAwait(false);
                    var url = images.FirstOrDefault()?.Url;
                    if (string.IsNullOrEmpty(url))
                    {
                        return;
                    }

                    // The original files are several MB; the w780 size is plenty for a season poster and much quicker
                    await _providerManager.SaveImage(season, url.Replace("/t/p/original/", "/t/p/w780/", StringComparison.Ordinal),
                        ImageType.Primary, null, budget.Token).ConfigureAwait(false);
                    await season.UpdateToRepositoryAsync(ItemUpdateType.ImageUpdate, budget.Token).ConfigureAwait(false);
                    Interlocked.Increment(ref saved);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogDebug("Could not save the poster of {SeasonName}: {Message}", season.Name, ex.Message);
                }
                finally
                {
                    throttle.Release();
                }
            })).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Best effort, see the summary
        }

        _logger.LogDebug("Saved {Saved} of {Count} season posters for {ItemName} in {Seconds:0.0} s",
            saved, seasons.Count, show.Name, (DateTimeOffset.UtcNow - started).TotalSeconds);
    }

    /// <summary>
    /// Save the poster and backdrop from TMDB as local artwork (folder.jpg and backdrop.jpg, the names Jellyfin itself saves
    /// them under), so the item has them the moment Jellyfin builds it. Seerr has no season posters; those are saved by
    /// <see cref="SaveSeasonPostersAsync"/>.
    /// </summary>
    private async Task SaveArtworkAsync(IJellyseerrItem item, string folder, CancellationToken cancellationToken)
    {
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        budget.CancelAfter(ArtworkTimeout);
        var started = DateTimeOffset.UtcNow;
        var files = new List<(string Url, string Path)>();
        if (PosterPath(item) is { Length: > 0 } posterPath)
        {
            files.Add((ArtworkBaseUrl + posterPath, Path.Combine(folder, "folder.jpg")));
        }

        if (((item as JellyseerrMovie)?.BackdropPath ?? (item as JellyseerrShow)?.BackdropPath) is { Length: > 0 } backdropPath)
        {
            files.Add((BackdropBaseUrl + backdropPath, Path.Combine(folder, "backdrop.jpg")));
        }

        try
        {
            var client = _httpClientFactory.CreateClient();
            using var throttle = new SemaphoreSlim(ArtworkConcurrency);
            var saved = await Task.WhenAll(files.Where(file => !File.Exists(file.Path)).Select(async file =>
            {
                await throttle.WaitAsync(budget.Token).ConfigureAwait(false);
                try
                {
                    using var response = await client.GetAsync(file.Url, budget.Token).ConfigureAwait(false);
                    if (!response.IsSuccessStatusCode)
                    {
                        return false;
                    }

                    var bytes = await response.Content.ReadAsByteArrayAsync(budget.Token).ConfigureAwait(false);
                    var temp = file.Path + ".part";
                    await File.WriteAllBytesAsync(temp, bytes, CancellationToken.None).ConfigureAwait(false);
                    File.Move(temp, file.Path, overwrite: true);
                    return true;
                }
                catch (HttpRequestException ex)
                {
                    _logger.LogDebug("Could not fetch {Url}: {Message}", file.Url, ex.Message);
                    return false;
                }
                finally
                {
                    throttle.Release();
                }
            })).ConfigureAwait(false);
            _logger.LogDebug("Saved {Saved} of {Count} pictures for {ItemName} in {Seconds:0.0} s",
                saved.Count(ok => ok), saved.Length, item.MediaName, (DateTimeOffset.UtcNow - started).TotalSeconds);
        }
        catch (OperationCanceledException)
        {
            // Best effort: the pictures that are missing come from the background refresh
            _logger.LogDebug("Pictures for {ItemName} were not all saved within {Seconds} s", item.MediaName, ArtworkTimeout.TotalSeconds);
        }
    }

    private BaseItemDto CreateCard(IJellyseerrItem item, string serverId)
    {
        var isMovie = item is JellyseerrMovie;
        var card = new BaseItemDto
        {
            Id = SearchId(item),
            ServerId = serverId,
            Name = item.MediaName,
            SortName = item.MediaName,
            ProductionYear = int.TryParse(item.Year, out var year) ? year : null,
            Overview = (item as JellyseerrMovie)?.Overview ?? (item as JellyseerrShow)?.Overview,
            Type = isMovie ? BaseItemKind.Movie : BaseItemKind.Series,
            IsFolder = !isMovie,
            MediaType = isMovie ? Jellyfin.Data.Enums.MediaType.Video : Jellyfin.Data.Enums.MediaType.Unknown,
            LocationType = LocationType.Virtual,
            CanDelete = false,
            ProviderIds = new Dictionary<string, string> { [nameof(MetadataProvider.Tmdb)] = item.Id.ToString() },
            ImageTags = new Dictionary<ImageType, string>(),
        };

        var posterPath = PosterPath(item);
        if (!string.IsNullOrEmpty(posterPath))
        {
            card.ImageTags[ImageType.Primary] = "jbsearch" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(posterPath)).AsSpan(0, 8)).ToLowerInvariant();
            card.PrimaryImageAspectRatio = 2.0 / 3.0;
        }

        return card;
    }

    private static string? PosterPath(IJellyseerrItem item) => (item as JellyseerrMovie)?.PosterPath ?? (item as JellyseerrShow)?.PosterPath;

    /// <summary>
    /// Stable id for a search result, so the same title keeps the same card id across searches.
    /// </summary>
    private static Guid SearchId(IJellyseerrItem item) =>
        new Guid(MD5.HashData(Encoding.UTF8.GetBytes($"jellybridge/search/{(item is JellyseerrMovie ? "movie" : "tv")}/{item.Id}")));
}
