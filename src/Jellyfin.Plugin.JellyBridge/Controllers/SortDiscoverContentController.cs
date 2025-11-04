using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Jellyfin.Plugin.JellyBridge.Configuration;
using Jellyfin.Plugin.JellyBridge.Services;
using Jellyfin.Plugin.JellyBridge.Utils;
using Jellyfin.Plugin.JellyBridge.BridgeModels;
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
                var result = await Plugin.ExecuteWithLockAsync<object?>(async () =>
                {
                    // Sort discover library by updating play counts for all users
                    var sortResult = await _sortService.SortJellyBridge();

                    _logger.LogInformation("Sort library completed: {0}", sortResult.ToString());

                    if (sortResult.Refresh != null) {
                        // Refresh library to reload user data (play counts) - same as SortTask
                        await _libraryService.RefreshBridgeLibrary(refreshUserData: false);
                        _logger.LogInformation("Library refreshed started for Sort Library");
                    }

                    return new
                    {
                        result = sortResult.ToString(),
                        success = sortResult.Success,
                        message = sortResult.Message
                    };
                }, _logger, "Sort Library");

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

