using Jellyfin.Plugin.JellyBridge.BridgeModels;
using Jellyfin.Plugin.JellyBridge.JellyseerrModel;
using Jellyfin.Plugin.JellyBridge.JellyfinModels;
using Jellyfin.Plugin.JellyBridge.Utils;
using Jellyfin.Plugin.JellyBridge.Configuration;
using Microsoft.Extensions.Logging;
using MediaBrowser.Controller.Dto;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System;
using System.Text;

namespace Jellyfin.Plugin.JellyBridge.Services;

/// <summary>
/// Service for handling mixed elements from Jellyfin and Jellyseerr.
/// </summary>
public class BridgeService
{
    private readonly DebugLogger<BridgeService> _logger;
    private readonly JellyfinILibraryManager _libraryManager;
    private readonly IDtoService _dtoService;
    private readonly MetadataService _metadataService;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _fileSemaphores = new();

    public readonly static string IgnoreFileName = ".ignore";
    public readonly static string IgnorePattern = "**/*";

    public BridgeService(ILogger<BridgeService> logger, JellyfinILibraryManager libraryManager, IDtoService dtoService, MetadataService metadataService)
    {
        _logger = new DebugLogger<BridgeService>(logger);
        _libraryManager = libraryManager;
        _dtoService = dtoService;
        _metadataService = metadataService;
    }

    /// <summary>
    /// Overload: Scan providing a flat list of Jellyseerr items. Fetch Jellyfin items from the library.
    /// Unmatched items returns all Jellyseerr items specific to the Jellymatch Library directory.
    /// </summary>
    public async Task<(List<JellyMatch> matched, List<IJellyseerrItem> unmatched)> LibraryScanAsync(List<IJellyseerrItem> jellyseerrItems)
    {
        var existingMovies = _libraryManager.GetExistingItems<JellyfinMovie>();
        var existingShows = _libraryManager.GetExistingItems<JellyfinSeries>();
        var jellyfinItems = new List<IJellyfinItem>();
        jellyfinItems.AddRange(existingMovies);
        jellyfinItems.AddRange(existingShows);
        var matches = await LibraryScanAsync(jellyfinItems, jellyseerrItems);
        var libraryMatchedItems = matches.Select(m => m.JellyseerrItem).ToList();
        var unmatched = GetNonMatchingJellyseerrItems(libraryMatchedItems, jellyseerrItems);
        return (matches, unmatched);
    }

    /// <summary>
    /// Overload: Scan providing a flat list of Jellyfin items. Fetch Jellyseerr metadata via ReadMetadataAsync.
    /// Unmatched items returns all Jellyseerr items regardless of Library directory.
    /// </summary>
    public async Task<(List<JellyMatch> matched, List<IJellyfinItem> unmatched)> LibraryScanAsync(List<IJellyfinItem> jellyfinItems)
    {
        var (moviesMeta, showsMeta) = await _metadataService.ReadMetadataAsync();
        var jellyseerrItems = new List<IJellyseerrItem>();
        jellyseerrItems.AddRange(moviesMeta.Cast<IJellyseerrItem>());
        jellyseerrItems.AddRange(showsMeta.Cast<IJellyseerrItem>());
        var matches = await LibraryScanAsync(jellyfinItems, jellyseerrItems);
        var matchedJfIds = matches.Select(m => m.JellyfinItem.Id).ToHashSet();
        var unmatchedJellyfin = jellyfinItems.Where(jf => !matchedJfIds.Contains(jf.Id)).ToList();
        return (matches, unmatchedJellyfin);
    }

