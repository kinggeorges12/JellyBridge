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

            // Choose play count algorithm based on configuration
            Dictionary<string, (int playCount, BaseItemKind mediaType)>? directoryInfoMap;
            switch (sortOrder)
            {
                case SortOrderOptions.None:
                    _logger.LogDebug("Using None sort order - setting play counts to zero");
                    directoryInfoMap = await playCountZero();
                    break;
                
                case SortOrderOptions.Random:
                    _logger.LogDebug("Using Random sort order - randomizing play counts");
                    directoryInfoMap = await playCountRandomize();
                    break;
                
                case SortOrderOptions.Smart:
                    //throw new NotImplementedException("Smart sort order is not yet implemented");
                    goto default; // Fall through to default case
                    
                default:
                    _logger.LogWarning("Unknown sort order value: {SortOrder}, defaulting to None", sortOrder);
                    directoryInfoMap = await playCountZero();
                    break;
            }
            
            if (directoryInfoMap == null)
            {
                result.Success = false;
                result.Message = "No directories found to update";
                return result;
            }

            // Apply the play count algorithm
            var (successes, failures, skipped) = await ApplyPlayCountAlgorithmAsync(users, directoryInfoMap);
            
            result.Success = true;
            result.Message = $"Sort library algorithm completed successfully ({sortOrder})";
            result.Details = $"Algorithm: {sortOrder}\nUsers: {users.Count} (play counts updated for {users.Count} user{(users.Count == 1 ? "" : "s")})";
            
            // Populate ProcessResult
            result.ItemsSorted = successes;
            result.ItemsFailed = failures;
            result.ItemsSkipped = skipped;
            
            // Set refresh plan if items were sorted
            if (successes.Count > 0)
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
    /// Randomizes play counts by creating shuffled play count values and mapping them to directories.
    /// </summary>
    /// <returns>A dictionary mapping directory paths to (playCount, mediaType) tuples, or null if no directories found.</returns>
    private async Task<Dictionary<string, (int playCount, BaseItemKind mediaType)>?> playCountRandomize()
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

        // Create a list of play count values (1000, 1100, 1200, etc. with increments of 100) and shuffle them
        // Using increments of 100 ensures that when users play items (incrementing by 1), the sort order remains stable
        var random = System.Random.Shared;
        var playCounts = Enumerable.Range(0, totalCount)
            .Select(i => 1000 + (i * 100))
            .OrderBy(_ => random.Next())
            .ToList();

        // Create directory info map with play count and media type (for efficient lookup)
        // Combine movies and shows, then map each to (playCount, mediaType) tuple
        return movieDirectories.Select(dir => (dir, mediaType: BaseItemKind.Movie))
            .Concat(showDirectories.Select(dir => (dir, mediaType: BaseItemKind.Series)))
            .Select((item, index) => (item.dir, playCount: playCounts[index], item.mediaType))
            .ToDictionary(x => x.dir, x => (x.playCount, x.mediaType));
    }

    /// <summary>
    /// Sets play counts to zero by mapping all directories to play count zero.
    /// </summary>
    /// <returns>A dictionary mapping directory paths to (playCount, mediaType) tuples, or null if no directories found.</returns>
    private async Task<Dictionary<string, (int playCount, BaseItemKind mediaType)>?> playCountZero()
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

        // Create directory info map with play count set to zero and media type (for efficient lookup)
        // Combine movies and shows, then map each to (playCount: 0, mediaType) tuple
        return movieDirectories.Select(dir => (dir, mediaType: BaseItemKind.Movie))
            .Concat(showDirectories.Select(dir => (dir, mediaType: BaseItemKind.Series)))
            .Select(item => (item.dir, playCount: 0, item.mediaType))
            .ToDictionary(x => x.dir, x => (x.playCount, x.mediaType));
    }

    /// <summary>
    /// Applies the play count algorithm to all discover library items across the provided users.
    /// </summary>
    /// <param name="users">List of users to update play counts for</param>
    /// <param name="directoryInfoMap">Dictionary mapping directory paths to play counts and media types</param>
    /// <returns>A tuple containing lists of successes, failures, and skipped items</returns>
    private async Task<(List<(IJellyfinItem item, int playCount)> successes, List<string> failures, List<(IJellyfinItem? item, string path)> skipped)> ApplyPlayCountAlgorithmAsync(
        List<JellyfinUser> users,
        Dictionary<string, (int playCount, BaseItemKind mediaType)> directoryInfoMap)
    {
        var successes = new List<(IJellyfinItem item, int playCount)>();
        var failures = new List<string>();
        var skipped = new List<(IJellyfinItem? item, string path)>();
        
        // Get configuration setting for marking media as played
        var markMediaPlayed = Plugin.GetConfigOrDefault<bool>(nameof(PluginConfiguration.MarkMediaPlayed));

        // Update play count for each item across all users - parallelize by item
        // Sort by play count (ascending) before updating to ensure lower play counts are processed first
        var updateTasks = directoryInfoMap.OrderBy(kvp => kvp.Value.playCount).Select(async kvp =>
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
                    
                    // Update play count for each user - parallelize user updates for this item
                    var userUpdateTasks = users.Select(user => Task.Run(() =>
                    {
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
                    }));

                    // Wait for all user updates for this item to complete
                    await Task.WhenAll(userUpdateTasks);

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
            });

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

