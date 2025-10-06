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
        }

        [HttpPost("TestConnection")]
        public async Task<IActionResult> TestConnection([FromBody] TestConnectionRequest request)
        {
            try
            {
                _logger.LogInformation("Testing connection to Jellyseerr at {Url}", request.JellyseerrUrl);

                if (string.IsNullOrEmpty(request.JellyseerrUrl))
                {
                    return BadRequest(new { success = false, message = "Jellyseerr URL is required" });
                }

                // Test basic connectivity
                var statusUrl = $"{request.JellyseerrUrl.TrimEnd('/')}/api/v1/status";
                _logger.LogInformation("Testing status endpoint: {StatusUrl}", statusUrl);

                using var httpClient = new HttpClient();
                var statusResponse = await httpClient.GetAsync(statusUrl);
                
                if (!statusResponse.IsSuccessStatusCode)
                {
                    return Ok(new { 
                        success = false, 
                        message = $"Jellyseerr responded with status {statusResponse.StatusCode}" 
                    });
                }

                // If API key is provided, test authentication
                if (!string.IsNullOrEmpty(request.ApiKey))
                {
                    var userUrl = $"{request.JellyseerrUrl.TrimEnd('/')}/api/v1/user";
                    _logger.LogInformation("Testing user endpoint with API key");

                    var requestMessage = new HttpRequestMessage(HttpMethod.Get, userUrl);
                    requestMessage.Headers.Add("X-Api-Key", request.ApiKey);

                    var userResponse = await httpClient.SendAsync(requestMessage);
                    
                    if (!userResponse.IsSuccessStatusCode)
                    {
                        return Ok(new { 
                            success = false, 
                            message = $"API key authentication failed with status {userResponse.StatusCode}" 
                        });
                    }

                    _logger.LogInformation("API key authentication successful");
                }

                _logger.LogInformation("Connection test successful");
                return Ok(new { 
                    success = true, 
                    message = "Connection successful! Jellyseerr is reachable and API key is valid." 
                });
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError(ex, "HTTP error during connection test");
                return Ok(new { 
                    success = false, 
                    message = $"Connection failed: {ex.Message}" 
                });
            }
            catch (TaskCanceledException ex)
            {
                _logger.LogError(ex, "Timeout during connection test");
                return Ok(new { 
                    success = false, 
                    message = "Connection timed out. Please check the URL and try again." 
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error during connection test");
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