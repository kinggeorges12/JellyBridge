using Jellyfin.Plugin.JellyBridge.BridgeModels;
using Jellyfin.Plugin.JellyBridge.JellyseerrModel;
using Jellyfin.Plugin.JellyBridge.JellyseerrModel.Server;
using Jellyfin.Plugin.JellyBridge.Utils;
using Jellyfin.Plugin.JellyBridge.Configuration;
using Microsoft.Extensions.Logging;

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
    
    #region FromJellyseerr

    #region Process
    

    /// <summary>
    /// Generic method to fetch discover data for all networks using the specified endpoint.
    /// </summary>
    /// <typeparam name="T">The Jellyseerr type (JellyseerrMovie or JellyseerrShow)</typeparam>
    /// <returns>List of items fetched from all networks</returns>
    public async Task<List<T>> FetchDiscoverMediaAsync<T>() where T : TmdbMediaResult, IJellyseerrItem
    {
        var config = Plugin.GetConfiguration();
        var networkMap = config?.NetworkMap ?? new List<JellyseerrNetwork>();
        var allItems = new List<T>();
        
        foreach (var network in networkMap)
        {
            _logger.LogDebug("Fetching {MediaType} for network: {NetworkName} (ID: {NetworkId}, Country: {Country}, Priority: {DisplayPriority})", T.LibraryType, network.Name, network.Id, network.Country, network.DisplayPriority);
            
            // Add network Id and Country parameters to query parameters
            var networkParameters = new Dictionary<string, object> {
                ["watchRegion"] = network.Country,
                ["watchProviders"] = network.Id
            };
            JellyseerrEndpoint? endpoint = null;  
            if (typeof(T) == typeof(JellyseerrMovie)) {
                endpoint = JellyseerrEndpoint.DiscoverMovies;
            } else if (typeof(T) == typeof(JellyseerrShow)) {
                endpoint = JellyseerrEndpoint.DiscoverTv;
            }
            if (endpoint == null) {
                _logger.LogError("Unsupported type: {Type}", typeof(T));
                throw new InvalidOperationException($"Unsupported type: {typeof(T)}");
            }

            // Debug: Log the parameters being used
            _logger.LogTrace("Calling {Endpoint} with parameters: {Parameters}", 
                endpoint.Value, 
                string.Join(", ", networkParameters.Select(kvp => $"{kvp.Key}={kvp.Value}")));
            
            var networkData = await _apiService.CallEndpointAsync(endpoint.Value, parameters: networkParameters);
            
            if (networkData == null)
            {
                _logger.LogWarning("API call returned null for {MediaType} endpoint for network: {NetworkName}", T.LibraryType, network.Name);
                continue;
            }
            
            var items = (List<T>)networkData;
            
            // Debug: Log the response data
            _logger.LogTrace("API call returned {ItemCount} items for {NetworkName}", items.Count, network.Name);
            
            if (items.Count == 0)
            {
                _logger.LogWarning("No {MediaType} returned for network: {NetworkName}", T.LibraryType, network.Name);
            }
            
            // Add items to list (no deduplication)
            foreach (var item in items)
            {
                // Set the network tag for this item
                item.NetworkTag = network.Name;
                item.NetworkId = network.Id;
                allItems.Add(item);
            }
            
            _logger.LogTrace("Retrieved {ItemCount} {MediaType} for {NetworkName}", items.Count, T.LibraryType, network.Name);
        }
        
        _logger.LogDebug("Total {MediaType} collected: {TotalCount}", T.LibraryType, allItems.Count);
        
        return allItems;
    }
    
    /// <summary>
    /// Filters duplicate media items from a list using the GetItemHashCode method.
    /// Returns a list containing only unique items based on their hash code.
    /// If UseNetworkFolders and AddDuplicateContent are both enabled, filters by library and excludes existing metadata items.
    /// </summary>
    /// <param name="items">List of media items to filter</param>
    /// <returns>List of unique media items (or original list if duplicates should be kept)</returns>
    public async Task<List<IJellyseerrItem>> FilterDuplicateMedia(List<IJellyseerrItem> items)
    {
        // If both UseNetworkFolders and AddDuplicateContent are enabled, skip filtering
        var useNetworkFolders = Plugin.GetConfigOrDefault<bool>(nameof(PluginConfiguration.UseNetworkFolders));
        var addDuplicateContent = Plugin.GetConfigOrDefault<bool>(nameof(PluginConfiguration.AddDuplicateContent));
        
        if (useNetworkFolders && addDuplicateContent)
        {
            _logger.LogDebug("Filtering duplicates by library: UseNetworkFolders and AddDuplicateContent are both enabled");
            var libraryResults = await _bridgeService.FilterDuplicatesByLibrary(items);
            // Extract items from library-directory-item tuples into a single flat list
            var combinedItems = libraryResults.Select(tuple => tuple.item).ToList();
            _logger.LogDebug("Extracted {ItemCount} items from {LibraryCount} library-directory-item tuples", 
                combinedItems.Count, libraryResults.Count);
            return combinedItems;
        }
        
        var seenHashes = new HashSet<int>();
        var uniqueItems = new List<IJellyseerrItem>();
        
        foreach (var item in items)
        {
            if (item == null) continue;
            
            var hash = item.GetItemHashCode();
            if (seenHashes.Add(hash))
            {
                uniqueItems.Add(item);
            }
            else
            {
                _logger.LogTrace("Filtered duplicate item: {MediaName} (Id: {Id}, Hash: {Hash})", 
                    item.MediaName, item.Id, hash);
            }
        }
        
        _logger.LogDebug("Filtered {TotalCount} items to {UniqueCount} unique items", 
            items.Count, uniqueItems.Count);
        
        return uniqueItems;
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
        
        _logger.LogDebug("Processing {UnmatchedCount} unmatched items for placeholder creation", 
            unmatchedItems.Count);
        
        foreach (var item in unmatchedItems)
        {
            try
            {
                // Get the folder path for this item
                var folderPath = _metadataService.GetJellyBridgeItemDirectory(item);
                
                if (string.IsNullOrEmpty(folderPath) || !Directory.Exists(folderPath))
                {
                    throw new InvalidOperationException($"Folder does not exist for item: {item.MediaName}");
                }
                
                // Create placeholder video based on media type
                if (item is JellyseerrMovie)
                {
                    tasks.Add(_placeholderVideoGenerator.GeneratePlaceholderMovieAsync(folderPath));
                }
                else if (item is JellyseerrShow)
                {
                    tasks.Add(_placeholderVideoGenerator.GeneratePlaceholderShowAsync(folderPath));
                }
                
                processedItems.Add(item);
                _logger.LogTrace("✅ Created placeholder video for {ItemName}", item.MediaName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ ERROR creating placeholder video for {ItemName}", item.MediaName);
            }
        }
        
        // Await all placeholder video tasks
        await Task.WhenAll(tasks);
        
        _logger.LogDebug("Completed - Processed {ProcessedCount} items", 
            processedItems.Count);
        
        return processedItems;
    }

    /// <summary>
    /// Create season folders for all TV shows.
    /// Creates Season 01 through Season 12 folders with season placeholder videos for each show.
    /// </summary>
    public async Task CreateSeasonFoldersForShows(List<JellyseerrShow> shows)
    {
        _logger.LogDebug("Starting season folder creation for {ShowCount} shows", shows.Count);
        
        foreach (var show in shows)
        {
            try
            {
                var showFolderPath = _metadataService.GetJellyBridgeItemDirectory(show);
                
                _logger.LogTrace("Creating season folders for show '{MediaName}' in '{ShowFolderPath}'", 
                    show.MediaName, showFolderPath);
                
                try
                {
                    // Generate season placeholder video (calculates season folder path internally)
                    var placeholderSuccess = await _placeholderVideoGenerator.GeneratePlaceholderSeasonAsync(showFolderPath);
                    if (placeholderSuccess)
                    {
                        var seasonFolderPath = PlaceholderVideoGenerator.GetSeasonFolder(showFolderPath);
                        _logger.LogDebug("Created season placeholder for: '{SeasonFolderPath}'", seasonFolderPath);
                    }
                    else
                    {
                        var seasonFolderPath = PlaceholderVideoGenerator.GetSeasonFolder(showFolderPath);
                        _logger.LogWarning("Failed to create season placeholder for: '{SeasonFolderPath}'", seasonFolderPath);
                    }
                    
                    _logger.LogTrace("✅ Created season folder for show '{MediaName}'", show.MediaName);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error creating season folder for show '{MediaName}'", show.MediaName);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ ERROR creating season folders for show '{MediaName}'", show.MediaName);
            }
        }
        
        _logger.LogDebug("Completed season folder creation for {ShowCount} shows", shows.Count);
    }


    #endregion
    
    #region Deleted

    /// <summary>
    /// Cleans up metadata by removing items older than the specified number of days.
    /// </summary>
    public async Task<(List<JellyseerrMovie> deletedMovies, List<JellyseerrShow> deletedShows)> CleanupMetadataAsync()
    {
        var maxRetentionDays = Plugin.GetConfigOrDefault<int>(nameof(PluginConfiguration.MaxRetentionDays));
        var cutoffDate = DateTime.Now.AddDays(-maxRetentionDays);

        try
        {
            // Read all bridge folder metadata
            var (movies, shows) = await _metadataService.ReadMetadataAsync();
            
            _logger.LogDebug("Found {MovieCount} movies and {ShowCount} shows to check for cleanup", 
                movies.Count, shows.Count);

            // Process movies and shows using the same logic
            var deletedMovies = ProcessItemsForCleanup(movies);
            var deletedShows = ProcessItemsForCleanup(shows);

            _logger.LogDebug("Completed cleanup - Deleted {MovieCount} movies, {ShowCount} shows", 
                deletedMovies.Count, deletedShows.Count);
            
            return (deletedMovies, deletedShows);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during cleanup process");
            return (new List<JellyseerrMovie>(), new List<JellyseerrShow>());
        }
    }

    /// <summary>
    /// Validates that the NetworkId exists in the NetworkMap configuration.
    /// </summary>
    private bool ValidateNetworkId(IJellyseerrItem item)
    {
        // If no NetworkId is set, delete the item
        if (!item.NetworkId.HasValue)
        {
            return false;
        }

        var networkMap = Plugin.GetConfigOrDefault<List<JellyseerrNetwork>>(nameof(PluginConfiguration.NetworkMap));
        
        // Check if the NetworkId exists in the NetworkMap
        return networkMap.Any(network => network.Id == item.NetworkId.Value);
    }

    /// <summary>
    /// Helper method to process items for cleanup.
    /// </summary>
    private List<TJellyseerr> ProcessItemsForCleanup<TJellyseerr>(
        List<TJellyseerr> items) 
        where TJellyseerr : TmdbMediaResult, IJellyseerrItem
    {
        var deletedItems = new List<TJellyseerr>();
        var maxRetentionDays = Plugin.GetConfigOrDefault<int>(nameof(PluginConfiguration.MaxRetentionDays));
        var cutoffDate = DateTimeOffset.Now.AddDays(-maxRetentionDays);
        var itemType = typeof(TJellyseerr).Name.ToLower().Replace("jellyseerr", "");
        
        _logger.LogTrace("Processing {ItemCount} {ItemType}s for cleanup (older than {MaxRetentionDays} days, before {CutoffDate})", 
            items.Count, itemType, maxRetentionDays, cutoffDate.ToString("yyyy-MM-dd HH:mm:ss"));
        
        try
        {
            foreach (var item in items)
            {
                string deletionReason = "";

                // Validate NetworkId exists in NetworkMap configuration
                if (!ValidateNetworkId(item))
                {
                    deletionReason = $"NetworkId {item.NetworkId} not found in NetworkMap configuration";
                }
                // Check if the item's CreatedDate is older than the cutoff date
                // Treat null CreatedDate as very old (past cutoff date)
                else if (item.CreatedDate == null || item.CreatedDate.Value < cutoffDate)
                {
                    deletionReason = $"Created {item.CreatedDate?.ToString("yyyy-MM-dd HH:mm:ss") ?? "N/A"} is older than cutoff {cutoffDate:yyyy-MM-dd HH:mm:ss}";
                }

                if (!string.IsNullOrEmpty(deletionReason))
                {
                    var itemDirectory = _metadataService.GetJellyBridgeItemDirectory(item);
                    
                    if (Directory.Exists(itemDirectory))
                    {
                        Directory.Delete(itemDirectory, true);
                        deletedItems.Add(item);
                        _logger.LogTrace("✅ Removed {ItemType} '{ItemName}' - {Reason}", 
                            itemType, item.MediaName, deletionReason);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing {ItemType}", itemType);
        }
        
        return deletedItems;
    }

    /// <summary>
    /// Recursively deletes all .ignore files from the Jellyseerr bridge directory.
    /// </summary>
    public Task<int> DeleteAllIgnoreFilesAsync()
    {
        var syncDirectory = Plugin.GetConfigOrDefault<string>(nameof(PluginConfiguration.LibraryDirectory));
        
        try
        {
            if (string.IsNullOrEmpty(syncDirectory) || !Directory.Exists(syncDirectory))
            {
                _logger.LogWarning("Sync directory not configured or does not exist: {SyncDirectory}", syncDirectory);
                return Task.FromResult(0);
            }

            _logger.LogTrace("Starting recursive deletion of .ignore files from: {SyncDirectory}", syncDirectory);
            
            var deletedCount = 0;
            var ignoreFiles = Directory.GetFiles(syncDirectory, BridgeService.IgnoreFileName, SearchOption.AllDirectories);
            
            foreach (var ignoreFile in ignoreFiles)
            {
                try
                {
                    File.Delete(ignoreFile);
                    deletedCount++;
                    _logger.LogDebug("Deleted .ignore file: {IgnoreFile}", ignoreFile);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to delete .ignore file: {IgnoreFile}", ignoreFile);
                }
            }

            _logger.LogTrace("Completed deletion of .ignore files. Deleted {DeletedCount} files out of {TotalCount} found", 
                deletedCount, ignoreFiles.Length);
            
            return Task.FromResult(deletedCount);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during .ignore file deletion");
            return Task.FromResult(0);
        }
    }

    /// <summary>
    /// Filters out Jellyfin matches that are already inside the JellyBridge sync directory.
    /// This prevents re-processing items that were created by the plugin itself.
    /// </summary>
    public (List<JellyMatch> filtered, List<IJellyseerrItem> synced) FilterSyncedItems(List<JellyMatch> matches)
    {
        if (matches == null || matches.Count == 0)
        {
            return (new List<JellyMatch>(), new List<IJellyseerrItem>());
        }

        var filtered = new List<JellyMatch>();
        var synced = new List<IJellyseerrItem>();
        foreach (var match in matches)
        {
            var path = match?.JellyfinItem?.Path;
            // Keep the match only if it's not in the sync directory
            if (match != null)
            {
                if (string.IsNullOrEmpty(path) || !FolderUtils.IsPathInSyncDirectory(path))
                {
                    filtered.Add(match);
                } else {
                    synced.Add(match.JellyseerrItem);
                }
            }
        }

        _logger.LogTrace("FilterSyncedItems: filtered={Filtered}, synced={Synced}, total={Total}", filtered.Count, synced.Count, matches.Count);
        return (filtered, synced);
    }

    /// <summary>
    /// Filters Jellyseerr items that have an ignore file in their target directory.
    /// Returns only items that do NOT have the ignore file present.
    /// </summary>
    public List<IJellyseerrItem> FilterIgnoredItems(List<IJellyseerrItem> items)
    {
        if (items == null || items.Count == 0)
        {
            return new List<IJellyseerrItem>();
        }

        var kept = new List<IJellyseerrItem>(items.Count);
        foreach (var item in items)
        {
            try
            {
                var dir = _metadataService.GetJellyBridgeItemDirectory(item);
                var ignorePath = Path.Combine(dir, BridgeService.IgnoreFileName);
                if (!File.Exists(ignorePath))
                {
                    kept.Add(item);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "FilterIgnoredItems failed for {Name}", item?.MediaName);
            }
        }

        _logger.LogTrace("FilterIgnoredItems kept {Kept}/{Total}", kept.Count, items.Count);
        return kept;
    }

    #endregion

    #endregion
}