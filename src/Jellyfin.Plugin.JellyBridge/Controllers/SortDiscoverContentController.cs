using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Jellyfin.Plugin.JellyBridge.Configuration;
using Jellyfin.Plugin.JellyBridge.Services;
using Jellyfin.Plugin.JellyBridge.Utils;
using System.Linq;

namespace Jellyfin.Plugin.JellyBridge.Controllers
{
    [ApiController]
    [Route("JellyBridge")]
    public class SortDiscoverContentController : ControllerBase
    {
        private readonly DebugLogger<SortDiscoverContentController> _logger;
        private readonly SortService _sortService;
        private readonly LibraryService _libraryService;

        public SortDiscoverContentController(ILoggerFactory loggerFactory, SortService sortService, LibraryService libraryService)
        {
            _logger = new DebugLogger<SortDiscoverContentController>(loggerFactory.CreateLogger<SortDiscoverContentController>());
            _sortService = sortService;
            _libraryService = libraryService;
        }

        [HttpPost("SortLibrary")]
        public async Task<IActionResult> SortLibrary()
        {
            _logger.LogTrace("Sort library requested");
            
            try
            {
                // Use Jellyfin-style locking that pauses instead of canceling
                var result = await Plugin.ExecuteWithLockAsync(async () =>
                {
                    // Randomize play counts for all users to enable random sorting
                    var (successes, failures, skipped) = await _sortService.ApplyPlayCountAlgorithmAsync();

                    _logger.LogTrace("Sort library completed successfully - {SuccessCount} successes, {FailureCount} failures, {SkippedCount} skipped", successes.Count, failures.Count, skipped.Count);

                    if (successes.Count > 0) {
                        // Refresh library to reload user data (play counts) - same as SortTask
                        await _libraryService.RefreshBridgeLibrary(refreshUserData: false);
                        _logger.LogInformation("Library refreshed started for Sort Library");
                    }

                    // Build detailed message
                    var detailsBuilder = new System.Text.StringBuilder();
                    detailsBuilder.AppendLine($"Items randomized: {successes.Count}");
                    
                    if (skipped.Count > 0)
                    {
                        detailsBuilder.AppendLine($"Items skipped (ignored): {skipped.Count}");
                    }
                    
                    // Sort successes by playCount (ascending - lowest play count first, which will appear first in sort order)
                    var sortedSuccesses = successes.OrderBy(s => s.playCount).Take(10).ToList();
                    
                    if (sortedSuccesses.Count > 0)
                    {
                        detailsBuilder.AppendLine("\nTop 10 by sort order (lowest play count first):");
                        for (int i = 0; i < sortedSuccesses.Count; i++)
                        {
                            var item = sortedSuccesses[i];
                            detailsBuilder.AppendLine($"  {i + 1}. {item.name} ({item.type}) - Play Count: {item.playCount}");
                        }
                    }
                    
                    if (failures.Count > 0)
                    {
                        detailsBuilder.AppendLine($"\nFailures: {failures.Count}");
                        detailsBuilder.AppendLine("Failed items:");
                        foreach (var failure in failures.Take(10))
                        {
                            detailsBuilder.AppendLine($"  - {failure}");
                        }
                        if (failures.Count > 10)
                        {
                            detailsBuilder.AppendLine($"  ... and {failures.Count - 10} more");
                        }
                    }

                    return new
                    {
                        success = true,
                        message = "Sort library randomization completed successfully",
                        details = detailsBuilder.ToString().TrimEnd()
                    };
                }, _logger, "Sort Library");

                _logger.LogInformation("Sort library completed");
                return Ok(result);
            }
            catch (TimeoutException)
            {
                var taskTimeoutMinutes = Plugin.GetConfigOrDefault<int>(nameof(PluginConfiguration.TaskTimeoutMinutes));
                _logger.LogWarning("Sort library timed out after {TimeoutMinutes} minutes waiting for lock", taskTimeoutMinutes);
                return StatusCode(408, new { 
                    success = false,
                    error = "Request timeout",
                    message = "Sort library operation timed out while waiting for lock.",
                    details = $"Operation timed out after {taskTimeoutMinutes} minutes waiting for another operation to complete"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Sort library failed");
                return StatusCode(500, new { 
                    success = false,
                    error = "Sort library failed",
                    message = $"Sort library operation failed: {ex.Message}", 
                    details = $"Exception: {ex.GetType().Name} - {ex.Message}" 
                });
            }
        }
    }
}

