using Jellyfin.Plugin.JellyBridge.Configuration;
using Jellyfin.Plugin.JellyBridge.JellyfinModels;
using Jellyfin.Plugin.JellyBridge.JellyseerrModel;
using Jellyfin.Plugin.JellyBridge.Utils;
using Microsoft.Extensions.Logging;
using System.IO;
using static Jellyfin.Plugin.JellyBridge.BridgeModels.BridgeConfiguration;

namespace Jellyfin.Plugin.JellyBridge.Services;

/// <summary>
/// Service for managing sort operations and play count algorithms for Jellyseerr bridge items.
/// </summary>
public class SortService
{
    private readonly DebugLogger<SortService> _logger;
    private readonly JellyfinILibraryManager _libraryManager;
    private readonly JellyfinIUserDataManager _userDataManager;
    private readonly JellyfinIUserManager _userManager;
    private readonly MetadataService _metadataService;

    public SortService(ILogger<SortService> logger, JellyfinILibraryManager libraryManager, JellyfinIUserDataManager userDataManager, JellyfinIUserManager userManager, MetadataService metadataService)
    {
        _logger = new DebugLogger<SortService>(logger);
        _libraryManager = libraryManager;
        _userDataManager = userDataManager;
        _userManager = userManager;
        _metadataService = metadataService;
    }


    /// <summary>
    /// Randomizes play counts by creating shuffled play count values and mapping them to directories.
    /// </summary>
    /// <returns>A dictionary mapping directory paths to (playCount, isShow) tuples, or null if no directories found.</returns>
    private Dictionary<string, (int playCount, bool isShow)>? playCountRandomize()
    {
        // Get categorized directories
        var (movieDirectories, showDirectories) = _metadataService.ReadMetadataFolders();
        var totalCount = movieDirectories.Count + showDirectories.Count;

        if (totalCount == 0)
        {
            _logger.LogDebug("No directories found to update");
            return null;
        }

        // Create a list of play count values (1000, 1100, 1200, etc. with increments of 100) and shuffle them
        // Using increments of 100 ensures that when users play items (incrementing by 1), the sort order remains stable
        var random = System.Random.Shared;
        var playCounts = Enumerable.Range(0, totalCount)
            .Select(i => 1000 + (i * 100))
            .OrderBy(_ => random.Next())
            .ToList();

        // Create directory info map with play count and isShow flag (for efficient lookup)
        // Combine movies and shows, then map each to (playCount, isShow) tuple
        return movieDirectories.Select(dir => (dir, isShow: false))
            .Concat(showDirectories.Select(dir => (dir, isShow: true)))
            .Select((item, index) => (item.dir, playCount: playCounts[index], item.isShow))
            .ToDictionary(x => x.dir, x => (x.playCount, x.isShow));
    }

    /// <summary>
    /// Sets play counts to zero by mapping all directories to play count zero.
    /// </summary>
    /// <returns>A dictionary mapping directory paths to (playCount, isShow) tuples, or null if no directories found.</returns>
    private Dictionary<string, (int playCount, bool isShow)>? playCountZero()
    {
        // Get categorized directories
        var (movieDirectories, showDirectories) = _metadataService.ReadMetadataFolders();
        var totalCount = movieDirectories.Count + showDirectories.Count;

        if (totalCount == 0)
        {
            _logger.LogDebug("No directories found to update");
            return null;
        }

        // Create directory info map with play count set to zero and isShow flag (for efficient lookup)
        // Combine movies and shows, then map each to (playCount: 0, isShow) tuple
        return movieDirectories.Select(dir => (dir, isShow: false))
            .Concat(showDirectories.Select(dir => (dir, isShow: true)))
            .Select(item => (item.dir, playCount: 0, item.isShow))
            .ToDictionary(x => x.dir, x => (x.playCount, x.isShow));
    }

