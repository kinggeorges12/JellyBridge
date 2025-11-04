using Jellyfin.Data.Enums;
using Jellyfin.Plugin.JellyBridge.BridgeModels;
using Jellyfin.Plugin.JellyBridge.Configuration;
using Jellyfin.Plugin.JellyBridge.JellyfinModels;
using Jellyfin.Plugin.JellyBridge.JellyseerrModel;
using Jellyfin.Plugin.JellyBridge.Utils;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using static Jellyfin.Plugin.JellyBridge.BridgeModels.BridgeConfiguration;

namespace Jellyfin.Plugin.JellyBridge.Services;

/// <summary>
/// Service for sorting and organizing content in the JellyBridge library.
/// </summary>
public class SortService
{
    private readonly DebugLogger<SortService> _logger;
    private readonly JellyfinILibraryManager _libraryManager;
    private readonly JellyfinIUserDataManager _userDataManager;
    private readonly JellyfinIUserManager _userManager;
    private readonly MetadataService _metadataService;
    private readonly BridgeService _bridgeService;

    public SortService(ILogger<SortService> logger, JellyfinILibraryManager libraryManager, JellyfinIUserDataManager userDataManager, JellyfinIUserManager userManager, MetadataService metadataService, BridgeService bridgeService)
    {
        _logger = new DebugLogger<SortService>(logger);
        _libraryManager = libraryManager;
        _userDataManager = userDataManager;
        _userManager = userManager;
        _metadataService = metadataService;
        _bridgeService = bridgeService;
    }