    /// <summary>
    /// Core scan: compare provided Jellyfin items against provided Jellyseerr metadata and return matches.
    /// </summary>
    private Task<List<JellyMatch>> LibraryScanAsync(List<IJellyfinItem> jellyfinItems, List<IJellyseerrItem> jellyseerrItems)
    {
        _logger.LogDebug("Running library scan for {ItemCount} Jellyseerr items against {JfCount} Jellyfin items", jellyseerrItems.Count, jellyfinItems.Count);

        try
        {
            // Split Jellyseerr items into movies and shows for existing matcher
            var jellyseerrMovies = jellyseerrItems.OfType<JellyseerrMovie>().ToList();
            var jellyseerrShows = jellyseerrItems.OfType<JellyseerrShow>().ToList();

            // Partition Jellyfin items
            var jellyfinMovies = jellyfinItems.OfType<JellyfinMovie>().ToList();
            var jellyfinShows = jellyfinItems.OfType<JellyfinSeries>().ToList();

            // Find matches
            var movieMatches = FindMatches(jellyfinMovies, jellyseerrMovies);
            var showMatches = FindMatches(jellyfinShows, jellyseerrShows);

            var allMatches = new List<JellyMatch>();
            allMatches.AddRange(movieMatches);
            allMatches.AddRange(showMatches);

            _logger.LogDebug("Library scan completed. Matches: {MatchCount}", allMatches.Count);
            return Task.FromResult(allMatches);
        }
        catch (MissingMethodException ex)
        {
            _logger.LogDebug(ex, "Using incompatible Jellyfin version. Skipping library scan");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during library scan");
        }

        return Task.FromResult(new List<JellyMatch>());
    }

    /// <summary>
    /// Find matches between existing Jellyfin items and bridge metadata.
    /// </summary>
    private List<JellyMatch> FindMatches<TJellyfin, TJellyseerr>(
        List<TJellyfin> jellyfinItems,
        List<TJellyseerr> jellyseerrItems)
        where TJellyfin : IJellyfinItem
        where TJellyseerr : TmdbMediaResult, IJellyseerrItem
    {
        var matches = new List<JellyMatch>();

        foreach (var jellyfinItem in jellyfinItems)
        {
            // Finding all items in case we are using network folders and add duplicate content.
            var matchingJellyseerrItems = jellyseerrItems.Where(bm => bm.EqualsItem(jellyfinItem)).ToList();
            if (matchingJellyseerrItems.Count > 0)
            {
                foreach (var jellyseerrItem in matchingJellyseerrItems)
                {
                    _logger.LogTrace("Found match: '{JellyfinItemName}' (Id: {JellyfinItemId}) matches '{JellyseerrItemName}' (Id: {JellyseerrItemId})",
                        jellyseerrItem.MediaName, jellyseerrItem.Id, jellyfinItem.Name, jellyfinItem.Id);
                    matches.Add(new JellyMatch(jellyseerrItem, jellyfinItem));
                }
            }
        }

        _logger.LogDebug("Found {MatchCount} matches between Jellyfin items and bridge metadata", matches.Count);
        return matches;
    }

    /// <summary>
    /// Creates an ignore file with the specified JSON content.
    /// Uses a semaphore indexed by filename to prevent concurrent writes to the same file.
    /// </summary>
    /// <param name="ignoreFilePath">The full path to the ignore file</param>
    /// <param name="fileContent">The JSON content to write to the file</param>
    /// <returns>Task that completes when the file is written</returns>
    public async Task CreateIgnoreFileAsync(string ignoreFilePath, string? fileContent)
    {
        var semaphore = _fileSemaphores.GetOrAdd(ignoreFilePath, _ => new SemaphoreSlim(1, 1));
        await semaphore.WaitAsync(60 * 1000); // Wait up to 60 seconds to acquire the semaphore
        try
        {
            // Comment out each line of the JSON content
            if (string.IsNullOrEmpty(fileContent))
            {
                await File.WriteAllTextAsync(ignoreFilePath, IgnorePattern);
                return;
            }

            var lines = fileContent.Split(Environment.NewLine);

            var commentPrefix = "#";

            // Calculate the expected length of the new string
            var sb = new StringBuilder(fileContent.Length + lines.Length * (commentPrefix.Length + Environment.NewLine.Length) + IgnorePattern.Length + Environment.NewLine.Length);

            foreach (var line in lines)
            {
                sb.Append(commentPrefix);
                sb.AppendLine(line);
            }

            sb.AppendLine(IgnorePattern);

            await File.WriteAllTextAsync(ignoreFilePath, sb.ToString());
        }
        finally
        {
            semaphore.Release();
        }
    }

