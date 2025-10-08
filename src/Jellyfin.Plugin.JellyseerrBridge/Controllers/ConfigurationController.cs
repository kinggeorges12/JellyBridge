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

        [HttpGet("GetPluginConfiguration")]
        public IActionResult GetPluginConfiguration()
        {
            _logger.LogInformation("[JellyseerrBridge] GetPluginConfiguration endpoint called");
            
            try
            {
                var config = Plugin.Instance.Configuration;
                
                // Ensure default networks are loaded if ActiveNetworks is empty
                config.EnsureDefaultNetworks();
                
                // Convert the internal list format to dictionary format for JavaScript
                var configForFrontend = new
                {
                    JellyseerrUrl = config.JellyseerrUrl,
                    ApiKey = config.ApiKey,
                    LibraryDirectory = config.LibraryDirectory,
                    UserId = config.UserId,
                    IsEnabled = config.IsEnabled,
                    SyncIntervalHours = config.SyncIntervalHours,
                    CreateSeparateLibraries = config.CreateSeparateLibraries,
                    LibraryPrefix = config.LibraryPrefix,
                    ExcludeFromMainLibraries = config.ExcludeFromMainLibraries,
                    AutoSyncOnStartup = config.AutoSyncOnStartup,
                    RequestTimeout = config.RequestTimeout,
                    RetryAttempts = config.RetryAttempts,
                    EnableDebugLogging = config.EnableDebugLogging,
                    WatchProviderRegion = config.WatchProviderRegion,
                    ActiveNetworks = config.ActiveNetworks,
                    NetworkNameToId = config.GetNetworkNameToIdDictionary(), // Convert to dictionary for JavaScript
                    DefaultNetworks = config.DefaultNetworks
                };
                
                return Ok(configForFrontend);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting plugin configuration");
                return StatusCode(500, new { error = "Failed to get configuration" });
            }
        }

        [HttpPost("UpdatePluginConfiguration")]
        public IActionResult UpdatePluginConfiguration([FromBody] dynamic configData)
        {
            _logger.LogInformation("[JellyseerrBridge] UpdatePluginConfiguration endpoint called");
            
            try
            {
                var config = Plugin.Instance.Configuration;
                
                // Update configuration properties
                config.JellyseerrUrl = configData.JellyseerrUrl?.ToString() ?? config.JellyseerrUrl;
                config.ApiKey = configData.ApiKey?.ToString() ?? config.ApiKey;
                config.LibraryDirectory = configData.LibraryDirectory?.ToString() ?? config.LibraryDirectory;
                config.UserId = configData.UserId != null ? (int)configData.UserId : config.UserId;
                config.IsEnabled = configData.IsEnabled != null ? (bool)configData.IsEnabled : config.IsEnabled;
                config.SyncIntervalHours = configData.SyncIntervalHours != null ? (int)configData.SyncIntervalHours : config.SyncIntervalHours;
                config.CreateSeparateLibraries = configData.CreateSeparateLibraries != null ? (bool)configData.CreateSeparateLibraries : config.CreateSeparateLibraries;
                config.LibraryPrefix = configData.LibraryPrefix?.ToString() ?? config.LibraryPrefix;
                config.ExcludeFromMainLibraries = configData.ExcludeFromMainLibraries != null ? (bool)configData.ExcludeFromMainLibraries : config.ExcludeFromMainLibraries;
                config.AutoSyncOnStartup = configData.AutoSyncOnStartup != null ? (bool)configData.AutoSyncOnStartup : config.AutoSyncOnStartup;
                config.RequestTimeout = configData.RequestTimeout != null ? (int)configData.RequestTimeout : config.RequestTimeout;
                config.RetryAttempts = configData.RetryAttempts != null ? (int)configData.RetryAttempts : config.RetryAttempts;
                config.EnableDebugLogging = configData.EnableDebugLogging != null ? (bool)configData.EnableDebugLogging : config.EnableDebugLogging;
                config.WatchProviderRegion = configData.WatchProviderRegion?.ToString() ?? config.WatchProviderRegion;
                
                // Handle ActiveNetworks array
                if (configData.ActiveNetworks != null)
                {
                    var activeNetworksList = new List<string>();
                    foreach (var network in configData.ActiveNetworks)
                    {
                        activeNetworksList.Add(network.ToString());
                    }
                    config.ActiveNetworks = activeNetworksList;
                }
                
                // Handle NetworkNameToId dictionary conversion
                if (configData.NetworkNameToId != null)
                {
                    var networkDict = new Dictionary<string, int>();
                    
                    // Convert the JavaScript object to C# dictionary
                    var networkNameToIdObj = configData.NetworkNameToId;
                    foreach (var property in networkNameToIdObj)
                    {
                        if (!string.IsNullOrEmpty(property.Name))
                        {
                            var valueStr = property.Value?.ToString();
                            if (int.TryParse(valueStr, out int id))
                            {
                                networkDict[property.Name] = id;
                            }
                        }
                    }
                    
                    config.SetNetworkNameToIdDictionary(networkDict);
                }
                
                // Save the configuration
                Plugin.Instance.UpdateConfiguration(config);
                
                _logger.LogInformation("[JellyseerrBridge] Configuration updated successfully");
                return Ok(new { success = true, message = "Configuration updated successfully" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating plugin configuration");
                return StatusCode(500, new { error = "Failed to update configuration" });
            }
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
                
                var regions = await apiService.GetWatchProviderRegionsAsync();
                
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

        /// <summary>
        /// Update the network name-to-ID mapping in the configuration.
        /// </summary>
        [HttpPost("UpdateNetworkMapping")]
        public async Task<IActionResult> UpdateNetworkMapping()
        {
            try
            {
                var apiService = HttpContext.RequestServices.GetRequiredService<JellyseerrApiService>();
                var config = Plugin.Instance.Configuration;
                
                _logger.LogInformation("Updating network name-to-ID mapping for region: {Region}", config.WatchProviderRegion);
                
                var networks = await apiService.GetNetworksAsync(config.WatchProviderRegion);
                config.SetNetworkNameToIdDictionary(networks.ToDictionary(n => n.Name, n => n.Id));
                
                _logger.LogInformation("Updated network mapping with {Count} networks", config.NetworkNameToId.Count);
                
                return Ok(new { success = true, count = config.NetworkNameToId.Count });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating network mapping");
                return StatusCode(500, new { success = false, error = ex.Message });
            }
        }

        [HttpGet("WatchProviders")]
        public async Task<IActionResult> GetWatchProviders([FromQuery] string region = "US")
        {
            _logger.LogInformation("[JellyseerrBridge] Watch providers requested for region: {Region}", region);
            
            try
            {
                var config = Plugin.Instance.Configuration;
                var apiService = HttpContext.RequestServices.GetRequiredService<JellyseerrApiService>();
                
                var providers = await apiService.GetWatchProvidersAsync(region);
                
                _logger.LogInformation("[JellyseerrBridge] Retrieved {Count} watch providers for region {Region}", providers?.Count ?? 0, region);
                
                return Ok(new { 
                    success = true, 
                    region = region,
                    providers = providers 
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[JellyseerrBridge] Failed to get watch providers for region {Region}", region);
                return Ok(new { 
                    success = false, 
                    message = $"Failed to get watch providers: {ex.Message}" 
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