    /// <summary>
    /// Sorts the JellyBridge library by applying the play count algorithm to all discover library items.
    /// This enables random sorting by play count in Jellyfin.
    /// </summary>
    /// <returns>A SortLibraryResult containing successful updates, failed item paths, and skipped item paths (ignored files).</returns>
    public async Task<SortLibraryResult> SortJellyBridge()
    {
        var result = new SortLibraryResult();
        
        try
        {
            // Get all users
            var users = _userManager.GetAllUsers().ToList();
            if (users.Count == 0)
            {
                _logger.LogWarning("No users found - cannot update play counts");
                result.Success = false;
                result.Message = "No users found - cannot update play counts";
                return result;
            }
            
            result.Users = users;

            // Get configuration setting for sort order
            var sortOrder = Plugin.GetConfigOrDefault<SortOrderOptions>(nameof(PluginConfiguration.SortOrder));
            result.SortAlgorithm = sortOrder;

            // Get all directories first (same for all users)
            var allDirectories = await GetAllDirectories();
            if (allDirectories == null || allDirectories.Count == 0)
            {
                result.Success = false;
                result.Message = "No directories found to update";
                return result;
            }

            // Apply the play count algorithm for each user separately (each user gets unique sort order)
            // Process all users in parallel for better performance
            var userTasks = users.Select(async user =>
            {
                // Generate play count map for this specific user (different randomization per user)
                Dictionary<string, (int playCount, BaseItemKind mediaType)>? directoryInfoMap;
                switch (sortOrder)
                {
                    case SortOrderOptions.None:
                        _logger.LogDebug("Using None sort order - setting play counts to zero for user {UserName}", user.Username);
                        directoryInfoMap = playCountZero(allDirectories);
                        break;
                    
                    case SortOrderOptions.Random:
                        _logger.LogDebug("Using Random sort order - randomizing play counts for user {UserName}", user.Username);
                        directoryInfoMap = playCountRandomize(allDirectories);
                        break;
                    
                    case SortOrderOptions.Smart:
                        _logger.LogDebug("Using Smart sort order - genre-based sorting for user {UserName}", user.Username);
                        directoryInfoMap = await playCountSmart(user, allDirectories);
                        break;
                    
                    case SortOrderOptions.Smartish:
                        _logger.LogDebug("Using Smartish sort order - genre-based sorting for user {UserName}", user.Username);
                        directoryInfoMap = await playCountSmartish(user, allDirectories);
                        break;
                        
                    default:
                        _logger.LogWarning("Unknown sort order value: {SortOrder}, defaulting to None for user {UserName}", sortOrder, user.Username);
                        directoryInfoMap = playCountZero(allDirectories);
                        break;
                }
                
                if (directoryInfoMap == null)
                {
                    _logger.LogWarning("Failed to generate play count map for user {UserName}", user.Username);
                    return (successes: new List<(IJellyfinItem item, int playCount)>(), failures: new List<string>(), skipped: new List<(IJellyfinItem? item, string path)>());
                }

                // Apply the play count algorithm for this user
                return await ApplyPlayCountAlgorithmAsync(user, directoryInfoMap);
            });

            // Wait for all users to complete processing
            var userResults = await Task.WhenAll(userTasks);
            
            // Aggregate results from all users
            var allSuccesses = new List<(IJellyfinItem item, int playCount)>();
            var allFailures = new List<string>();
            var allSkipped = new List<(IJellyfinItem? item, string path)>();
            
            foreach (var (successes, failures, skipped) in userResults)
            {
                allSuccesses.AddRange(successes);
                allFailures.AddRange(failures);
                allSkipped.AddRange(skipped);
            }
            
            result.Success = true;
            result.Message = "✓ Sort library completed successfully";
            
            // Populate ProcessResult
            result.ItemsSorted = allSuccesses;
            result.ItemsFailed = allFailures;
            result.ItemsSkipped = allSkipped;
            
            // Set refresh plan if items were sorted
            if (allSuccesses.Count > 0)
            {
                result.Refresh = new RefreshPlan
                {
                    FullRefresh = false,
                    RefreshImages = false
                };
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating play counts");
            result.Success = false;
            result.Message = $"Error updating play counts: {ex.Message}";
        }
        
        return result;
    }

    /// <summary>
    /// Gets all directories from the JellyBridge library with their media types.
    /// </summary>
    /// <returns>A list of (directory, mediaType) tuples, or null if no directories found.</returns>
    private async Task<List<(string directory, BaseItemKind mediaType)>?> GetAllDirectories()
    {
        // Get categorized directories that are actually in Jellyfin libraries
        var metadataItems = await _bridgeService.ReadMetadataLibraries();
        var movieDirectories = metadataItems.Where(item => item.item is JellyseerrMovie).Select(item => item.directory).ToList();
        var showDirectories = metadataItems.Where(item => item.item is JellyseerrShow).Select(item => item.directory).ToList();
        var totalCount = movieDirectories.Count + showDirectories.Count;

        if (totalCount == 0)
        {
            _logger.LogDebug("No directories found to update");
            return null;
        }

        // Combine movies and shows with their media types
        return movieDirectories.Select(dir => (dir, mediaType: BaseItemKind.Movie))
            .Concat(showDirectories.Select(dir => (dir, mediaType: BaseItemKind.Series)))
            .ToList();
    }

    /// <summary>
    /// Randomizes play counts by creating shuffled play count values and mapping them to directories.
    /// Each call generates a new random shuffle, so each user gets a unique sort order.
    /// </summary>
    /// <param name="allDirectories">List of directories with their media types</param>
    /// <returns>A dictionary mapping directory paths to (playCount, mediaType) tuples, or null if no directories found.</returns>
    private Dictionary<string, (int playCount, BaseItemKind mediaType)>? playCountRandomize(List<(string directory, BaseItemKind mediaType)> allDirectories)
    {
        var totalCount = allDirectories.Count;

        if (totalCount == 0)
        {
            _logger.LogDebug("No directories found to update");
            return null;
        }

        // Create a list of play count values (1000, 1100, 1200, etc. with increments of 100) and shuffle them
        // Using increments of 100 ensures that when users play items (incrementing by 1), the sort order remains stable
        // Each call to this method creates a NEW random shuffle, so each user gets unique sort order
        var random = System.Random.Shared;
        var playCounts = Enumerable.Range(0, totalCount)
            .Select(i => 1000 + (i * 100))
            .OrderBy(_ => random.Next())
            .ToList();

        // Create directory info map with play count and media type (for efficient lookup)
        return allDirectories
            .Select((item, index) => (item.directory, playCount: playCounts[index], item.mediaType))
            .ToDictionary(x => x.directory, x => (x.playCount, x.mediaType));
    }

    /// <summary>
    /// Smart sort algorithm that uses genre preferences from user's library (excluding JellyBridge items).
    /// For each genre in user's library, counts occurrences and adds 1.
    /// For JellyBridge items, sums genre mappings for matching genres, averages them, and adds random value.
    /// </summary>
    /// <param name="user">User to generate smart sort for</param>
    /// <param name="allDirectories">List of directories with their media types</param>
    /// <returns>A dictionary mapping directory paths to (playCount, mediaType) tuples, or null if no directories found.</returns>
    private Task<Dictionary<string, (int playCount, BaseItemKind mediaType)>?> playCountSmart(
        JellyfinUser user,
        List<(string directory, BaseItemKind mediaType)> allDirectories)
    {
        if (allDirectories == null || allDirectories.Count == 0)
        {
            _logger.LogDebug("No directories found to update");
            return Task.FromResult<Dictionary<string, (int playCount, BaseItemKind mediaType)>?>(null);
        }

        // Get JellyBridge library directory to exclude from user's library
        var libraryDirectory = Plugin.GetConfigOrDefault<string>(nameof(PluginConfiguration.LibraryDirectory));
        var bridgeLibraryPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrEmpty(libraryDirectory))
        {
            bridgeLibraryPaths.Add(Path.GetFullPath(libraryDirectory));
        }

        // Get all items from user's library (excluding JellyBridge items)
        List<JellyfinMovie> userMovies;
        List<JellyfinSeries> userSeries;
        try
        {
            userMovies = _libraryManager.GetUserLibraryItems<JellyfinMovie>(user, bridgeLibraryPaths);
            userSeries = _libraryManager.GetUserLibraryItems<JellyfinSeries>(user, bridgeLibraryPaths);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get user library items for smart sort");
            return Task.FromResult<Dictionary<string, (int playCount, BaseItemKind mediaType)>?>(null);
        }

        // Count genres in user's library (excluding JellyBridge) and add 1 to each count
        var genreCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var movie in userMovies)
        {
            var genres = movie.Inner.Genres;
            if (genres != null)
            {
                foreach (var genre in genres)
                {
                    if (!string.IsNullOrEmpty(genre))
                    {
                        genreCounts.TryGetValue(genre, out var count);
                        genreCounts[genre] = count + 1;
                    }
                }
            }
        }
        foreach (var series in userSeries)
        {
            var genres = series.Inner.Genres;
            if (genres != null)
            {
                foreach (var genre in genres)
                {
                    if (!string.IsNullOrEmpty(genre))
                    {
                        genreCounts.TryGetValue(genre, out var count);
                        genreCounts[genre] = count + 1;
                    }
                }
            }
        }

