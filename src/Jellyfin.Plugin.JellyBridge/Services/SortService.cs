using Jellyfin.Data.Enums;
using Jellyfin.Plugin.JellyBridge.BridgeModels;
using Jellyfin.Plugin.JellyBridge.Configuration;
using Jellyfin.Plugin.JellyBridge.JellyfinModels;
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

            // Record total unique items (movies + shows) to display as Processed
            result.Processed = allDirectories.Count;

            // Apply the play count algorithm for each user separately (each user gets unique sort order)
            // Process all users in parallel for better performance
            var userTasks = users.Select(user => Task.Run(async () =>
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
                        directoryInfoMap = playCountRandom(allDirectories);
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

                // Calculate date mappings from play counts
                var dateMapping = CalculatePlayDateMapping(directoryInfoMap);

                // Combine directory info and date mapping using directory string as the common key
                var combinedInfoMap = directoryInfoMap.ToDictionary(
                    kvp => kvp.Key,
                    kvp => (kvp.Value.playCount, kvp.Value.mediaType, dateMapping.TryGetValue(kvp.Key, out var date) ? date : null)
                );

                // Apply the play count algorithm for this user asynchronously (sequential within each user)
                return await ApplyPlayCountAlgorithmAsync(user, combinedInfoMap).ConfigureAwait(false);
            }));

            // Wait for all users to complete processing
            var userResults = await Task.WhenAll(userTasks);
            
            // Aggregate results from all users
            var allSuccesses = new List<(IJellyfinItem item, int playCount)>();
            var allFailures = new List<string>();
            var allSkipped = new List<(IJellyfinItem? item, string path)>();
            
            foreach (var userResult in userResults)
            {
                List<(IJellyfinItem item, int playCount)> successes = userResult.successes;
                List<string> failures = userResult.failures;
                List<(IJellyfinItem? item, string path)> skipped = userResult.skipped;
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
    private Dictionary<string, (int playCount, BaseItemKind mediaType)>? playCountRandom(List<(string directory, BaseItemKind mediaType)> allDirectories)
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
            .Select(i => 100 + (i * 100))
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
            // Return null so the failure is visible in results (items will be counted as skipped/failed)
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
                    // Item not found - directory is likely ignored (similar to .ignore file)
                    // Include it in result with zero play count so it gets processed and counted as skipped
                    // This matches Random sort behavior where all directories are included
                    result[directory] = (0, mediaType);
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

                // Sort play count in ascending order
                playCount = maxGenreCount - playCount;

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

        // Apply random sort to all failed directories at once (exceptions only, not null baseItems)
        if (failedDirectories.Count > 0)
        {
            _logger.LogDebug("Applying random sort fallback to {Count} failed directories", failedDirectories.Count);
            var fallbackResult = playCountRandom(failedDirectories);
            if (fallbackResult != null)
            {
                foreach (var kvp in fallbackResult)
                {
                    result[kvp.Key] = kvp.Value;
                }
            }
        }

        // Return the dictionary - consistent with playCountRandom which always returns a dictionary when input has items
        return Task.FromResult<Dictionary<string, (int playCount, BaseItemKind mediaType)>?>(result);
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
    /// Calculates date mappings from play count mappings.
    /// Dates go back one day starting from yesterday (DateTime.UtcNow.AddDays(-1)), with most recent date assigned to lowest play count and earliest date to highest play count.
    /// This ensures Android TV's "Last Played" sort (DESCENDING) matches the ascending play count order.
    /// Items with the same play count get the same date.
    /// If play count is zero, LastPlayedDate is set to null.
    /// </summary>
    /// <param name="directoryInfoMap">Dictionary mapping directory paths to play counts and media types</param>
    /// <returns>Dictionary mapping directory paths to their assigned play dates (null for zero play count)</returns>
    private Dictionary<string, DateTime?> CalculatePlayDateMapping(Dictionary<string, (int playCount, BaseItemKind mediaType)> directoryInfoMap)
    {
        if (directoryInfoMap == null || directoryInfoMap.Count == 0)
        {
            return new Dictionary<string, DateTime?>();
        }

        // Get unique play counts sorted in ascending order (lowest to highest), excluding zero
        var uniquePlayCounts = directoryInfoMap.Values
            .Select(v => v.playCount)
            .Distinct()
            .Where(pc => pc > 0) // Exclude zero play counts
            .OrderBy(pc => pc)
            .ToList();

        // Create mapping from play count to date
        // Lowest play count gets yesterday (DateTime.UtcNow.AddDays(-1)) - most recent date, so it appears first when Android TV sorts by Last Played (DESCENDING)
        // Higher play counts go further back in time - older dates
        // All dates are normalized to midnight (start of day) and explicitly set to UTC for compatibility with UserDataManager.SaveUserData
        var playCountToDateMap = new Dictionary<int, DateTime?>();
        // Use .Date to normalize to midnight, then explicitly ensure UTC Kind (DateTime.UtcNow.Date preserves UTC Kind, but being explicit)
        var baseDate = DateTime.SpecifyKind(DateTime.UtcNow.AddDays(-1).Date, DateTimeKind.Utc); // Start from yesterday at midnight UTC
        
        for (int i = 0; i < uniquePlayCounts.Count; i++)
        {
            var playCount = uniquePlayCounts[i];
            // Days to subtract: current index
            // This ensures lowest play count (index 0) gets 0 days subtracted (yesterday - most recent), highest gets (uniquePlayCounts.Count - 1) days subtracted (oldest)
            var daysToSubtract = i;
            // AddDays preserves the DateTime and Kind, so result stays at midnight UTC
            playCountToDateMap[playCount] = baseDate.AddDays(-daysToSubtract);
        }

        // Create directory to date mapping
        // Zero play count gets null, others get their assigned date
        var dateMapping = new Dictionary<string, DateTime?>();
        foreach (var kvp in directoryInfoMap)
        {
            if (kvp.Value.playCount == 0)
            {
                dateMapping[kvp.Key] = null;
            }
            else
            {
                dateMapping[kvp.Key] = playCountToDateMap[kvp.Value.playCount];
            }
        }

        return dateMapping;
    }

    /// <summary>
    /// Applies the play count algorithm to all discover library items for a single user.
    /// </summary>
    /// <param name="user">User to update play counts for</param>
    /// <param name="combinedInfoMap">Dictionary mapping directory paths to combined info (play count, media type, and play date)</param>
    /// <returns>A tuple containing lists of successes, failures, and skipped items</returns>
    private async Task<(List<(IJellyfinItem item, int playCount)> successes,
        List<string> failures,
        List<(IJellyfinItem? item, string path)> skipped)> ApplyPlayCountAlgorithmAsync(
        JellyfinUser user,
        Dictionary<string, (int playCount, BaseItemKind mediaType, DateTime? playDate)> combinedInfoMap)
    {
        var successes = new List<(IJellyfinItem item, int playCount)>();
        var failures = new List<string>();
        var skipped = new List<(IJellyfinItem? item, string path)>();
        
        // Get configuration setting for marking media as played
        var markMediaPlayed = Plugin.GetConfigOrDefault<bool>(nameof(PluginConfiguration.MarkMediaPlayed));

        // Collect tasks for play count updates and play status marking
        var playCountTasks = new List<(Task<JellyfinWrapperResult> task, IJellyfinItem item, int playCount, string directory)>();
        var playStatusTasks = new List<Task>();

        // Create tasks for all items
        foreach (var kvp in combinedInfoMap)
        {
            var directory = kvp.Key;
            var (assignedPlayCount, mediaType, assignedPlayDate) = kvp.Value;

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
                    skipped.Add((skippedWrapper, directory));
                    continue;
                }

                // Find item by directory path - handles both movies and shows
                var baseItem = _libraryManager.FindItemByDirectoryPath(directory);

                if (baseItem == null)
                {
                    _logger.LogDebug("Item not found for path: {Path}", directory);
                    failures.Add(directory);
                    continue;
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
                    failures.Add(directory);
                    continue;
                }

                if (item == null)
                {
                    _logger.LogDebug("Could not create wrapper for item at path: {Path}", directory);
                    failures.Add(directory);
                    continue;
                }

                string itemName = item.Name;

                // Create task for updating play count and last played date
                // assignedPlayDate is already extracted from combinedInfoMap above
                var playCountTask = _userDataManager.TryUpdatePlayCountAsync(user, item, assignedPlayCount, assignedPlayDate);
                playCountTasks.Add((playCountTask, item, assignedPlayCount, directory));

                // Create task for marking play status (runs independently, doesn't affect success/failure)
                var playStatusTask = _userDataManager.MarkItemPlayStatusAsync(user, item, markMediaPlayed);
                playStatusTasks.Add(playStatusTask);
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning("Operation canceled while processing directory: {Directory}", directory);
                failures.Add(directory);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to create tasks for directory: {Directory}", directory);
                failures.Add(directory);
            }
        }

        // Await all play count tasks at once
        // Task.WhenAll waits for all tasks to complete before throwing (if any fail)
        // Ensure we always process all results even if aggregation fails
        try
        {
            await Task.WhenAll(playCountTasks.Select(t => t.task)).ConfigureAwait(false);
        }
        catch (AggregateException ex)
        {
            // Log summary of failed tasks, but continue to process all results below
            // Task.WhenAll already waited for all tasks to complete before throwing
            _logger.LogWarning(ex, "Some play count update tasks failed for user {UserName}. Processing all results individually.", user.Username);
        }
        catch (OperationCanceledException ex)
        {
            // Cancellation occurred, but all tasks should still be in a final state
            _logger.LogWarning(ex, "Operation canceled while awaiting play count update tasks for user {UserName}. Processing all results individually.", user.Username);
        }
        catch (Exception ex)
        {
            // Catch any other unexpected exceptions during aggregation
            // Even if this fails, we still want to process all task results
            _logger.LogError(ex, "Unexpected exception while awaiting play count update tasks for user {UserName}. Processing all results individually.", user.Username);
        }

        // Process results and determine success/failure/skipped based on results
        foreach (var (task, item, playCount, directory) in playCountTasks)
        {
            if (task.IsFaulted)
            {
                _logger.LogWarning(task.Exception?.GetBaseException() ?? new Exception("Task faulted with unknown error"),
                    "Failed to update play count for user {UserName}, item: {Path}", user.Username, directory);
                failures.Add(directory);
            }
            else if (task.IsCanceled)
            {
                _logger.LogWarning("Operation canceled while updating play count for user {UserName}, item: {Path}", user.Username, directory);
                failures.Add(directory);
            }
            else
            {
                var result = task.Result;
                if (result.Success)
                {
                    _logger.LogTrace("Updated play count and last played date for user {UserName}, item: {ItemName} ({Path}) to {PlayCount}",
                        user.Username, item.Name, directory, playCount);
                    successes.Add((item, playCount));
                }
                else
                {
                    _logger.LogWarning("Failed to update play count for user {UserName}, item: {Path}: {Message}", 
                        user.Username, directory, result.Message);
                    failures.Add(directory);
                }
            }
        }

        // Await all play status tasks (fire and forget - errors are handled in user data manager)
        try
        {
            await Task.WhenAll(playStatusTasks).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // Log aggregate exception but don't affect success/failure/skipped
            _logger.LogWarning(ex, "Some play status marking tasks failed for user {UserName}", user.Username);
        }

        return (successes, failures, skipped);
    }

}

