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
                // Extract parameters from request
                string? jellyseerUrl = null;
                string? apiKey = null;
                
                if (requestData.TryGetProperty("JellyseerrUrl", out var urlElement))
                {
                    jellyseerUrl = urlElement.GetString();
                }
                
                if (requestData.TryGetProperty("ApiKey", out var apiKeyElement))
                {
                    apiKey = apiKeyElement.GetString();
                }
                
                // Fallback to current configuration if not provided
                jellyseerUrl ??= Plugin.GetConfigOrDefault<string>(nameof(PluginConfiguration.JellyseerrUrl));
                
                _logger.LogTrace("Request details - URL: {Url}, HasApiKey: {HasApiKey}, ApiKeyLength: {ApiKeyLength}", 
                    jellyseerUrl, !string.IsNullOrEmpty(apiKey), apiKey?.Length ?? 0);

                if (string.IsNullOrEmpty(jellyseerUrl) || string.IsNullOrEmpty(apiKey))
                {
                    _logger.LogWarning("TestConnection failed: Missing required fields");
                    return BadRequest(new { success = false, message = "Jellyseerr URL and API Key are required" });
                }

                // Create temporary config for testing
                var testConfig = new PluginConfiguration
                {
                    JellyseerrUrl = jellyseerUrl,
                    ApiKey = apiKey
                };

                _logger.LogInformation("Testing connection using ApiService");

                // Test basic connectivity using ApiService
                var status = (SystemStatus)await _apiService.CallEndpointAsync(JellyseerrEndpoint.Status, testConfig);
                if (status == null)
                {
                    _logger.LogWarning("Basic connectivity test failed - Status endpoint returned null");
                    return Unauthorized(new { 
                        success = false, 
                        message = "Authentication failed",
                        details = "The API key is invalid or Jellyseerr is not accessible. Verify the API key and URL.",
                        errorCode = "AUTH_FAILED"
                    });
                }
                
                _logger.LogInformation("Basic connectivity test successful");

                // Test authentication using ApiService
                var userInfo = await _apiService.CallEndpointAsync(JellyseerrEndpoint.AuthMe, testConfig);
                _logger.LogTrace("AuthMe response type: {Type}, Value: {Value}", userInfo?.GetType().Name ?? "null", userInfo?.ToString() ?? "null");
                
                var typedUserInfo = (JellyseerrUser?)userInfo;
                if (typedUserInfo == null)
                {
                    _logger.LogWarning("API key authentication failed - userInfo is null");
                    return StatusCode(503, new { 
                        success = false, 
                        message = "Jellyseerr service unavailable",
                        details = "Unable to authenticate with Jellyseerr service. The service may be down or unreachable.",
                        errorCode = "SERVICE_UNAVAILABLE"
                    });
                }
                
                _logger.LogDebug("API key authentication successful for user: {Username}", typedUserInfo.DisplayName ?? typedUserInfo.JellyfinUsername ?? typedUserInfo.Username ?? "Unknown");

                // Test user list permissions using ApiService
                var users = (List<JellyseerrUser>)await _apiService.CallEndpointAsync(JellyseerrEndpoint.UserList, testConfig);
                if (users == null)
                {
                    _logger.LogWarning("User list test failed - UserList endpoint returned null");
                    return StatusCode(403, new { 
                        success = false, 
                        message = "Insufficient privileges to access user list",
                        details = "The API key does not have sufficient permissions to access the user list endpoint.",
                        errorCode = "INSUFFICIENT_PRIVILEGES"
                    });
                }
                    
                _logger.LogInformation("Successfully retrieved user list from object list. Found {UserCount} users", users.Count);

                if (users.Count == 0)
                {
                    _logger.LogWarning("User list test failed - No users found");
                    return StatusCode(500, new { 
                        success = false, 
                        message = "No users found in Jellyseerr",
                        details = "The API key has access but no users were found. This may indicate a configuration issue in Jellyseerr.",
                        errorCode = "NO_USERS_FOUND"
                    });
                }

                return Ok(new { 
                    success = true, 
                    message = "Connection test successful",
                    details = $"Connected to Jellyseerr v{status.Version}, authenticated as {typedUserInfo.DisplayName ?? typedUserInfo.JellyfinUsername ?? typedUserInfo.Username ?? "Unknown"}, found {users.Count} users"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Connection test failed with exception");
                return StatusCode(500, new { 
                    success = false, 
                    message = $"Connection test failed: {ex.Message}",
                    details = $"Connection test exception: {ex.GetType().Name} - {ex.Message}",
                    stackTrace = ex.StackTrace,
                    errorCode = "CONNECTION_EXCEPTION"
                });
            }
        }
    }
}

