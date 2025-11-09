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
            _logger.LogInformation("Running connection test...");
            
            try
            {
                // Extract and validate parameters
                var jellyseerUrl = (requestData.TryGetProperty("JellyseerrUrl", out var jellyseerUrlElement)
                    && !string.IsNullOrWhiteSpace(jellyseerUrlElement.GetString())
                    ? jellyseerUrlElement.GetString()
                    : null)
                    ?? (string)PluginConfiguration.DefaultValues[nameof(PluginConfiguration.JellyseerrUrl)];
                
                var apiKey = requestData.TryGetProperty("ApiKey", out var apiKeyElement)
                    && !string.IsNullOrWhiteSpace(apiKeyElement.GetString())
                    ? apiKeyElement.GetString()
                    : null;

                var libraryDirectory = (requestData.TryGetProperty("LibraryDirectory", out var libraryDirElement)
                    && !string.IsNullOrWhiteSpace(libraryDirElement.GetString())
                    ? libraryDirElement.GetString()
                    : null)
                    ?? (string)PluginConfiguration.DefaultValues[nameof(PluginConfiguration.LibraryDirectory)];

                if (string.IsNullOrEmpty(apiKey))
                {
                    _logger.LogWarning("TestConnection failed: Missing required fields");
                    return BadRequest(new { 
                        success = false, 
                        message = "Jellyseerr API Key is required" 
                    });
                }
                _logger.LogInformation("API Key is not empty");
                
                if (!_libraryService.TestLibraryDirectoryReadWrite(libraryDirectory))
                {
                    _logger.LogWarning("Library directory read/write test failed for: {Directory}", libraryDirectory);
                    return StatusCode(507, new { 
                        success = false, 
                        message = $"The library directory is not accessible: {libraryDirectory}",
                        details = $"The library directory '{libraryDirectory}' cannot be read from or written to. Please check directory permissions and ensure the path is accessible.",
                        errorCode = "INSUFFICIENT_STORAGE"
                    });
                }
                _logger.LogInformation("Library directory test successful for: {Directory}", libraryDirectory);

                var status = await _apiService.TestConnectionAsync(jellyseerUrl, apiKey);
                _logger.LogInformation("Test connection successful to Jellyseerr at: {JellyseerrUrl}", jellyseerUrl);
                
                // Check privileges (UserList endpoint requires user list permissions)
                var testConfig = new PluginConfiguration
                {
                    JellyseerrUrl = jellyseerUrl,
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
                _logger.LogInformation("User list check successful found {Users} users", users.Count);
                
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
            catch (UriFormatException ex)
            {
                // Thrown by TestConnectionAsync when URL format is invalid
                _logger.LogError(ex, "Connection test failed: Invalid URL format");
                return BadRequest(new { 
                    success = false, 
                    message = "Invalid Jellyseerr URL format",
                    details = ex.Message,
                    errorCode = "INVALID_URL"
                });
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
            catch (Exception ex)
            {
                // Catch any unexpected exceptions that would cause a 500
                _logger.LogError(ex, "Connection test failed: Unexpected error");
                return StatusCode(500, new { 
                    success = false, 
                    message = "An unexpected error occurred during connection test",
                    details = ex.Message,
                    errorCode = "INTERNAL_ERROR"
                });
            }
        }
    }
}