        // Add 1 to each genre count (as per requirement)
        foreach (var genre in genreCounts.Keys.ToList())
        {
            genreCounts[genre] += 1;
        }

        // Calculate max genre count for random number range
        var maxGenreCount = genreCounts.Values.Any() ? genreCounts.Values.Max() : 0;
        var random = System.Random.Shared;

        // Get genres for each JellyBridge item and calculate play count
        var result = new Dictionary<string, (int playCount, BaseItemKind mediaType)>();
        var failedDirectories = new List<(string directory, BaseItemKind mediaType)>();
        
        foreach (var (directory, mediaType) in allDirectories)
        {
            try
            {
                var baseItem = _libraryManager.FindItemByDirectoryPath(directory);
                if (baseItem == null)
                {
                    continue;
                }

                // Get genres for this JellyBridge item
                var itemGenres = baseItem.Genres?.ToList() ?? new List<string>();
                
                // Find matching genres between JellyBridge item and user's library
                var matchingGenres = itemGenres.Where(g => 
                    !string.IsNullOrEmpty(g) && genreCounts.ContainsKey(g)).ToList();
                
                int playCount;
                if (matchingGenres.Any())
                {
                    // Sum genre counts for matching genres
                    var genreSum = matchingGenres.Sum(genre => genreCounts[genre]);
                    
                    // Average across all matching genres and round to int
                    playCount = (int)Math.Round((double)genreSum / matchingGenres.Count);
                }
                else
                {
                    // No matching genres, use minimum value
                    playCount = 0;
                }
                
                // Add 100 to base count
                playCount += 100;
                
                result[directory] = (playCount, mediaType);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to process directory for smart sort: {Directory}", directory);
                failedDirectories.Add((directory, mediaType));
            }
        }

