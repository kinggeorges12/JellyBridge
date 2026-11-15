using Jellyfin.Plugin.JellyBridge.BridgeModels;
using Jellyfin.Plugin.JellyBridge.JellyseerrModel;
using Jellyfin.Plugin.JellyBridge.JellyseerrModel.Server;
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
    /// Fetches discover data for all networks, calling both movies and TV endpoints for each network.
    /// </summary>
    /// <returns>Tuple containing lists of movies and shows fetched from all networks</returns>
    public async Task<(List<JellyseerrMovie>, List<JellyseerrShow>)> FetchDiscoverMediaAsync()
    {
        var config = Plugin.GetConfiguration();
        var networkMap = config?.NetworkMap;
        var allMovies = new List<JellyseerrMovie>();
        var allShows = new List<JellyseerrShow>();

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

            // Debug: Log the parameters being used
            _logger.LogTrace("Calling discover endpoints with parameters: {Parameters}", 
                string.Join(", ", networkParameters.Select(kvp => $"{kvp.Key}={kvp.Value}")));
            
            // Fetch movies for this network
            var moviesData = await _apiService.CallEndpointAsync(JellyseerrEndpoint.DiscoverMovies, parameters: networkParameters);
            var movies = moviesData as List<JellyseerrMovie> ?? new List<JellyseerrMovie>();
            
            // Fetch TV shows for this network
            var showsData = await _apiService.CallEndpointAsync(JellyseerrEndpoint.DiscoverTv, parameters: networkParameters);
            var shows = showsData as List<JellyseerrShow> ?? new List<JellyseerrShow>();
            
            // Debug: Log the response data
            _logger.LogTrace("API calls returned {MovieCount} movies and {ShowCount} shows for {NetworkName}", movies.Count, shows.Count, network.Name);
            
            // Add items to lists (no deduplication), skipping titles blocklisted in Jellyseerr since they can't be requested
            foreach (var item in movies.Where(m => m.MediaInfo?.Status != MediaStatus.BLACKLISTED))
            {
                // Set the network tag for this item
                item.NetworkTag = network.Name;
                item.NetworkId = network.Id;
                allMovies.Add(item);
            }
            
            foreach (var item in shows.Where(s => s.MediaInfo?.Status != MediaStatus.BLACKLISTED))
            {
                // Set the network tag for this item
                item.NetworkTag = network.Name;
                item.NetworkId = network.Id;
                allShows.Add(item);
            }
            
            // Only log warning if no items found for either endpoint
            if (allMovies.Count == 0 && allShows.Count == 0)
            {
                _logger.LogWarning("No movies or shows returned for network: {NetworkName} (ID: {NetworkId}) [Country: {Country}]", network.Name, network.Id, network.Country);
            }
        
            _logger.LogTrace("Retrieved {MovieCount} movies and {ShowCount} shows for {NetworkName}", movies.Count, shows.Count, network.Name);
        }
        
        return (allMovies, allShows);
    }

    #endregion

    #region Created
    
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
    /// If UseNetworkFolders and AddDuplicateContent are both enabled, filters by library and excludes existing metadata items.
    /// </summary>
    /// <param name="allItems">List of media items to filter</param>
    /// <param name="uniqueItems">List of unique media items</param>
    /// <returns>Tuple of (newly ignored items, existing ignored items)</returns>
    public async Task<(List<IJellyseerrItem> NewlyIgnored, List<IJellyseerrItem> ExistingIgnored)> IgnoreJellyBridgeDuplicates(List<IJellyseerrItem> allItems, List<IJellyseerrItem> uniqueItems)
    {
        var newlyIgnored = new List<IJellyseerrItem>();
        var existingIgnored = new List<IJellyseerrItem>();
        var useNetworkFolders = Plugin.GetConfigOrDefault<bool>(nameof(PluginConfiguration.UseNetworkFolders));
        var addDuplicateContent = Plugin.GetConfigOrDefault<bool>(nameof(PluginConfiguration.AddDuplicateContent));
        var duplicates = new List<IJellyseerrItem>();
        
        // Network folders are not used, but we need to filter out localized language duplicates by hash
        var uniqueFolderHashes = new HashSet<int>(uniqueItems.Select(item => item.GetFolderHashCode(network: useNetworkFolders && addDuplicateContent)));

        foreach (var item in allItems)
        {
            if (!uniqueFolderHashes.Contains(item.GetFolderHashCode(network: useNetworkFolders && addDuplicateContent)))
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
            $"  <title>{SecurityElement.Escape(title)}</title>\n" +
            $"  <season>{season.SeasonNumber}</season>\n" +
            "  <episode>9999</episode>\n" +
            $"  <plot>{SecurityElement.Escape($"JellyBridge placeholder. Add the show to your favorites to request it in Jellyseerr" + (season.EpisodeCount.HasValue ? $" ({season.EpisodeCount} episodes)." : "."))}</plot>\n" +
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
        var networkMap = Plugin.GetConfiguration()?.NetworkMap;
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