    /// <summary>
    /// Create ignore files for matched items.
    /// Returns a tuple of (newly ignored items, existing ignored items).
    /// </summary>
    public async Task<(List<IJellyseerrItem> newIgnored, List<IJellyseerrItem> existingIgnored)> IgnoreMatchAsync(List<JellyMatch> matches)
    {
        var newIgnored = new List<IJellyseerrItem>();
        var existingIgnored = new List<IJellyseerrItem>();
        var ignoreFileTasks = new List<Task>();

        foreach (var match in matches)
        {
            var bridgeFolderPath = _metadataService.GetJellyBridgeItemDirectory(match.JellyseerrItem);
            var item = match.JellyfinItem;
            var ignoreFilePath = Path.Combine(bridgeFolderPath, IgnoreFileName);
            try
            {
                if (File.Exists(ignoreFilePath))
                {
                    existingIgnored.Add(match.JellyseerrItem);
                    _logger.LogTrace("Ignore file already exists for {ItemName} (Id: {ItemId}) at {IgnoreFilePath}",
                        item.Name, item.Id, ignoreFilePath);
                }
                else
                {
                    try
                    {
                        _logger.LogTrace("Creating ignore file for {ItemName} (Id: {ItemId}) at {IgnoreFilePath}",
                            item.Name, item.Id, ignoreFilePath);
                        var itemJson = item.ToJson(_dtoService);
                        _logger.LogTrace("Successfully serialized {ItemName} to JSON - JSON length: {JsonLength} characters",
                            item.Name, itemJson?.Length ?? 0);
                        ignoreFileTasks.Add(CreateIgnoreFileAsync(ignoreFilePath, itemJson));
                        newIgnored.Add(match.JellyseerrItem);
                        _logger.LogTrace("Created ignore file for {ItemName} in {BridgeFolder}", item.Name, bridgeFolderPath);
                    }
                    catch (MissingMethodException ex)
                    {
                        _logger.LogWarning(ex, "Using incompatible Jellyfin version. Writing empty ignore file for {ItemName}", item.Name);
                        await CreateIgnoreFileAsync(ignoreFilePath, "");
                        newIgnored.Add(match.JellyseerrItem);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating ignore file for {ItemName}", item.Name);
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
        return (newIgnored, existingIgnored);
    }

    /// <summary>
    /// Gets all Jellyfin libraries that contain JellyBridge folders (locations within the sync directory).
    /// Returns a dictionary mapping library names to their normalized location paths.
    /// </summary>
    /// <returns>Dictionary mapping library names to HashSet of normalized location paths</returns>
    private Dictionary<string, HashSet<string>> GetBridgeLibraries()
    {
        var result = new Dictionary<string, HashSet<string>>();
        var libraries = _libraryManager.Inner.GetVirtualFolders();
        var bridgeLibraries = libraries.Where(lib =>
            lib.Locations?.Any(location => FolderUtils.IsPathInSyncDirectory(location)) == true).ToList();

        foreach (var library in bridgeLibraries)
        {
            result[library.Name] = library.Locations.ToList().ToHashSet();
        }

        return result;
    }

    /// <summary>
    /// Reads metadata items from JellyBridge libraries.
    /// Returns a list of BridgeLibrary objects, each containing all items for that library.
    /// </summary>
    /// <returns>List of BridgeLibrary objects, including an empty library for unmatched items</returns>
    public async Task<List<BridgeLibrary>> ReadMetadataLibraries()
    {
        try
        {
            // 1. Get all JellyBridge libraries with their locations
            var bridgeLibraries = GetBridgeLibraries();
            if (!bridgeLibraries.Any())
            {
                _logger.LogDebug("No JellyBridge libraries found");
                return new List<BridgeLibrary>();
            }

            // 2. Create BridgeLibrary objects with their locations
            var libraryMap = new Dictionary<string, BridgeLibrary>();
            foreach (var kvp in bridgeLibraries)
            {
                libraryMap[kvp.Key] = new BridgeLibrary(kvp.Key, kvp.Value);
            }

            // 3. Collect all directories
            var allMovieDirs = new List<string>();
            var allShowDirs = new List<string>();

            foreach (var libraryEntry in bridgeLibraries)
            {
                foreach (var libraryLocation in libraryEntry.Value)
                {
                    try
                    {
                        var (movieDirs, showDirs) = _metadataService.ReadMetadataFolders(libraryLocation);
                        allMovieDirs.AddRange(movieDirs);
                        allShowDirs.AddRange(showDirs);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Error reading metadata folders from library location: {Location}", libraryLocation);
                    }
                }
            }

            if (allMovieDirs.Count == 0 && allShowDirs.Count == 0)
            {
                _logger.LogDebug("No metadata directories found in any JellyBridge library");
                return libraryMap.Values.ToList();
            }

            _logger.LogDebug("Found {MovieCount} movie directories and {ShowCount} show directories across all libraries",
                allMovieDirs.Count, allShowDirs.Count);

            // 4. Read metadata items from all directories
            var (movies, shows) = await _metadataService.ReadMetadataAsync(allMovieDirs, allShowDirs);

            // 5. Map items to libraries
            var unmatchedItems = new List<IJellyseerrItem>();

            foreach (var item in movies.Cast<IJellyseerrItem>().Concat(shows))
            {
                try
                {
                    var expectedDirectory = _metadataService.GetJellyBridgeItemDirectory(item);
                    var normalizedExpected = FolderUtils.GetNormalizedPath(expectedDirectory);

                    if (string.IsNullOrEmpty(normalizedExpected))
                    {
                        _logger.LogWarning("Could not normalize directory for item {MediaName} (Id: {Id}): {ExpectedDirectory}",
                            item.MediaName, item.Id, expectedDirectory);
                        continue;
                    }

                    // Find which library contains this directory
                    var matchedLibrary = libraryMap.Values.FirstOrDefault(lib => lib.ContainsLocation(normalizedExpected));

                    if (matchedLibrary != null)
                    {
                        matchedLibrary.Add(item);
                    }
                    else if (FolderUtils.IsPathInSyncDirectory(expectedDirectory))
                    {
                        unmatchedItems.Add(item);
                        _logger.LogTrace("Item {MediaName} (Id: {Id}) is in JellyBridge directory but not mapped to a library; will be added to empty library.",
                            item.MediaName, item.Id);
                    }
                    else
                    {
                        _logger.LogWarning("Could not find matching directory/library for item {MediaName} (Id: {Id}). Expected: {ExpectedDirectory}",
                            item.MediaName, item.Id, expectedDirectory);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error getting directory/library for item {MediaName} (Id: {Id})",
                        item.MediaName, item.Id);
                }
            }

            // 6. Create the empty library for unmatched items
            if (unmatchedItems.Count > 0)
            {
                var emptyLibrary = new BridgeLibrary(string.Empty);
                emptyLibrary.AddRange(unmatchedItems);
                libraryMap[string.Empty] = emptyLibrary;
                _logger.LogTrace("Created empty library with {Count} unmatched items", unmatchedItems.Count);
            }

            // 7. Convert to list and log results
            var results = libraryMap.Values.ToList();
            _logger.LogDebug("Read {LibraryCount} libraries with {ItemCount} total items",
                results.Count, results.Sum(l => l.Count));

            foreach (var library in results)
            {
                _logger.LogTrace("Library: {LibraryName} - {MovieCount} movies, {ShowCount} shows at {LocationCount} location(s)",
                    string.IsNullOrEmpty(library.LibraryName) ? "(Empty)" : library.LibraryName,
                    library.MovieCount, library.ShowCount, library.Locations.Count);
            }

            return results;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error reading metadata libraries");
            return new List<BridgeLibrary>();
        }
    }

    /// <summary>
    /// Maps items to libraries by directory.
    /// Filters out items that already exist in their target library or are duplicates in the incoming list.
    /// </summary>
    /// <param name="items">List of Jellyseerr items to map (from Jellyseerr API)</param>
    /// <returns>List of unique Jellyseerr items to sync</returns>
    public async Task<List<IJellyseerrItem>> FilterDuplicatesByLibrary(List<IJellyseerrItem> items)
    {
        // If both UseNetworkFolders and AddDuplicateContent are enabled, skip filtering
        var useNetworkFolders = Plugin.GetConfigOrDefault<bool>(nameof(PluginConfiguration.UseNetworkFolders));
        var addDuplicateContent = Plugin.GetConfigOrDefault<bool>(nameof(PluginConfiguration.AddDuplicateContent));
        
        if (items == null || items.Count == 0)
        {
            return new List<IJellyseerrItem>();
        }

        try
        {
            // 1. Read existing metadata from JellyBridge libraries (this is the source of truth)
            var existingLibraries = await ReadMetadataLibraries();
            if (!existingLibraries.Any())
            {
                _logger.LogWarning("No JellyBridge libraries found");
                return new List<IJellyseerrItem>();
            }

            _logger.LogTrace("Built location-to-library lookup with {Count} entries from {LibraryCount} libraries", 
                existingLibraries.SelectMany(lib => lib.Locations).Count(), existingLibraries.Count);

            // 3. Build a dictionary: library name -> set of existing item hashes in that library
            // Use the BridgeLibrary's built-in ToDeduplicationSet() method
            var libraryItemHashes = new Dictionary<string, HashSet<(int ItemHash, int FolderHash)>>();
            
            foreach (var library in existingLibraries)
            {
                var libraryName = library.LibraryName ?? string.Empty;
                
                // Get all hashes from this library using the built-in method
                // Filter ignored items first, then get hashes
                var filteredItems = FilterIgnoredItems(library.Items);
                var hashes = filteredItems
                    .Select(item => (
                        ItemHash: item.GetItemHashCode(network: useNetworkFolders && addDuplicateContent),
                        FolderHash: item.GetFolderHashCode(network: useNetworkFolders && addDuplicateContent)
                    ))
                    .ToHashSet();
                libraryItemHashes[libraryName] = hashes;
                
                _logger.LogTrace("Library '{LibraryName}' has {Count} existing items (ignored items filtered)",
                    string.IsNullOrEmpty(libraryName) ? "(Empty)" : libraryName,
                    hashes.Count);
            }

            var totalExisting = libraryItemHashes.Values.Sum(h => h.Count);
            _logger.LogTrace("Loaded {TotalCount} existing items from metadata across {LibraryCount} libraries (ignored items filtered)",
                totalExisting, libraryItemHashes.Count);

            // Store all mapped items from the libraries
            var mappedItems = new List<IJellyseerrItem>();
            // Track duplicates within the incoming list
            var seenAnywhere = new HashSet<int>();

            foreach (var library in existingLibraries)
            {
                // 4. Process each input item
                var seenInLibrary = new HashSet<int>(); // Track duplicates within the incoming list

                foreach (var item in items)
                {
                    var directory = _metadataService.GetJellyBridgeItemDirectory(item);
                    if (string.IsNullOrEmpty(directory) || !FolderUtils.IsPathInSyncDirectory(directory))
                    {
                        _logger.LogTrace("Item {MediaName} (Id: {Id}) is not in a JellyBridge directory; skipping", item.MediaName, item.Id);
                        continue;
                    }

                    var isInLibrary = library.ContainsLocation(FolderUtils.GetNormalizedPath(directory));
                    if (!isInLibrary)
                    {
                        _logger.LogTrace("Item {MediaName} (Id: {Id}) is in a JellyBridge directory but not in library '{LibraryName}'; skipping",
                            item.MediaName, item.Id, string.IsNullOrEmpty(library.LibraryName) ? "(Empty)" : library.LibraryName);
                        continue;
                    }

                    var folderHash = item.GetFolderHashCode(network: useNetworkFolders && addDuplicateContent);
                    // Check folder hash to determine if the item is already in this exact location in the library
                    if (libraryItemHashes.TryGetValue(library.LibraryName, out var existingFolders) &&
                        existingFolders.Any(tuple => tuple.FolderHash == folderHash))
                    {
                        seenAnywhere.Add(folderHash);
                        mappedItems.Add(item);
                        _logger.LogTrace("Adding existing item to library '{Library}': {MediaName} (FolderHash: {FolderHash})",
                            library.LibraryName, item.MediaName, folderHash);
                    }

                    // Look for new items or duplicate items with different folder names
                    var itemHash = item.GetItemHashCode(network: useNetworkFolders && addDuplicateContent);
                    if(useNetworkFolders && addDuplicateContent && !seenInLibrary.Add(itemHash))
                    {
                        _logger.LogTrace("Filtered duplicate from input list (within library '{LibraryName}'): {MediaName} (ItemHash: {ItemHash})",
                            string.IsNullOrEmpty(library.LibraryName) ? "(Empty)" : library.LibraryName, item.MediaName, itemHash);
                        continue;
                    }
                    else if(!(useNetworkFolders && addDuplicateContent) && !seenAnywhere.Add(itemHash))
                    {
                        _logger.LogTrace("Filtered duplicate from input list (across libraries): {MediaName} (ItemHash: {ItemHash})",
                            item.MediaName, itemHash);
                        continue;
                    }
                    
                    // Ensure we capture those items taht are not in a library
                    seenAnywhere.Add(itemHash);

                    // Check for duplicates within the current library
                    if (libraryItemHashes.TryGetValue(library.LibraryName, out var existingItems) &&
                        existingItems.Any(tuple => tuple.ItemHash == itemHash))
                    {
                        _logger.LogTrace("Filtered duplicate from input list: {MediaName} (ItemHash: {ItemHash})",
                            item.MediaName, itemHash);
                        continue;
                    }

                    // Item is unique and doesn't exist in its target library - add it
                    mappedItems.Add(item);
                    _logger.LogTrace("Adding new item to library '{Library}': {MediaName} (ItemHash: {ItemHash})",
                        library.LibraryName, item.MediaName, itemHash);
                }
            }
            
            // Flatten the library list to search all hashes
            var (allItemHashes, allFolderHashes) = (
                libraryItemHashes.SelectMany(kvp => kvp.Value.Select(t => t.ItemHash)).ToHashSet(),
                libraryItemHashes.SelectMany(kvp => kvp.Value.Select(t => t.FolderHash)).ToHashSet()
            );
            // Catch-all for items that did not appear in ANY library or existing items
            foreach (var item in items)
            {
                var itemHash = item.GetItemHashCode(network: useNetworkFolders && addDuplicateContent);
                var folderHash = item.GetFolderHashCode(network: useNetworkFolders && addDuplicateContent);
                if (seenAnywhere.Contains(itemHash) || seenAnywhere.Contains(folderHash))
                {
                    continue; // Already processed in the previous loop
                }
                if(allFolderHashes.Contains(folderHash))
                {
                    seenAnywhere.Add(folderHash);
                    mappedItems.Add(item);
                    _logger.LogTrace("Adding item not found in any library  {MediaName} (FolderHash: {folderHash})", item.MediaName, folderHash);
                }
                if(!allItemHashes.Contains(itemHash))
                {
                    seenAnywhere.Add(itemHash);
                    mappedItems.Add(item);
                    _logger.LogTrace("Adding item not found in any library: {MediaName} (ItemHash: {ItemHash})", item.MediaName, itemHash);
                }
            }

            _logger.LogDebug("Processed {Total} items - filtered {InputDuplicates} duplicates -> {Mapped} new items to sync",
                items.Count - mappedItems.Count,
                items.Count,
                mappedItems.Count);

            return mappedItems;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error mapping items to libraries");
            return new List<IJellyseerrItem>();
        }
    }

    /// <summary>
    /// Checks if an item is ignored (has .ignore file in its directory).
    /// </summary>
    private bool IsItemIgnored(IJellyseerrItem item)
    {
        try
        {
            var dir = _metadataService.GetJellyBridgeItemDirectory(item);
            var ignorePath = Path.Combine(dir, BridgeService.IgnoreFileName);
            return File.Exists(ignorePath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not check ignore status for {MediaName}", item?.MediaName);
            return false;
        }
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
            if (!IsItemIgnored(item))
            {
                kept.Add(item);
            }
        }

        _logger.LogTrace("Filtered out ignored items: kept {Kept}/{Total}", kept.Count, items.Count);
        return kept;
    }

    /// <summary>
    /// Compare two Jellyseerr item lists for equivalence based on configuration.
    /// When UseNetworkFolders is enabled, compares by composite key (Id + normalized directory).
    /// Otherwise, compares by Id only.
    /// The first list represents library matches; the second represents test items.
    /// </summary>
    /// <param name="libraryMatches">Items discovered from library/scan context</param>
    /// <param name="testItems">Items to compare against libraryMatches</param>
    /// <returns>True if sets are equivalent under the configured comparison mode</returns>
    private List<IJellyseerrItem> GetNonMatchingJellyseerrItems(List<IJellyseerrItem> libraryMatches, List<IJellyseerrItem> testItems)
    {
        // If both UseNetworkFolders and AddDuplicateContent are enabled, skip filtering
        var useNetworkFolders = Plugin.GetConfigOrDefault<bool>(nameof(PluginConfiguration.UseNetworkFolders));
        var addDuplicateContent = Plugin.GetConfigOrDefault<bool>(nameof(PluginConfiguration.AddDuplicateContent));

        var unmatched = new List<IJellyseerrItem>();

        var libHashCodes = new HashSet<int>(libraryMatches.Select(i => i.GetItemHashCode(network: useNetworkFolders && addDuplicateContent)));
        unmatched = testItems.Where(t => !libHashCodes.Contains(t.GetItemHashCode(network: useNetworkFolders && addDuplicateContent))).ToList();
        _logger.LogDebug("{UnmatchedCount} unmatched items", unmatched.Count);
        return unmatched;
    }
}