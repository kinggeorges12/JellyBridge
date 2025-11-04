using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Jellyfin.Plugin.JellyBridge.Configuration;
using Jellyfin.Plugin.JellyBridge.Services;
using Jellyfin.Plugin.JellyBridge.BridgeModels;
using Jellyfin.Plugin.JellyBridge.JellyseerrModel;
using Jellyfin.Plugin.JellyBridge.Utils;

namespace Jellyfin.Plugin.JellyBridge.Controllers
{
    [ApiController]
    [Route("JellyBridge")]
    public class ImportDiscoverContentController : ControllerBase
    {
        private readonly DebugLogger<ImportDiscoverContentController> _logger;
        private readonly ApiService _apiService;
        private readonly SyncService _syncService;

        public ImportDiscoverContentController(ILoggerFactory loggerFactory, ApiService apiService, SyncService syncService)
        {
            _logger = new DebugLogger<ImportDiscoverContentController>(loggerFactory.CreateLogger<ImportDiscoverContentController>());
            _apiService = apiService;
            _syncService = syncService;
        }

        [HttpGet("Regions")]
        public async Task<IActionResult> GetRegions()
        {
            _logger.LogDebug("Regions requested");
            
            try
            {
                var config = Plugin.GetConfiguration();
                _logger.LogDebug("Config - JellyseerrUrl: {Url}, ApiKey: {ApiKey}", 
                    config.JellyseerrUrl, 
                    string.IsNullOrEmpty(config.ApiKey) ? "EMPTY" : "SET");
                
                var regions = await _apiService.CallEndpointAsync(JellyseerrEndpoint.WatchProvidersRegions, config);
                var typedRegions = (List<JellyseerrWatchProviderRegion>)regions ?? new List<JellyseerrWatchProviderRegion>();
                
                _logger.LogInformation("Retrieved {Count} regions", typedRegions.Count);
                
                if (typedRegions == null || typedRegions.Count == 0)
                {
                    _logger.LogWarning("No regions returned from API service");
                    return NotFound(new { 
                        success = false, 
                        message = "No regions returned from Jellyseerr API",
                        details = "The Jellyseerr API returned an empty regions list. This may indicate a configuration issue or API version mismatch.",
                        regions = new List<object>(),
                        errorCode = "NO_REGIONS_FOUND"
                    });
                }
                
                return Ok(new { 
                    success = true, 
                    regions = typedRegions 
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get watch regions");
                return StatusCode(500, new { 
                    success = false, 
                    message = $"Failed to get watch regions: {ex.Message}",
                    details = $"Regions retrieval exception: {ex.GetType().Name} - {ex.Message}",
                    stackTrace = ex.StackTrace,
                    errorCode = "REGIONS_EXCEPTION"
                });
            }
        }

        [HttpGet("Networks")]
        public async Task<IActionResult> GetNetworks([FromQuery] string? region = null)
        {
            _logger.LogDebug("Networks requested for region: {Region}", region);
            
            try
            {
                var config = Plugin.GetConfiguration();
                // Use provided region or default from config
                var targetRegion = region ?? Plugin.GetConfigOrDefault<string>(nameof(PluginConfiguration.Region));
                config.Region = targetRegion;
                
                // Get networks for both movies and TV, then combine and deduplicate
                var movieNetworksList = (List<JellyseerrNetwork>)await _apiService.CallEndpointAsync(JellyseerrEndpoint.WatchProvidersMovies, config);
                var showNetworksList = (List<JellyseerrNetwork>)await _apiService.CallEndpointAsync(JellyseerrEndpoint.WatchProvidersTv, config);
                
                _logger.LogTrace("MovieNetworks response type: {Type}, Count: {Count}", 
                    movieNetworksList?.GetType().Name ?? "null", 
                    movieNetworksList?.Count ?? 0);
                
                _logger.LogTrace("showNetworks response type: {Type}, Count: {Count}", 
                    showNetworksList?.GetType().Name ?? "null", 
                    showNetworksList?.Count ?? 0);
                
                // Combine networks and add country
                var combinedNetworks = new List<JellyseerrNetwork>();
                combinedNetworks.AddRange(movieNetworksList ?? new List<JellyseerrNetwork>());
                combinedNetworks.AddRange(showNetworksList ?? new List<JellyseerrNetwork>());
                combinedNetworks.ForEach(network => network.Country = targetRegion);
                
                _logger.LogInformation("Retrieved {Count} networks for region {Region}", combinedNetworks.Count, targetRegion);
                
                return Ok(combinedNetworks);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get watch networks for region {Region}", region);
                return StatusCode(500, new { 
                    success = false, 
                    message = $"Failed to get networks: {ex.Message}",
                    details = $"Networks retrieval exception for region '{region}': {ex.GetType().Name} - {ex.Message}",
                    stackTrace = ex.StackTrace,
                    errorCode = "NETWORKS_EXCEPTION"
                });
            }
        }

        [HttpPost("SyncDiscover")]
        public async Task<IActionResult> SyncDiscover()
        {
            _logger.LogTrace("Sync discover requested");
            
            try
            {
                // Use Jellyfin-style locking that pauses instead of canceling
                var result = await Plugin.ExecuteWithLockAsync(async () =>
                {
                    var syncResult = await _syncService.SyncFromJellyseerr();
                    await _syncService.ApplyRefreshAsync(syncToResult: null, syncFromResult: syncResult);

                    _logger.LogTrace("Discover sync completed successfully");
                    _logger.LogInformation("Sync discover completed: {0}", syncResult.ToString());

                    return new
                    {
                        result = syncResult.ToString(),
                        success = syncResult.Success,
                        message = syncResult.Message
                    };
                }, _logger, "Sync Discover");

                return Ok(result);
            }
            catch (TimeoutException)
            {
                var taskTimeoutMinutes = Plugin.GetConfigOrDefault<int>(nameof(PluginConfiguration.TaskTimeoutMinutes));
                _logger.LogWarning("Sync discover timed out after {TimeoutMinutes} minutes waiting for lock", taskTimeoutMinutes);
                return StatusCode(408, new { 
                    success = false,
                    error = "Request timeout",
                    message = "Discover sync operation timed out while waiting for lock.",
                    details = $"Operation timed out after {taskTimeoutMinutes} minutes waiting for another operation to complete"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Sync discover failed");
                return StatusCode(500, new { 
                    message = $"Sync failed: {ex.Message}", 
                    details = $"Sync operation exception: {ex.GetType().Name} - {ex.Message}" 
                });
            }
        }
    }
}

