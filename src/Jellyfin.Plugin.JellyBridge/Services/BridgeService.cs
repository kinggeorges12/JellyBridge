using Jellyfin.Plugin.JellyBridge.BridgeModels;
using Jellyfin.Plugin.JellyBridge.JellyseerrModel;
using Jellyfin.Plugin.JellyBridge.JellyfinModels;
using Jellyfin.Plugin.JellyBridge.Utils;
using Jellyfin.Plugin.JellyBridge.Configuration;
using Microsoft.Extensions.Logging;
using MediaBrowser.Controller.Dto;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System;
 

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

    public readonly static string IgnoreFileName = ".ignore";

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
    /// Create ignore files for matched items.
    /// </summary>
    public async Task CreateIgnoreFilesAsync(List<JellyMatch> matches)
    {
        var ignoreFileTasks = new List<Task>();

        foreach (var match in matches)
        {
            var bridgeFolderPath = _metadataService.GetJellyBridgeItemDirectory(match.JellyseerrItem);
            var item = match.JellyfinItem;
            var ignoreFilePath = Path.Combine(bridgeFolderPath, IgnoreFileName);
            try
            {
                _logger.LogTrace("Creating ignore file for {ItemName} (Id: {ItemId}) at {IgnoreFilePath}", 
                    item.Name, item.Id, ignoreFilePath);
                var itemJson = item.ToJson(_dtoService);
                _logger.LogTrace("Successfully serialized {ItemName} to JSON - JSON length: {JsonLength} characters", 
                    item.Name, itemJson?.Length ?? 0);
                ignoreFileTasks.Add(File.WriteAllTextAsync(ignoreFilePath, itemJson));
                _logger.LogTrace("Created ignore file for {ItemName} in {BridgeFolder}", item.Name, bridgeFolderPath);
            }
            catch (MissingMethodException ex)
            {
                _logger.LogDebug(ex, "Using incompatible Jellyfin version. Writing empty ignore file for {ItemName}", item.Name);
                await File.WriteAllTextAsync(ignoreFilePath, "");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating ignore file for {ItemName}", item.Name);
            }
        }

        await Task.WhenAll(ignoreFileTasks);
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
            if (library.Locations == null || !library.Locations.Any())
            {
                continue;
            }

            var normalizedLocations = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var location in library.Locations)
            {
                try
                {
                    normalizedLocations.Add(Path.GetFullPath(location));
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error normalizing library location {Location} for library {LibraryName}", 
                        location, library.Name);
                }
            }

            if (normalizedLocations.Any())
            {
                result[library.Name] = normalizedLocations;
            }
        }
        
        return result;
    }

    /// <summary>
    /// Reads metadata items from JellyBridge libraries.
    /// Calls ReadMetadataFolders for each library location and then ReadMetadataAsync to get the items.
    /// Returns items with their library name, directory, and the item itself.
    /// </summary>
    /// <returns>List of tuples containing library name, directory path, and metadata item</returns>
    public async Task<List<(string libraryName, string directory, IJellyseerrItem item)>> ReadMetadataLibraries()
    {
        var allMetadataItems = new List<(string libraryName, string directory, IJellyseerrItem item)>();
        // Map normalized directory paths directly to library names
        var directoryLibraryMap = new Dictionary<string, (string libraryName, string originalDirectory)>(StringComparer.OrdinalIgnoreCase);

        try
        {
            // Get all JellyBridge libraries with normalized locations
            var bridgeLibraryLocations = GetBridgeLibraries();
            
            var allMovieDirs = new List<string>();
            var allShowDirs = new List<string>();
            
            // Read metadata folders for each library location and track which library each directory belongs to
            foreach (var libraryEntry in bridgeLibraryLocations)
            {
                var libraryName = libraryEntry.Key;
                
                foreach (var libraryLocation in libraryEntry.Value)
                {
                    try
                    {
                        var (movieDirs, showDirs) = _metadataService.ReadMetadataFolders(libraryLocation);
                        
                        // Track which library each directory belongs to (normalize paths for consistent lookup)
                        var allDirs = new List<string>();
                        allDirs.AddRange(movieDirs);
                        allDirs.AddRange(showDirs);
                        foreach (var dir in allDirs)
                        {
                            try
                            {
                                var normalized = Path.GetFullPath(dir);
                                directoryLibraryMap[normalized] = (libraryName, dir);
                            }
                            catch (Exception ex)
                            {
                                _logger.LogWarning(ex, "Error normalizing directory path: {Directory}. Skipping this directory.", dir);
                            }
                        }
                        
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
                return allMetadataItems;
            }

            _logger.LogDebug("Found {MovieCount} movie directories and {ShowCount} show directories across all libraries", 
                allMovieDirs.Count, allShowDirs.Count);

            // Read metadata items using MetadataService
            var (movies, shows) = await _metadataService.ReadMetadataAsync(allMovieDirs, allShowDirs);
            
            // Combine movies and shows into a single list of IJellyseerrItem
            var allItems = new List<IJellyseerrItem>();
            allItems.AddRange(movies);
            allItems.AddRange(shows);
            
            // Map items to their directories and library names
            foreach (var item in allItems)
            {
                try
                {
                    var expectedDirectory = _metadataService.GetJellyBridgeItemDirectory(item);
                    var normalizedExpected = Path.GetFullPath(expectedDirectory);
                    
					if (directoryLibraryMap.TryGetValue(normalizedExpected, out var libraryInfo))
					{
						allMetadataItems.Add((libraryInfo.libraryName, libraryInfo.originalDirectory, item));
					}
					else
					{
						// If the item is inside the JellyBridge sync directory but not part of any library, return with empty library name
						if (FolderUtils.IsPathInSyncDirectory(expectedDirectory))
						{
							allMetadataItems.Add((string.Empty, expectedDirectory, item));
							_logger.LogTrace("Item {MediaName} (Id: {Id}) is in JellyBridge directory but not mapped to a library; assigning empty library name.", item.MediaName, item.Id);
						}
						else
						{
							_logger.LogWarning("Could not find matching directory/library for item {MediaName} (Id: {Id}). Expected: {ExpectedDirectory}", 
								item.MediaName, item.Id, expectedDirectory);
						}
					}
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error getting directory/library for item {MediaName} (Id: {Id})", item.MediaName, item.Id);
                }
            }

            _logger.LogDebug("Read {ItemCount} metadata items from libraries", allMetadataItems.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error reading metadata libraries");
        }

        return allMetadataItems;
    }

    /// <summary>
    /// Maps items to libraries by directory.
    /// Uses GetJellyBridgeItemDirectory to map items to directories, then maps directories to libraries.
    /// Filters out items that already exist in the same library (by comparing library name and item hash code).
    /// </summary>
    /// <param name="items">List of Jellyseerr items to map</param>
    /// <returns>List of tuples containing library name, directory, and item</returns>
    public async Task<List<(string libraryName, string directory, IJellyseerrItem item)>> FilterDuplicatesByLibrary(List<IJellyseerrItem> items)
    {
        var results = new List<(string libraryName, string directory, IJellyseerrItem item)>();
        
        if (items == null || items.Count == 0)
        {
            return results;
        }

        try
        {
            // Get all JellyBridge libraries with normalized locations
            var bridgeLibraryLocations = GetBridgeLibraries();

            if (!bridgeLibraryLocations.Any())
            {
                _logger.LogDebug("No JellyBridge libraries found, returning empty list");
                return results;
            }

            // Flatten library locations into (libraryName, location) pairs
            var libraryLocationPairs = bridgeLibraryLocations
                .SelectMany(libraryEntry => 
                    libraryEntry.Value.Select(location => (libraryName: libraryEntry.Key, location)))
                .ToList();

            // Map each item to its network folder (normalized), then join with library locations
            var itemNetworkPairs = items
                .Where(item => item != null && !string.IsNullOrEmpty(item.NetworkTag))
                .SelectMany(item =>
                {
                    try
                    {
                        var networkPath = _metadataService.GetNetworkFolder(item.NetworkTag);
                        if (networkPath != null)
                        {
                            networkPath = Path.GetFullPath(networkPath);
                            return new[] { (item: item, networkPath: networkPath) };
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Error getting network folder for item {ItemName}", item?.MediaName);
                    }
                    return Array.Empty<(IJellyseerrItem item, string networkPath)>();
                })
                .ToList();

            // Join items with library locations where network folder matches library location (exact match)
            // Both sides are already normalized
            var libraryItemPairs = itemNetworkPairs
                .Join(
                    libraryLocationPairs,
                    itemPath => itemPath.networkPath, // No longer nullable after Select
                    libraryPair => libraryPair.location, // Already normalized from GetBridgeLibraries
                    (itemPath, libraryPair) => 
                    {
                        // Get directory for the final result
                        var directory = _metadataService.GetJellyBridgeItemDirectory(itemPath.item);
                        return (libraryName: libraryPair.libraryName, directory: directory, item: itemPath.item);
                    },
                    StringComparer.OrdinalIgnoreCase)
                .ToList();

			// Get existing metadata items from libraries to filter out duplicates
			var existingMetadataItems = await ReadMetadataLibraries();
			
			// Create a HashSet of (libraryName, directory, itemHashCode) tuples for fast lookup
			// Use normalized uppercase strings to avoid case-sensitivity issues with default comparer
			var existingItemsSet = new HashSet<(string libraryName, string directory, int itemHashCode)>();
			foreach (var existingItem in existingMetadataItems)
			{
				try
				{
					var itemHashCode = existingItem.item.GetItemHashCode();
					var normalizedDir = Path.GetFullPath(existingItem.directory)?.ToUpperInvariant() ?? string.Empty;
					var normalizedLibrary = existingItem.libraryName?.ToUpperInvariant() ?? string.Empty;
					existingItemsSet.Add((normalizedLibrary, normalizedDir, itemHashCode));
				}
				catch (Exception ex)
				{
					_logger.LogWarning(ex, "Error preparing duplicate key for existing item {MediaName} in library {LibraryName}", 
						existingItem.item.MediaName, existingItem.libraryName);
				}
			}

            // Anti-join: filter out items that already exist in the same library
            // Items are considered duplicates if they have the same library name and the same item hash code
			var filteredLibraryItemPairs = libraryItemPairs
                .Where(pair =>
                {
                    try
                    {
                        // Add all items that have no library mapping
                        if (string.IsNullOrEmpty(pair.libraryName))
                        {
                            return true;
                        }
						var itemHashCode = pair.item.GetItemHashCode();
                        var normalizedDir = Path.GetFullPath(pair.directory)?.ToUpperInvariant() ?? string.Empty;
                        var normalizedLibrary = pair.libraryName?.ToUpperInvariant() ?? string.Empty;
						// Treat as duplicate only if an existing entry with the same library and hash exists in a DIFFERENT directory
						var existsSameDir = existingItemsSet.Contains((normalizedLibrary, normalizedDir, itemHashCode));
						var existsAnyDir = existingItemsSet.Any(t => t.libraryName == normalizedLibrary && t.itemHashCode == itemHashCode);
                        var shouldFilter = !existsSameDir && existsAnyDir;
						
						if (shouldFilter)
						{
							_logger.LogTrace("Filtering duplicate {MediaName} (Hash: {HashCode}) in library {LibraryName} from different directory. CurrentDir={Dir}", 
								pair.item.MediaName, itemHashCode, pair.libraryName, normalizedDir);
						}
						
						return !shouldFilter;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Error checking duplicate for item {MediaName} in library {LibraryName}", 
                            pair.item.MediaName, pair.libraryName);
                        // If we can't check, include it to be safe
                        return true;
                    }
                })
                .ToList();

			results.AddRange(filteredLibraryItemPairs);
			_logger.LogDebug("Appended {Count} items from libraries after filtering duplicates", filteredLibraryItemPairs.Count);

			// Second step: for items without a matching library location, map them to the default empty library
			var unmatchedAsDefault = itemNetworkPairs
				.Where(ip => !libraryLocationPairs.Any(lp => string.Equals(lp.location, ip.networkPath, StringComparison.OrdinalIgnoreCase)))
				.Select(ip => (
					libraryName: string.Empty,
					directory: _metadataService.GetJellyBridgeItemDirectory(ip.item),
					item: ip.item
				))
				.Where(t => !string.IsNullOrEmpty(t.directory) && FolderUtils.IsPathInSyncDirectory(t.directory))
				.ToList();

			if (unmatchedAsDefault.Count > 0)
			{
				results.AddRange(unmatchedAsDefault);
				_logger.LogDebug("Appended {Count} unmatched items to results with default (empty) library", unmatchedAsDefault.Count);
			} else {
				_logger.LogTrace("No unmatched items found to add to results with default (empty) library");
			}

            _logger.LogTrace("Mapped {TotalCount} items into {ResultCount} library-directory-item tuples after filtering {DuplicateCount} duplicates", 
                items.Count, results.Count, libraryItemPairs.Count - filteredLibraryItemPairs.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error mapping items to libraries, returning empty list");
        }
        return results;
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

        if (useNetworkFolders && addDuplicateContent)
        {
            var libHashCodes = new HashSet<int>(libraryMatches.Select(i => i.GetItemFolderHashCode()));
            unmatched = testItems.Where(t => !libHashCodes.Contains(t.GetItemFolderHashCode())).ToList();
        } else {
            var libIds = new HashSet<int>(libraryMatches.Select(i => i.Id));
            unmatched = testItems.Where(t => !libIds.Contains(t.Id)).ToList();
        }
        _logger.LogDebug("GetNonMatchingJellyseerrItems: {UnmatchedCount} unmatched items", unmatched.Count);
        return unmatched;
    }
}