    /// <summary>
    /// Applies the play count algorithm to all discover library items across all users.
    /// This enables random sorting by play count in Jellyfin.
    /// Uses MetadataService.ReadMetadataFolders to discover movie and show directories.
    /// </summary>
    /// <returns>A tuple containing a list of successful updates (name, type, playCount), a list of failed item paths, and a list of skipped item paths (ignored files).</returns>
    public async Task<(List<(string name, string type, int playCount)> successes, List<string> failures, List<string> skipped)> ApplyPlayCountAlgorithmAsync()
    {
        var successes = new List<(string name, string type, int playCount)>();
        var failures = new List<string>();
        var skipped = new List<string>();
        
        // Get configuration setting for marking shows as played
        var markShowsPlayed = Plugin.GetConfigOrDefault<bool>(nameof(PluginConfiguration.MarkShowsPlayed));
        
        // Get configuration setting for sort order
        var sortOrder = Plugin.GetConfigOrDefault<SortOrderOptions>(nameof(PluginConfiguration.SortOrder));
        
        try
        {
            // Get all users
            var users = _userManager.GetAllUsers().ToList();
            if (users.Count == 0)
            {
                _logger.LogWarning("No users found - cannot update play counts");
                return (successes, failures, skipped);
            }

            // Choose play count algorithm based on configuration
            Dictionary<string, (int playCount, bool isShow)>? directoryInfoMap;
            switch (sortOrder)
            {
                case SortOrderOptions.None:
                    _logger.LogDebug("Using None sort order - setting play counts to zero");
                    directoryInfoMap = playCountZero();
                    break;
                
                case SortOrderOptions.Random:
                    _logger.LogDebug("Using Random sort order - randomizing play counts");
                    directoryInfoMap = playCountRandomize();
                    break;
                
                case SortOrderOptions.Smart:
                    //throw new NotImplementedException("Smart sort order is not yet implemented");
                    goto default; // Fall through to default case
                    
                default:
                    _logger.LogWarning("Unknown sort order value: {SortOrder}, defaulting to None", sortOrder);
                    directoryInfoMap = playCountZero();
                    break;
            }
            
            if (directoryInfoMap == null)
            {
                return (successes, failures, skipped);
            }

            // Update play count for each item across all users - parallelize by item
            // Sort by play count (ascending) before updating to ensure lower play counts are processed first
            var updateTasks = directoryInfoMap.OrderBy(kvp => kvp.Value.playCount).Select(async kvp =>
            {
                var directory = kvp.Key;
                var (assignedPlayCount, isShowDirectory) = kvp.Value;
                
                try
                {
                    // Check if directory is ignored (has .ignore file)
                    var ignoreFile = Path.Combine(directory, ".ignore");
                    if (File.Exists(ignoreFile))
                    {
                        _logger.LogDebug("Item ignored (has .ignore file) for path: {Path}", directory);
                        return (success: ((string name, string type, int playCount)?)null, failure: (string?)null, skipped: directory);
                    }

                    // Find item by directory path - handles both movies and shows
                    var item = _libraryManager.FindItemByDirectoryPath(directory);
                    
                    if (item == null)
                    {
                        _logger.LogDebug("Item not found for path: {Path}", directory);
                        return (success: ((string name, string type, int playCount)?)null, failure: directory, skipped: (string?)null);
                    }
                    string itemName = item.Name;
                    string itemType = item.GetType().Name;
                    
                    // Update play count for each user - parallelize user updates for this item
                    var userUpdateTasks = users.Select(user => Task.Run(() =>
                    {
                        try
                        {
                            if (_userDataManager.TryUpdatePlayCount(user, item, assignedPlayCount))
                            {
                                _logger.LogTrace("Updated play count for user {UserName}, item: {ItemName} ({Path}) to {PlayCount} (isShowDirectory: {IsShowDirectory})", 
                                    user.Username, itemName, directory, assignedPlayCount, isShowDirectory);
                                
                                // For shows, update placeholder episode (S00E00 special) play status based on MarkShowsPlayed setting
                                if (isShowDirectory)
                                {
                                    try
                                    {
                                        var seriesWrapper = JellyfinSeries.FromItem(item);
                                        _logger.LogTrace("Attempting to update placeholder episode play status for series '{SeriesName}' for user {UserName} (MarkShowsPlayed: {MarkShowsPlayed})", 
                                            seriesWrapper.Name, user.Username, markShowsPlayed);
                                        
                                        var result = seriesWrapper.TrySetEpisodePlayCount(user, _userDataManager, markShowsPlayed);
                                        
                                        if (result.Success)
                                        {
                                            _logger.LogTrace("Placeholder episode play status updated for series '{SeriesName}' for user {UserName}: {Message}", 
                                                seriesWrapper.Name, user.Username, result.Message);
                                        }
                                        else
                                        {
                                            _logger.LogTrace("Placeholder episode play status not updated for series '{SeriesName}' for user {UserName}: {Message}", 
                                                seriesWrapper.Name, user.Username, result.Message);
                                        }
                                    }
                                    catch (ArgumentException ex)
                                    {
                                        // Item is not a Series - this is expected for some items
                                        _logger.LogTrace(ex, "Item '{ItemName}' is not a Series, skipping placeholder episode play status update", itemName);
                                    }
                                    catch (Exception ex)
                                    {
                                        // Handle other errors
                                        _logger.LogTrace(ex, "Could not update placeholder episode play status for user {UserName}, item: {ItemName}", user.Username, itemName);
                                    }
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
                    }));

                    // Wait for all user updates for this item to complete
                    await Task.WhenAll(userUpdateTasks);

                    return (success: ((string name, string type, int playCount)?)(itemName, itemType, assignedPlayCount), failure: (string?)null, skipped: (string?)null);
                }
                catch (OperationCanceledException)
                {
                    _logger.LogWarning("Operation canceled while processing directory: {Directory}", directory);
                    return (success: ((string name, string type, int playCount)?)null, failure: directory, skipped: (string?)null);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to update play count for directory: {Directory}", directory);
                    return (success: ((string name, string type, int playCount)?)null, failure: directory, skipped: (string?)null);
                }
            });

            // Wait for all item updates to complete and collect results
            var results = await Task.WhenAll(updateTasks);
            
            foreach (var (success, failure, skippedItem) in results)
            {
                if (success.HasValue)
                {
                    successes.Add((success.Value.name, success.Value.type, success.Value.playCount));
                }
                else if (failure != null)
                {
                    failures.Add(failure);
                }
                else if (skippedItem != null)
                {
                    skipped.Add(skippedItem);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating play counts");
        }
        
        return (successes, failures, skipped);
    }
}

