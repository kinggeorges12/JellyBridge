using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Jellyfin.Plugin.JellyseerrBridge.Configuration;
using System.Net.Http;
using System.Text.Json;
using Jellyfin.Plugin.JellyseerrBridge.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Jellyfin.Plugin.JellyseerrBridge.Controllers
{
    [ApiController]
    [Route("JellyseerrBridge")]
    public class ConfigurationController : ControllerBase
    {
        private readonly ILogger<ConfigurationController> _logger;
        private readonly JellyseerrSyncService _syncService;

        public ConfigurationController(ILoggerFactory loggerFactory, JellyseerrSyncService syncService)
        {
            _logger = loggerFactory.CreateLogger<ConfigurationController>();
            _syncService = syncService;
            _logger.LogInformation("[JellyseerrBridge] ConfigurationController initialized");
        }

        [HttpGet("GetConfiguration")]
        public IActionResult GetConfiguration()
        {
            _logger.LogInformation("[JellyseerrBridge] Configuration page accessed - GetConfiguration endpoint called");
            return Ok(new { message = "Configuration page loaded successfully" });
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
                    var authUrl = $"{request.JellyseerrUrl.TrimEnd('/')}/api/v1/auth/me";
                    _logger.LogInformation("[JellyseerrBridge] Testing auth/me endpoint with API key");

                    var requestMessage = new HttpRequestMessage(HttpMethod.Get, authUrl);
                    requestMessage.Headers.Add("X-Api-Key", request.ApiKey);

                    var authResponse = await httpClient.SendAsync(requestMessage);
                    _logger.LogInformation("[JellyseerrBridge] Auth/me endpoint response: {StatusCode}", authResponse.StatusCode);
                    
                    if (!authResponse.IsSuccessStatusCode)
                    {
                        _logger.LogWarning("[JellyseerrBridge] API key authentication failed with status: {StatusCode}", authResponse.StatusCode);
                        return Ok(new { 
                            success = false, 
                            message = $"API key authentication failed with status {authResponse.StatusCode}" 
                        });
                    }

                    // Parse the response to get user info
                    var authResponseContent = await authResponse.Content.ReadAsStringAsync();
                    _logger.LogInformation("[JellyseerrBridge] API key authentication successful. Response: {Response}", authResponseContent);

                    // Test if we can get the list of users (tests API permissions)
                    var usersUrl = $"{request.JellyseerrUrl.TrimEnd('/')}/api/v1/user";
                    _logger.LogInformation("[JellyseerrBridge] Testing user list endpoint to verify API permissions");

                    var usersRequestMessage = new HttpRequestMessage(HttpMethod.Get, usersUrl);
                    usersRequestMessage.Headers.Add("X-Api-Key", request.ApiKey);

                    var usersResponse = await httpClient.SendAsync(usersRequestMessage);
                    _logger.LogInformation("[JellyseerrBridge] User list endpoint response: {StatusCode}", usersResponse.StatusCode);
                    
                    if (usersResponse.IsSuccessStatusCode)
                    {
                        var usersResponseContent = await usersResponse.Content.ReadAsStringAsync();
                        
                        try
                        {
                            // Parse the JSON response to count users
                            var usersData = JsonSerializer.Deserialize<JsonElement>(usersResponseContent);
                            int userCount = 0;
                            
                            _logger.LogDebug("[JellyseerrBridge] Parsing user list response. Root element type: {ElementType}", usersData.ValueKind);
                            
                            // Jellyseerr API returns: {"pageInfo": {"pages":1,"pageSize":10,"results":6,"page":1}, "results": [...]}
                            if (usersData.TryGetProperty("results", out var resultsArray) && resultsArray.ValueKind == JsonValueKind.Array)
                            {
                                userCount = resultsArray.GetArrayLength();
                                _logger.LogDebug("[JellyseerrBridge] Found {UserCount} users in 'results' array", userCount);
                            }
                            // Fallback: if response is a direct array
                            else if (usersData.ValueKind == JsonValueKind.Array)
                            {
                                userCount = usersData.GetArrayLength();
                                _logger.LogDebug("[JellyseerrBridge] Found {UserCount} users in direct array", userCount);
                            }
                            // Fallback: if response is a single user object
                            else if (usersData.ValueKind == JsonValueKind.Object)
                            {
                                userCount = 1;
                                _logger.LogDebug("[JellyseerrBridge] Found single user object");
                            }
                            
                            _logger.LogInformation("[JellyseerrBridge] Successfully retrieved user list. Found {UserCount} users", userCount);
                        }
                        catch (JsonException ex)
                        {
                            _logger.LogWarning("[JellyseerrBridge] Failed to parse user list response: {Error}", ex.Message);
                            _logger.LogInformation("[JellyseerrBridge] Successfully retrieved user list (unable to parse count)");
                        }
                    }
                    else
                    {
                        _logger.LogWarning("[JellyseerrBridge] User list endpoint failed with status: {StatusCode} - API key may have limited permissions", usersResponse.StatusCode);
                    }
                }
                else
                {
                    _logger.LogInformation("[JellyseerrBridge] No API key provided, skipping authentication test");
                }

                _logger.LogInformation("[JellyseerrBridge] Connection test completed successfully");
                return Ok(new { 
                    success = true, 
                    message = "Connection successful! Jellyseerr is reachable and API key is valid.",
                    status = "ok"
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

        [HttpPost("Sync")]
        public async Task<IActionResult> Sync()
        {
            _logger.LogInformation("[JellyseerrBridge] Manual sync requested");
            
            try
            {
                await _syncService.SyncAsync();
                
                _logger.LogInformation("[JellyseerrBridge] Manual sync completed successfully");
                return Ok(new { 
                    success = true, 
                    message = "Sync completed successfully" 
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[JellyseerrBridge] Manual sync failed");
                return Ok(new { 
                    success = false, 
                    message = $"Sync failed: {ex.Message}" 
                });
            }
        }

        [HttpGet("WatchProviderRegions")]
        public async Task<IActionResult> GetWatchProviderRegions()
        {
            _logger.LogInformation("[JellyseerrBridge] Watch provider regions requested");
            
            try
            {
                var config = Plugin.Instance.Configuration;
                _logger.LogInformation("[JellyseerrBridge] Config - JellyseerrUrl: {Url}, ApiKey: {ApiKey}", 
                    config.JellyseerrUrl, 
                    string.IsNullOrEmpty(config.ApiKey) ? "EMPTY" : "SET");
                
                var apiService = HttpContext.RequestServices.GetRequiredService<JellyseerrApiService>();
                
                var regions = await apiService.GetWatchProviderRegionsAsync(config);
                
                _logger.LogInformation("[JellyseerrBridge] Retrieved {Count} watch provider regions", regions?.Count ?? 0);
                
                if (regions == null || regions.Count == 0)
                {
                    _logger.LogWarning("[JellyseerrBridge] No regions returned from API service");
                    return Ok(new { 
                        success = false, 
                        message = "No regions returned from Jellyseerr API",
                        regions = new List<object>()
                    });
                }
                
                return Ok(new { 
                    success = true, 
                    regions = regions 
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[JellyseerrBridge] Failed to get watch provider regions");
                return Ok(new { 
                    success = false, 
                    message = $"Failed to get watch provider regions: {ex.Message}" 
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