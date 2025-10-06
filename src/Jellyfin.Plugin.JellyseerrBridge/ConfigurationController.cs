using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Jellyfin.Plugin.JellyseerrBridge.Configuration;
using System.Net.Http;
using System.Text.Json;

namespace Jellyfin.Plugin.JellyseerrBridge.Api
{
    [ApiController]
    [Route("JellyseerrBridge")]
    public class ConfigurationController : ControllerBase
    {
        private readonly ILogger<ConfigurationController> _logger;

        public ConfigurationController(ILogger<ConfigurationController> logger)
        {
            _logger = logger;
            _logger.LogInformation("[JellyseerrBridge] ConfigurationController initialized");
        }

        [HttpPost("TestConnection")]
        public async Task<IActionResult> TestConnection([FromBody] TestConnectionRequest request)
        {
            _logger.LogInformation("[JellyseerrBridge] TestConnection endpoint called");
            _logger.LogInformation("[JellyseerrBridge] Request details - URL: {Url}, HasApiKey: {HasApiKey}", 
                request.JellyseerrUrl, !string.IsNullOrEmpty(request.ApiKey));

            try
            {
                if (string.IsNullOrEmpty(request.JellyseerrUrl))
                {
                    _logger.LogWarning("[JellyseerrBridge] TestConnection failed: Jellyseerr URL is required");
                    return BadRequest(new { success = false, message = "Jellyseerr URL is required" });
                }

                // Test basic connectivity
                var statusUrl = $"{request.JellyseerrUrl.TrimEnd('/')}/api/v1/status";
                _logger.LogInformation("[JellyseerrBridge] Testing status endpoint: {StatusUrl}", statusUrl);

                using var httpClient = new HttpClient();
                httpClient.Timeout = TimeSpan.FromSeconds(10); // Set timeout
                
                var statusResponse = await httpClient.GetAsync(statusUrl);
                _logger.LogInformation("[JellyseerrBridge] Status endpoint response: {StatusCode}", statusResponse.StatusCode);
                
                if (!statusResponse.IsSuccessStatusCode)
                {
                    _logger.LogWarning("[JellyseerrBridge] Status endpoint failed with status: {StatusCode}", statusResponse.StatusCode);
                    return Ok(new { 
                        success = false, 
                        message = $"Jellyseerr responded with status {statusResponse.StatusCode}" 
                    });
                }

                _logger.LogInformation("[JellyseerrBridge] Status endpoint test successful");

                // If API key is provided, test authentication
                if (!string.IsNullOrEmpty(request.ApiKey))
                {
                    var userUrl = $"{request.JellyseerrUrl.TrimEnd('/')}/api/v1/user";
                    _logger.LogInformation("[JellyseerrBridge] Testing user endpoint with API key");

                    var requestMessage = new HttpRequestMessage(HttpMethod.Get, userUrl);
                    requestMessage.Headers.Add("X-Api-Key", request.ApiKey);

                    var userResponse = await httpClient.SendAsync(requestMessage);
                    _logger.LogInformation("[JellyseerrBridge] User endpoint response: {StatusCode}", userResponse.StatusCode);
                    
                    if (!userResponse.IsSuccessStatusCode)
                    {
                        _logger.LogWarning("[JellyseerrBridge] API key authentication failed with status: {StatusCode}", userResponse.StatusCode);
                        return Ok(new { 
                            success = false, 
                            message = $"API key authentication failed with status {userResponse.StatusCode}" 
                        });
                    }

                    _logger.LogInformation("[JellyseerrBridge] API key authentication successful");
                }
                else
                {
                    _logger.LogInformation("[JellyseerrBridge] No API key provided, skipping authentication test");
                }

                _logger.LogInformation("[JellyseerrBridge] Connection test completed successfully");
                return Ok(new { 
                    success = true, 
                    message = "Connection successful! Jellyseerr is reachable and API key is valid." 
                });
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError(ex, "[JellyseerrBridge] HTTP error during connection test: {Message}", ex.Message);
                return Ok(new { 
                    success = false, 
                    message = $"Connection failed: {ex.Message}" 
                });
            }
            catch (TaskCanceledException ex)
            {
                _logger.LogError(ex, "[JellyseerrBridge] Timeout during connection test");
                return Ok(new { 
                    success = false, 
                    message = "Connection timed out. Please check the URL and try again." 
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[JellyseerrBridge] Unexpected error during connection test: {Message}", ex.Message);
                return Ok(new { 
                    success = false, 
                    message = $"Unexpected error: {ex.Message}" 
                });
            }
        }
    }

    public class TestConnectionRequest
    {
        public string JellyseerrUrl { get; set; } = string.Empty;
        public string ApiKey { get; set; } = string.Empty;
    }
}