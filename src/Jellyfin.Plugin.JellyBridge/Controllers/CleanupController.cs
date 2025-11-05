using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Jellyfin.Plugin.JellyBridge.Configuration;
using Jellyfin.Plugin.JellyBridge.Services;
using Jellyfin.Plugin.JellyBridge.BridgeModels;
using Jellyfin.Plugin.JellyBridge.Utils;

namespace Jellyfin.Plugin.JellyBridge.Controllers
{
    [ApiController]
    [Route("JellyBridge")]
    public class CleanupController : ControllerBase
    {
        private readonly DebugLogger<CleanupController> _logger;
        private readonly CleanupService _cleanupService;

        public CleanupController(ILoggerFactory loggerFactory, CleanupService cleanupService)
        {
            _logger = new DebugLogger<CleanupController>(loggerFactory.CreateLogger<CleanupController>());
            _cleanupService = cleanupService;
        }

        [HttpPost("CleanupMetadata")]
        public async Task<IActionResult> CleanupMetadata()
        {
            _logger.LogTrace("Cleanup metadata requested");
            
            try
            {
                // Use Jellyfin-style locking that pauses instead of canceling
                var result = await Plugin.ExecuteWithLockAsync(async () =>
                {
                    var cleanupResult = await _cleanupService.CleanupMetadataAsync();

                    _logger.LogInformation("Cleanup metadata completed: {DeletedCount} items deleted, {FoldersDeleted} folders without metadata", 
                        cleanupResult.ItemsDeleted.Count, cleanupResult.FoldersWithoutMetadataDeleted);

                    return new
                    {
                        result = cleanupResult.ToString(),
                        success = cleanupResult.Success,
                        message = cleanupResult.Message
                    };
                }, _logger, "Cleanup Metadata");

                return Ok(result);
            }
            catch (TimeoutException)
            {
                var taskTimeoutMinutes = Plugin.GetConfigOrDefault<int>(nameof(PluginConfiguration.TaskTimeoutMinutes));
                _logger.LogWarning("Cleanup metadata timed out after {TimeoutMinutes} minutes waiting for lock", taskTimeoutMinutes);
                return StatusCode(408, new { 
                    success = false,
                    error = "Request timeout",
                    message = "Cleanup metadata operation timed out while waiting for lock.",
                    details = $"Operation timed out after {taskTimeoutMinutes} minutes waiting for another operation to complete"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Cleanup metadata failed");
                return StatusCode(500, new { 
                    success = false,
                    error = "Cleanup failed",
                    message = $"Cleanup operation failed: {ex.Message}", 
                    details = $"Exception: {ex.GetType().Name} - {ex.Message}" 
                });
            }
        }
    }
}

