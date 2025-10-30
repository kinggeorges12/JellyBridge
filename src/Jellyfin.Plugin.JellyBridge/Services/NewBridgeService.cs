using Jellyfin.Plugin.JellyBridge.Configuration;
using Jellyfin.Plugin.JellyBridge.BridgeModels;
using Jellyfin.Plugin.JellyBridge.JellyseerrModel;
using Jellyfin.Plugin.JellyBridge.Utils;
using Jellyfin.Plugin.JellyBridge.JellyfinModels;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using MediaBrowser.Controller.Dto;
 

namespace Jellyfin.Plugin.JellyBridge.Services;

    /// <summary>
    /// Service for managing bridge folders and metadata (refactored version with only helper methods).
    /// </summary>
public class NewBridgeService
{
    private readonly DebugLogger<NewBridgeService> _logger;
    private readonly JellyfinILibraryManager _libraryManager;
    private readonly IDtoService _dtoService;
    private readonly MetadataService _metadataService;
    private readonly DiscoverService _discoverService;

    public NewBridgeService(ILogger<NewBridgeService> logger, JellyfinILibraryManager libraryManager, IDtoService dtoService, MetadataService metadataService, DiscoverService discoverService)
    {
        _logger = new DebugLogger<NewBridgeService>(logger);
        _libraryManager = libraryManager;
        _dtoService = dtoService;
        _metadataService = metadataService;
        _discoverService = discoverService;
    }

    

    /// <summary>
    /// Overload: Scan providing a flat list of Jellyseerr items. Fetch Jellyfin items from the library.
    /// </summary>
    public async Task<(List<JellyMatch> matched, List<IJellyseerrItem> unmatched)> LibraryScanAsync(List<IJellyseerrItem> jellyseerrItems)
    {
        var existingMovies = _libraryManager.GetExistingItems<JellyfinMovie>();
        var existingShows = _libraryManager.GetExistingItems<JellyfinSeries>();
        var jellyfinItems = new List<IJellyfinItem>();
        jellyfinItems.AddRange(existingMovies);
        jellyfinItems.AddRange(existingShows);
        var matches = await LibraryScanAsync(jellyfinItems, jellyseerrItems);
        var matchedIds = matches.Select(m => m.JellyseerrItem.Id).ToHashSet();
        var unmatched = jellyseerrItems.Where(item => !matchedIds.Contains(item.Id)).ToList();
        return (matches, unmatched);
    }

    /// <summary>
    /// Overload: Scan providing a flat list of Jellyfin items. Fetch Jellyseerr metadata via ReadMetadataAsync.
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
    /// Core scan: compare provided Jellyfin items against provided Jellyseerr metadata and return matches/unmatched.
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
            // Use the custom EqualsItem implementation rather than Equals cause I don't trust compile-time resolution.
            var jellyseerrItem = jellyseerrItems.FirstOrDefault(bm => bm.EqualsItem(jellyfinItem));

            if (jellyseerrItem != null)
            {
                _logger.LogTrace("Found match: '{JellyfinItemName}' (Id: {JellyfinItemId}) matches '{JellyseerrItemName}' (Id: {JellyseerrItemId})", 
                    jellyseerrItem.MediaName, jellyseerrItem.Id, jellyfinItem.Name, jellyfinItem.Id);
                
                matches.Add(new JellyMatch(jellyseerrItem, jellyfinItem));
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
            var bridgeFolderPath = _metadataService.GetJellyseerrItemDirectory(match.JellyseerrItem);
            var item = match.JellyfinItem;
            var ignoreFilePath = Path.Combine(bridgeFolderPath, ".ignore");
            
            try
            {
                _logger.LogTrace("Creating ignore file for {ItemName} (Id: {ItemId}) at {IgnoreFilePath}", 
                    item.Name, item.Id, ignoreFilePath);
                
                // Use the item's built-in serialization method
                var itemJson = item.ToJson(_dtoService);
                
                _logger.LogTrace("Successfully serialized {ItemName} to JSON - JSON length: {JsonLength} characters", 
                    item.Name, itemJson?.Length ?? 0);

                await File.WriteAllTextAsync(ignoreFilePath, itemJson);
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

        // Await all ignore file creation tasks
        await Task.WhenAll(ignoreFileTasks);
    }
}