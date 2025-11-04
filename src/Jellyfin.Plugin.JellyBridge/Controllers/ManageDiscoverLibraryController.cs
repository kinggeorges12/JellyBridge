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
    public class ManageDiscoverLibraryController : ControllerBase
    {
        private readonly DebugLogger<ManageDiscoverLibraryController> _logger;
        private readonly SyncService _syncService;
        private readonly MetadataService _metadataService;

        public ManageDiscoverLibraryController(ILoggerFactory loggerFactory, SyncService syncService, MetadataService metadataService)
        {
            _logger = new DebugLogger<ManageDiscoverLibraryController>(loggerFactory.CreateLogger<ManageDiscoverLibraryController>());
            _syncService = syncService;
            _metadataService = metadataService;
        }

        /// <summary>
        /// Generate network service folders in the JellyBridge library directory.
        /// </summary>
        [HttpPost("GenerateNetworkFolders")]
        public async Task<IActionResult> GenerateNetworkFolders()
        {
            _logger.LogDebug("GenerateNetworkFolders endpoint called");
            
            try
            {
                // Use Jellyfin-style locking that pauses instead of canceling
                var result = await Plugin.ExecuteWithLockAsync(async () =>
                {
                    var config = Plugin.GetConfiguration();
                    var networkMap = config.NetworkMap ?? new List<JellyseerrNetwork>();
                    
                    if (networkMap.Count == 0)
                    {
                        throw new InvalidOperationException("No networks configured. Please select networks in the Import Discover Content section first.");
                    }
                    
                    // Use MetadataService to create network folders
                    var (createdFolders, existingFolders) = await _metadataService.CreateEmptyNetworkFoldersAsync();
                    
                    var message = $"Successfully processed {networkMap.Count} network(s). {createdFolders.Count} folder(s) created, {existingFolders.Count} folder(s) already existed.";
                    
                    await Task.CompletedTask; // Satisfy async requirement for consistency
                    return new
                    {
                        success = true,
                        message = message,
                        createdFolders = createdFolders,
                        existingFolders = existingFolders,
                        totalNetworks = networkMap.Count
                    };
                }, _logger, "Generate Network Folders");
                
                return Ok(result);
            }
            catch (TimeoutException)
            {
                var taskTimeoutMinutes = Plugin.GetConfigOrDefault<int>(nameof(PluginConfiguration.TaskTimeoutMinutes));
                _logger.LogWarning("Generate network folders timed out after {TimeoutMinutes} minutes waiting for lock", taskTimeoutMinutes);
                return StatusCode(408, new { 
                    success = false,
                    error = "Request timeout",
                    message = "Network folder generation timed out while waiting for lock.",
                    details = $"Operation timed out after {taskTimeoutMinutes} minutes waiting for another operation to complete"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error generating network folders");
                return StatusCode(500, new { 
                    success = false,
                    error = "Failed to generate network folders",
                    message = ex.Message,
                    details = $"Exception type: {ex.GetType().Name}\nStack trace: {ex.StackTrace}"
                });
            }
        }

        [HttpPost("SyncFavorites")]
        public async Task<IActionResult> SyncFavorites()
        {
            _logger.LogDebug("SyncFavorites endpoint called");
            
            try
            {
                // Use Jellyfin-style locking that pauses instead of canceling
                var result = await Plugin.ExecuteWithLockAsync(async () =>
                {
                    _logger.LogTrace("Starting favorites sync to Jellyseerr...");
                    
                    var syncResult = await _syncService.SyncToJellyseerr();
                    await _syncService.ApplyRefreshAsync(syncToResult: syncResult);
                    
                    _logger.LogTrace("Favorites sync completed successfully");
                    _logger.LogDebug("Favorites sync result: {Success} - {Message}", syncResult.Success, syncResult.Message);

                    return new
                    {
                        success = syncResult.Success,
                        message = syncResult.Message,
                        details = syncResult.Details,
                        moviesResult = new
                        {
                            moviesProcessed = syncResult.MoviesResult.Processed,
                            moviesCreated = syncResult.MoviesResult.Created,
                            moviesDeleted = syncResult.MoviesResult.Removed,
                            moviesBlocked = syncResult.MoviesResult.Blocked
                        },
                        showsResult = new
                        {
                            showsProcessed = syncResult.ShowsResult.Processed,
                            showsCreated = syncResult.ShowsResult.Created,
                            showsDeleted = syncResult.ShowsResult.Removed,
                            showsBlocked = syncResult.ShowsResult.Blocked
                        }
                    };
                }, _logger, "Sync to Jellyseerr");
                
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

