using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Jellyfin.Plugin.JellyBridge.Configuration;
using Jellyfin.Plugin.JellyBridge.Services;
using Jellyfin.Plugin.JellyBridge.BridgeModels;
using Jellyfin.Plugin.JellyBridge.JellyseerrModel;
using Jellyfin.Plugin.JellyBridge.Utils;
using System.Text.Json;

namespace Jellyfin.Plugin.JellyBridge.Controllers
{
    [ApiController]
    [Route("JellyBridge")]
    public class GeneralSettingsController : ControllerBase
    {
        private readonly DebugLogger<GeneralSettingsController> _logger;
        private readonly ApiService _apiService;

        public GeneralSettingsController(ILoggerFactory loggerFactory, ApiService apiService)
        {
            _logger = new DebugLogger<GeneralSettingsController>(loggerFactory.CreateLogger<GeneralSettingsController>());
            _apiService = apiService;
        }

        [HttpPost("TestConnection")]
        public async Task<IActionResult> TestConnection([FromBody] JsonElement requestData)
        {
            _logger.LogDebug("TestConnection endpoint called");
            
            try
            {
                // Extract and validate parameters
                var jellyseerUrl = requestData.TryGetProperty("JellyseerrUrl", out var urlElement) 
                    ? urlElement.GetString() 
                    : null;
                
                var apiKey = requestData.TryGetProperty("ApiKey", out var apiKeyElement) 
                    ? apiKeyElement.GetString() 
                    : null;

                if (string.IsNullOrEmpty(apiKey))
                {
                    _logger.LogWarning("TestConnection failed: Missing required fields");
                    return BadRequest(new { 
                        success = false, 
                        message = "Jellyseerr API Key is required" 
                    });
                }

                var status = await _apiService.TestConnectionAsync(jellyseerUrl, apiKey);
                
                return Ok(new { 
                    success = true, 
                    message = "Connection test successful",
                    details = $"Connected to Jellyseerr v{status.Version}"
                });
            }
            catch (System.TimeoutException ex)
            {
                _logger.LogError(ex, "Connection test timed out");
                return StatusCode(408, new { 
                    success = false, 
                    message = "Connection test timed out",
                    details = ex.Message,
                    errorCode = "TIMEOUT"
                });
            }
            catch (HttpRequestException ex)
            {
                _logger.LogWarning(ex, "Connection test failed: HTTP error");
                return StatusCode(503, new { 
                    success = false, 
                    message = "Jellyseerr service unavailable",
                    details = ex.Message,
                    errorCode = "SERVICE_UNAVAILABLE"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Connection test failed");
                return StatusCode(500, new { 
                    success = false, 
                    message = $"Connection test failed: {ex.Message}",
                    details = ex.Message,
                    errorCode = "CONNECTION_EXCEPTION"
                });
            }
        }
    }
}

