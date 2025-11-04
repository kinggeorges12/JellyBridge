using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Jellyfin.Plugin.JellyBridge.Configuration;
using Jellyfin.Plugin.JellyBridge.Services;
using Jellyfin.Plugin.JellyBridge.BridgeModels;
using Jellyfin.Plugin.JellyBridge.JellyseerrModel;
using Jellyfin.Plugin.JellyBridge.Utils;
using System.Text.Json;
using System.Net.Http;

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
                
                // Check privileges (UserList endpoint requires user list permissions)
                _logger.LogInformation("Checking user list privileges");
                var testConfig = new PluginConfiguration
                {
                    JellyseerrUrl = jellyseerUrl ?? Plugin.GetConfigOrDefault<string>(nameof(PluginConfiguration.JellyseerrUrl)),
                    ApiKey = apiKey
                };
                var users = (List<JellyseerrUser>)await _apiService.CallEndpointAsync(JellyseerrEndpoint.UserList, testConfig);
                
                if (users == null || users.Count == 0)
                {
                    _logger.LogWarning("User list check failed - UserList returned null or empty");
                    return StatusCode(403, new { 
                        success = false, 
                        message = "Insufficient privileges to access user list",
                        details = "The API key does not have sufficient permissions to access the user list endpoint.",
                        errorCode = "INSUFFICIENT_PRIVILEGES"
                    });
                }
                
                return Ok(new { 
                    success = true, 
                    message = "Connection test successful",
                    details = $"Connected to Jellyseerr v{status.Version}"
                });
            }
            catch (System.TimeoutException ex)
            {
                // Thrown by MakeApiRequestAsync when all retry attempts timeout
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
                // Thrown by MakeApiRequestAsync on HTTP errors, or by TestConnectionAsync for validation failures
                _logger.LogWarning(ex, "Connection test failed: HTTP error");
                
                var errorMessage = ex.Message;
                int statusCode = 503;
                string errorCode = "SERVICE_UNAVAILABLE";
                string message = "Jellyseerr service unavailable";
                
                // Status code should always be available from MakeApiRequestAsync or TestConnectionAsync
                if (ex.Data.Contains("StatusCode") && ex.Data["StatusCode"] is int httpStatusCode)
                {
                    statusCode = httpStatusCode;
                    
                    // Map HTTP status codes to error codes and messages
                    switch (httpStatusCode)
                    {
                        case 401:
                            errorCode = "AUTH_FAILED";
                            message = "Unauthorized: Invalid API Key";
                            break;
                        case 403:
                            errorCode = "INSUFFICIENT_PRIVILEGES";
                            message = "Forbidden: Insufficient Permissions";
                            break;
                        case 502:
                            errorCode = "INVALID_RESPONSE";
                            message = "Invalid response from Jellyseerr";
                            break;
                        case 503:
                            errorCode = "SERVICE_UNAVAILABLE";
                            message = "Jellyseerr service unavailable";
                            break;
                        default:
                            errorCode = "HTTP_ERROR";
                            message = $"HTTP {httpStatusCode} error";
                            break;
                    }
                }
                else
                {
                    // Fallback if status code is missing (shouldn't happen, but handle gracefully)
                    _logger.LogWarning("HttpRequestException missing StatusCode in Data dictionary");
                    errorCode = "HTTP_ERROR";
                    message = "HTTP error occurred";
                }
                
                return StatusCode(statusCode, new { 
                    success = false, 
                    message = message,
                    details = errorMessage,
                    errorCode = errorCode
                });
            }
            catch (JsonException ex)
            {
                // Thrown by JsonSerializer.Deserialize in TestConnectionAsync
                _logger.LogError(ex, "Connection test failed: Invalid JSON response from Jellyseerr");
                return StatusCode(502, new { 
                    success = false, 
                    message = "Invalid response format from Jellyseerr",
                    details = "Jellyseerr returned an invalid JSON response",
                    errorCode = "INVALID_RESPONSE"
                });
            }
            catch (Exception ex)
            {
                // Catch-all for any other exceptions (InvalidOperationException, ArgumentException, etc.)
                _logger.LogError(ex, "Connection test failed: Unexpected error");
                return StatusCode(500, new { 
                    success = false, 
                    message = $"Connection test failed: {ex.Message}",
                    details = ex.Message,
                    errorCode = "UNEXPECTED_ERROR"
                });
            }
        }
    }
}

