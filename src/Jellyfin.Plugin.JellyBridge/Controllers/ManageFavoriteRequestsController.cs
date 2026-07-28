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
    public class ManageFavoriteRequestsController : ControllerBase
    {
        private readonly DebugLogger<ManageFavoriteRequestsController> _logger;
        private readonly SyncService _syncService;
        private readonly RefreshService _refreshService;

        public ManageFavoriteRequestsController(ILoggerFactory loggerFactory, SyncService syncService, RefreshService refreshService)
        {
            _logger = new DebugLogger<ManageFavoriteRequestsController>(loggerFactory.CreateLogger<ManageFavoriteRequestsController>());
            _syncService = syncService;
            _refreshService = refreshService;
        }

        [HttpPost("SyncFavorites")]
        public async Task<IActionResult> SyncFavorites()
        {
            _logger.LogInformation("Sync favorites requested from plugin configuration page.");
            
            try
            {
                // Use Jellyfin-style locking that pauses instead of canceling
                var result = await Plugin.ExecuteWithLockAsync(async () =>
                {
                    _logger.LogTrace("Starting favorites sync to Jellyseerr...");
                    
                    var syncResult = await _syncService.SyncToJellyseerr();
                    await _refreshService.ApplyRefreshAsync(syncResult);
                    
                    _logger.LogInformation("Sync favorites completed successfully:\n{SyncResult}", syncResult.ToString());

                    return new
                    {
                        result = syncResult.ToString(),
                        success = syncResult.Success,
                        message = syncResult.Message
                    };
                }, _logger, "Sync Favorites");
                
                return Ok(result);
            }
            catch (TimeoutException)
            {
                var taskTimeoutMinutes = Plugin.GetConfigOrDefault<int>(nameof(PluginConfiguration.TaskTimeoutMinutes));
                _logger.LogWarning("Favorites sync timed out after {TimeoutMinutes} minutes waiting for lock", taskTimeoutMinutes);
                return StatusCode(408, new { 
                    success = false,
                    error = "Request timeout",
                    message = "Favorites sync operation timed out while waiting for lock.",
                    details = $"Operation timed out after {taskTimeoutMinutes} minutes waiting for another operation to complete"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in favorites sync endpoint");
                return StatusCode(500, new { 
                    success = false,
                    error = "Internal server error", 
                    message = ex.Message,
                    details = $"Exception type: {ex.GetType().Name}\nStack trace: {ex.StackTrace}"
                });
            }
        }
    }
}

