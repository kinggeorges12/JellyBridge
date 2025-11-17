using Jellyfin.Plugin.JellyBridge.BridgeModels;
using Jellyfin.Plugin.JellyBridge.JellyseerrModel;
using Jellyfin.Plugin.JellyBridge.JellyseerrModel.Server;
using Jellyfin.Plugin.JellyBridge.Utils;
using Jellyfin.Plugin.JellyBridge.Configuration;
using Microsoft.Extensions.Logging;
using System.IO;

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
    private readonly CleanupService _cleanupService;

    public DiscoverService(ILogger<DiscoverService> logger, PlaceholderVideoGenerator placeholderVideoGenerator, ApiService apiService, MetadataService metadataService, BridgeService bridgeService, CleanupService cleanupService)
    {
        _logger = new DebugLogger<DiscoverService>(logger);
        _placeholderVideoGenerator = placeholderVideoGenerator;
        _apiService = apiService;
        _metadataService = metadataService;
        _bridgeService = bridgeService;
        _cleanupService = cleanupService;
    }
    
    #region FromJellyseerr

    #region Process
    

    /// <summary>
    /// Fetches discover data for all networks, calling both movies and TV endpoints for each network.
    /// </summary>
    /// <returns>Tuple containing lists of movies and shows fetched from all networks</returns>
    public async Task<(List<JellyseerrMovie>, List<JellyseerrShow>)> FetchDiscoverMediaAsync()
    {
        var config = Plugin.GetConfiguration();
        var networkMap = config?.NetworkMap ?? new List<JellyseerrNetwork>();
        var allMovies = new List<JellyseerrMovie>();
        var allShows = new List<JellyseerrShow>();
        
        foreach (var network in networkMap)
        {
            _logger.LogTrace("Fetching discover content for network: {NetworkName} (ID: {NetworkId}, Country: {Country}, Priority: {DisplayPriority})", network.Name, network.Id, network.Country, network.DisplayPriority);
            
            // Add network Id and Country parameters to query parameters
            var networkParameters = new Dictionary<string, object> {
                ["watchRegion"] = network.Country,
                ["watchProviders"] = network.Id
            };

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
            
            // Add items to lists (no deduplication)
            foreach (var item in movies)
            {
                // Set the network tag for this item
                item.NetworkTag = network.Name;
                item.NetworkId = network.Id;
                allMovies.Add(item);
            }
            
            foreach (var item in shows)
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
        
        var seenHashes = new HashSet<int>();
        var uniqueItems = new List<IJellyseerrItem>();

        if (useNetworkFolders && addDuplicateContent)
        {
            _logger.LogDebug("Filtering duplicates by library: UseNetworkFolders and AddDuplicateContent are both enabled");
            var libraryResults = await _bridgeService.FilterDuplicatesByLibrary(items);
            // Extract items from library-directory-item tuples into a single flat list
            uniqueItems = libraryResults.Select(tuple => tuple.item).ToList();
            _logger.LogDebug("Extracted {ItemCount} items from {LibraryCount} library-directory-item tuples", 
                uniqueItems.Count, libraryResults.Count);
            return uniqueItems;
        }
        
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
        var seenFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        
        _logger.LogDebug("Processing {UnmatchedCount} unmatched items for placeholder creation", 
            unmatchedItems.Count);
        
        foreach (var item in unmatchedItems)
        {
            try
            {
                // Get the folder path for this item
                var folderPath = _metadataService.GetJellyBridgeItemDirectory(item);
                var normalizedFolder = string.IsNullOrWhiteSpace(folderPath) ? folderPath : Path.GetFullPath(folderPath);
                if (!string.IsNullOrEmpty(normalizedFolder) && !seenFolders.Add(normalizedFolder))
                {
                    _logger.LogTrace("Skipping duplicate placeholder for {ItemName} at {FolderPath}", item.MediaName, normalizedFolder);
                    continue;
                }
                
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

    /// <summary>
    /// Filters duplicate media items from a list using the GetItemFolderHashCode method.
    /// Returns a list containing only unique items based on their hash code.
    /// If UseNetworkFolders and AddDuplicateContent are both enabled, filters by library and excludes existing metadata items.
    /// </summary>
    /// <param name="allItems">List of media items to filter</param>
    /// <param name="uniqueItems">List of unique media items</param>
    /// <returns>Tuple of (newly ignored items, existing ignored items)</returns>
    public async Task<(List<IJellyseerrItem> NewlyIgnored, List<IJellyseerrItem> ExistingIgnored)> IgnoreDuplicateLibraryItems(List<IJellyseerrItem> allItems, List<IJellyseerrItem> uniqueItems)
    {
        var newlyIgnored = new List<IJellyseerrItem>();
        var existingIgnored = new List<IJellyseerrItem>();
        var useNetworkFolders = Plugin.GetConfigOrDefault<bool>(nameof(PluginConfiguration.UseNetworkFolders));
        var addDuplicateContent = Plugin.GetConfigOrDefault<bool>(nameof(PluginConfiguration.AddDuplicateContent));
        if (!(useNetworkFolders && addDuplicateContent))
        {
            _logger.LogDebug("Ignoring duplicate library items: UseNetworkFolders and AddDuplicateContent are both disabled");
            return (newlyIgnored, existingIgnored);
        }

        var uniqueFolderHashes = new HashSet<int>(uniqueItems.Select(item => item.GetItemFolderHashCode()));
        var duplicates = new List<IJellyseerrItem>();
        foreach (var item in allItems)
        {
            if (!uniqueFolderHashes.Contains(item.GetItemFolderHashCode()))
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
                    var itemJson = System.Text.Json.JsonSerializer.Serialize(duplicate, duplicate.GetType(), JellyBridgeJsonSerializer.DefaultOptions<object>());
                    ignoreFileTasks.Add(File.WriteAllTextAsync(ignoreFilePath, itemJson));
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
    
    #region Deleted

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
    public async Task<(List<JellyMatch> matched, List<IJellyseerrItem> unmatched)> FilterSyncedLibraryItems(List<JellyMatch> matchedItems, List<IJellyseerrItem> unmatchedItems)
    {
        var matched = new List<JellyMatch>();
        var unmatched = new List<IJellyseerrItem>();
        unmatched.AddRange(unmatchedItems);

        foreach (var match in matchedItems)
        {
            var path = match?.JellyfinItem?.Path;
            // Keep the match only if it's not in the sync directory
            if (match != null)
            {
                if (string.IsNullOrEmpty(path) || !FolderUtils.IsPathInSyncDirectory(path))
                {
                    matched.Add(match);
                } else {
                    unmatched.Add(match.JellyseerrItem);
                }
            }
        }

        // Apply unique-by-library filtering to unmatched via existing duplicate filter
        var filteredUnmatched = await FilterDuplicateMedia(unmatched);

        _logger.LogTrace("FilterSyncedLibraryItems: matched={Matched}, unmatched={Unmatched}, total={Total}", matched.Count, filteredUnmatched.Count, matchedItems.Count + unmatchedItems.Count);
        return (matched, filteredUnmatched);
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

        var networkMap = Plugin.GetConfigOrDefault<List<JellyseerrNetwork>>(nameof(PluginConfiguration.NetworkMap));
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
                            var itemJson = System.Text.Json.JsonSerializer.Serialize(item, item.GetType(), JellyBridgeJsonSerializer.DefaultOptions<object>());
                            ignoreFileTasks.Add(File.WriteAllTextAsync(ignoreFilePath, itemJson));
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
                            var itemJson = System.Text.Json.JsonSerializer.Serialize(item, item.GetType(), JellyBridgeJsonSerializer.DefaultOptions<object>());
                            ignoreFileTasks.Add(File.WriteAllTextAsync(ignoreFilePath, itemJson));
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