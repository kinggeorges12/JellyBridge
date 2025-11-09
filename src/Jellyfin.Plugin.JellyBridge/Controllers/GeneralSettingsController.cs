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
        private readonly LibraryService _libraryService;

        public GeneralSettingsController(ILoggerFactory loggerFactory, ApiService apiService, LibraryService libraryService)
        {
            _logger = new DebugLogger<GeneralSettingsController>(loggerFactory.CreateLogger<GeneralSettingsController>());
            _apiService = apiService;
            _libraryService = libraryService;
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

                var libraryDirectory = requestData.TryGetProperty("LibraryDirectory", out var libraryDirElement) 
                    ? libraryDirElement.GetString() 
                    : null;

                if (string.IsNullOrEmpty(apiKey))
                {
                    _logger.LogWarning("TestConnection failed: Missing required fields");
                    return BadRequest(new { 
                        success = false, 
                        message = "Jellyseerr API Key is required" 
                    });
                }

                // Test library directory read/write access only if a directory is provided or configured
                var effectiveLibraryDirectory = !string.IsNullOrEmpty(libraryDirectory)
                    ? libraryDirectory
                    : Plugin.GetConfigOrDefault<string>(nameof(PluginConfiguration.LibraryDirectory));
                
                if (!_libraryService.TestLibraryDirectoryReadWrite(effectiveLibraryDirectory))
                {
                    _logger.LogWarning("Library directory read/write test failed for: {Directory}", effectiveLibraryDirectory);
                    return StatusCode(507, new { 
                        success = false, 
                        message = $"The library directory is not accessible: {effectiveLibraryDirectory}",
                        details = $"The library directory '{effectiveLibraryDirectory}' cannot be read from or written to. Please check directory permissions and ensure the path is accessible.",
                        errorCode = "INSUFFICIENT_STORAGE"
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
                
                // Check UserList endpoint - if it returns empty/null after successful connection, likely insufficient privileges
                var users = (List<JellyseerrUser>)await _apiService.CallEndpointAsync(JellyseerrEndpoint.UserList, testConfig);
                
                if (users == null || users.Count == 0)
                {
                    _logger.LogWarning("User list check returned empty list - likely insufficient privileges");
                    return StatusCode(403, new { 
                        success = false, 
                        message = "API key lacks required permissions",
                        details = "The API key cannot access the user list. Ensure the API key has user management permissions in Jellyseerr.",
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
                    message = "Connection to Jellyseerr timed out",
                    details = "The request to Jellyseerr took too long to respond. Check that Jellyseerr is running and the URL is correct.",
                    errorCode = "TIMEOUT"
                });
            }
            catch (HttpRequestException ex)
            {
                // Thrown by MakeApiRequestAsync on HTTP errors, or by TestConnectionAsync for validation failures
                _logger.LogWarning(ex, "Connection test failed: HTTP error");
                
                var errorMessage = ex.Message;
                
                // Check if status code is available
                if (ex.Data.Contains("StatusCode") && ex.Data["StatusCode"] is int httpStatusCode)
                {
                    int statusCode = httpStatusCode;
                    string errorCode;
                    string message;
                    
                    // Map HTTP status codes to error codes and messages
                    switch (httpStatusCode)
                    {
                        case 401:
                            errorCode = "AUTH_FAILED";
                            message = "API authentication failed";
                            break;
                        case 403:
                            errorCode = "INSUFFICIENT_PRIVILEGES";
                            message = "API Key is not valid";
                            break;
                        case 502:
                            errorCode = "INVALID_RESPONSE";
                            message = "Jellyseerr returned an invalid response";
                            break;
                        case 503:
                            errorCode = "SERVICE_UNAVAILABLE";
                            message = "Jellyseerr server is unavailable";
                            break;
                        default:
                            errorCode = "HTTP_ERROR";
                            message = $"Connection failed with error {httpStatusCode}";
                            break;
                    }
                    
                    return StatusCode(statusCode, new { 
                        success = false, 
                        message = message,
                        details = errorMessage,
                        errorCode = errorCode
                    });
                }
                else
                {
                    // No status code - connection error or unexpected exception (unreachable endpoint)
                    // This happens when connection is refused or endpoint is unreachable (no HTTP response)
                    _logger.LogWarning("HttpRequestException missing StatusCode - connection error or unexpected exception: {Error}", errorMessage);
                    return StatusCode(503, new { 
                        success = false, 
                        message = "Jellyseerr URL is unreachable from Jellyfin",
                        details = errorMessage,
                        errorCode = "SERVICE_UNAVAILABLE"
                    });
                }
            }
            catch (JsonException ex)
            {
                // Thrown by JsonSerializer.Deserialize in TestConnectionAsync
                _logger.LogError(ex, "Connection test failed: Invalid JSON response from Jellyseerr");
                return StatusCode(502, new { 
                    success = false, 
                    message = "Unable to parse Jellyseerr response",
                    details = ex.Message,
                    errorCode = "INVALID_RESPONSE"
                });
            }
        }
    }
}