        // Apply random sort to all failed directories at once
        if (failedDirectories.Count > 0)
        {
            _logger.LogDebug("Applying random sort fallback to {Count} failed directories", failedDirectories.Count);
            var fallbackResult = playCountRandomize(failedDirectories);
            if (fallbackResult != null)
            {
                foreach (var kvp in fallbackResult)
                {
                    result[kvp.Key] = kvp.Value;
                }
            }
        }

        return Task.FromResult<Dictionary<string, (int playCount, BaseItemKind mediaType)>?>(result.Count > 0 ? result : null);
    }

    /// <summary>
    /// Smartish sort algorithm that uses playCountSmart and adds a random value from 1 to (max-min) of all directories.
    /// </summary>
    /// <param name="user">User to generate smartish sort for</param>
    /// <param name="allDirectories">List of directories with their media types</param>
    /// <returns>A dictionary mapping directory paths to (playCount, mediaType) tuples, or null if no directories found.</returns>
    private async Task<Dictionary<string, (int playCount, BaseItemKind mediaType)>?> playCountSmartish(
        JellyfinUser user,
        List<(string directory, BaseItemKind mediaType)> allDirectories)
    {
        if (allDirectories == null || allDirectories.Count == 0)
        {
            _logger.LogDebug("No directories found to update");
            return null;
        }

        // Get base play counts from smart sort
        var smartResult = await playCountSmart(user, allDirectories);
        if (smartResult == null || smartResult.Count == 0)
        {
            return null;
        }

        // Calculate min and max play counts
        var playCounts = smartResult.Values.Select(v => v.playCount).ToList();
        var minPlayCount = playCounts.Min();
        var maxPlayCount = playCounts.Max();
        var range = maxPlayCount - minPlayCount;

        // Add random value from 0 to range (or 10 if range is 0) to each play count
        var random = System.Random.Shared;
        var result = new Dictionary<string, (int playCount, BaseItemKind mediaType)>();

        foreach (var kvp in smartResult)
        {
            var randomOffset = random.Next(0, range + 10); // 0 to range + 10 (inclusive)
            var newPlayCount = kvp.Value.playCount + randomOffset;
            result[kvp.Key] = (newPlayCount, kvp.Value.mediaType);
        }

        return result;
    }

    /// <summary>
    /// Sets play counts to zero by mapping all directories to play count zero.
    /// </summary>
    /// <param name="allDirectories">List of directories with their media types</param>
    /// <returns>A dictionary mapping directory paths to (playCount, mediaType) tuples, or null if no directories found.</returns>
    private Dictionary<string, (int playCount, BaseItemKind mediaType)>? playCountZero(List<(string directory, BaseItemKind mediaType)> allDirectories)
    {
        var totalCount = allDirectories.Count;

        if (totalCount == 0)
        {
            _logger.LogDebug("No directories found to update");
            return null;
        }

        // Create directory info map with play count set to zero and media type (for efficient lookup)
        return allDirectories
            .Select(item => (item.directory, playCount: 0, item.mediaType))
            .ToDictionary(x => x.directory, x => (x.playCount, x.mediaType));
    }

    /// <summary>
    /// Applies the play count algorithm to all discover library items for a single user.
    /// </summary>
    /// <param name="user">User to update play counts for</param>
    /// <param name="directoryInfoMap">Dictionary mapping directory paths to play counts and media types</param>
    /// <returns>A tuple containing lists of successes, failures, and skipped items</returns>
    private async Task<(List<(IJellyfinItem item, int playCount)> successes,
        List<string> failures,
        List<(IJellyfinItem? item, string path)> skipped)> ApplyPlayCountAlgorithmAsync(
        JellyfinUser user,
        Dictionary<string, (int playCount, BaseItemKind mediaType)> directoryInfoMap)
    {
        var successes = new List<(IJellyfinItem item, int playCount)>();
        var failures = new List<string>();
        var skipped = new List<(IJellyfinItem? item, string path)>();
        
        // Get configuration setting for marking media as played
        var markMediaPlayed = Plugin.GetConfigOrDefault<bool>(nameof(PluginConfiguration.MarkMediaPlayed));

        // Update play count for each item - parallelize by item
        // Sort by play count (ascending) before updating to ensure lower play counts are processed first
        var updateTasks = directoryInfoMap.OrderBy(kvp => kvp.Value.playCount).Select(kvp => Task.Run(() =>
            {
                var directory = kvp.Key;
                var (assignedPlayCount, mediaType) = kvp.Value;
                
                try
                {
                    // Check if directory is ignored (has .ignore file)
                    var ignoreFile = Path.Combine(directory, ".ignore");
                    if (File.Exists(ignoreFile))
                    {
                        _logger.LogDebug("Item ignored (has .ignore file) for path: {Path}", directory);
                        // Try to find the item even if it's skipped (for the result object)
                        var skippedBaseItem = _libraryManager.FindItemByDirectoryPath(directory);
                        IJellyfinItem? skippedWrapper = null;
                        if (skippedBaseItem != null)
                        {
                            try
                            {
                                if (mediaType == BaseItemKind.Movie)
                                {
                                    skippedWrapper = JellyfinMovie.FromItem(skippedBaseItem);
                                }
                                else if (mediaType == BaseItemKind.Series)
                                {
                                    skippedWrapper = JellyfinSeries.FromItem(skippedBaseItem);
                                }
                            }
                            catch
                            {
                                // Item type doesn't match, leave as null
                            }
                        }
                        return (success: ((IJellyfinItem item, int playCount)?)null, failure: (string?)null, skipped: (skippedWrapper, directory));
                    }

                    // Find item by directory path - handles both movies and shows
                    var baseItem = _libraryManager.FindItemByDirectoryPath(directory);
                    
                    if (baseItem == null)
                    {
                        _logger.LogDebug("Item not found for path: {Path}", directory);
                        return (success: ((IJellyfinItem item, int playCount)?)null, failure: directory, skipped: ((IJellyfinItem? item, string path)?)null);
                    }
                    
                    // Convert BaseItem to appropriate wrapper
                    IJellyfinItem? item = null;
                    try
                    {
                        if (mediaType == BaseItemKind.Movie)
                        {
                            item = JellyfinMovie.FromItem(baseItem);
                        }
                        else if (mediaType == BaseItemKind.Series)
                        {
                            item = JellyfinSeries.FromItem(baseItem);
                        }
                    }
                    catch (ArgumentException)
                    {
                        _logger.LogDebug("Item type mismatch for path: {Path}", directory);
                        return (success: ((IJellyfinItem item, int playCount)?)null, failure: directory, skipped: ((IJellyfinItem? item, string path)?)null);
                    }
                    
                    if (item == null)
                    {
                        _logger.LogDebug("Could not create wrapper for item at path: {Path}", directory);
                        return (success: ((IJellyfinItem item, int playCount)?)null, failure: directory, skipped: ((IJellyfinItem? item, string path)?)null);
                    }
                    
                    string itemName = item.Name;
                    
                    // Update play count for this user
                    try
                    {
                        if (_userDataManager.TryUpdatePlayCount(user, item, assignedPlayCount))
                        {
                            _logger.LogTrace("Updated play count for user {UserName}, item: {ItemName} ({Path}) to {PlayCount} (mediaType: {MediaType})", 
                                user.Username, itemName, directory, assignedPlayCount, mediaType);
                            
                            var result = new JellyfinWrapperResult();
                            try
                            {
                                var wrapperName = string.Empty;
                                // For shows, update placeholder episode (S00E00 special) play status based on MarkMediaPlayed setting
                                if (mediaType == BaseItemKind.Series && item is JellyfinSeries seriesWrapper)
                                {
                                    result = seriesWrapper.TrySetEpisodePlayCount(user, _userDataManager, markMediaPlayed);
                                    wrapperName = seriesWrapper.Name;
                                }
                                // Movies usually have no badge
                                else if (mediaType == BaseItemKind.Movie && item is JellyfinMovie movieWrapper)
                                {
                                    result = movieWrapper.TrySetMoviePlayCount(user, _userDataManager, markMediaPlayed);
                                    wrapperName = movieWrapper.Name;
                                }
                                if (result.Success)
                                {
                                    _logger.LogTrace("Placeholder episode play status updated for series '{SeriesName}' for user {UserName}: {Message}",
                                        wrapperName, user.Username, result.Message);
                                }
                                else
                                {
                                    _logger.LogTrace("Placeholder episode play status not updated for series '{SeriesName}' for user {UserName}: {Message}",
                                        wrapperName, user.Username, result.Message);
                                }
                            }
                            catch (ArgumentException ex)
                            {
                                _logger.LogTrace(ex, "Item '{ItemName}' is not a Series, skipping placeholder episode play status update", itemName);
                            }
                            catch (Exception ex)
                            {
                                _logger.LogTrace(ex, "Could not update placeholder episode play status for user {UserName}, item: {ItemName}", user.Username, itemName);
                            }
                        }
                        else
                        {
                            _logger.LogWarning("Failed to update play count for user {UserName}, item: {Path}", user.Username, directory);
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        _logger.LogWarning("Operation canceled while updating play count for user {UserName}, item: {Path}", user.Username, directory);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to update play count for user {UserName}, item: {Path}", user.Username, directory);
                    }

                    return (success: ((IJellyfinItem item, int playCount)?)(item, assignedPlayCount), failure: (string?)null, skipped: ((IJellyfinItem? item, string path)?)null);
                }
                catch (OperationCanceledException)
                {
                    _logger.LogWarning("Operation canceled while processing directory: {Directory}", directory);
                    return (success: ((IJellyfinItem item, int playCount)?)null, failure: directory, skipped: ((IJellyfinItem? item, string path)?)null);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to update play count for directory: {Directory}", directory);
                    return (success: ((IJellyfinItem item, int playCount)?)null, failure: directory, skipped: ((IJellyfinItem? item, string path)?)null);
                }
            }));

        // Wait for all item updates to complete and collect results
        var results = await Task.WhenAll(updateTasks);
        
        foreach (var (success, failure, skippedItem) in results)
        {
            if (success.HasValue)
            {
                successes.Add((success.Value.item, success.Value.playCount));
            }
            else if (failure != null)
            {
                failures.Add(failure);
            }
            else if (skippedItem.HasValue)
            {
                skipped.Add(skippedItem.Value);
            }
        }

        return (successes, failures, skipped);
    }

}

