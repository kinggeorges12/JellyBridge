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
        private readonly JellyseerrBridgeService _bridgeService;

        public ConfigurationController(ILoggerFactory loggerFactory, JellyseerrSyncService syncService, JellyseerrBridgeService bridgeService)
        {
            _logger = loggerFactory.CreateLogger<ConfigurationController>();
            _syncService = syncService;
            _bridgeService = bridgeService;
            _logger.LogInformation("[JellyseerrBridge] ConfigurationController initialized");
        }

        [HttpGet("GetPluginConfiguration")]
        public IActionResult GetPluginConfiguration()
        {
            _logger.LogInformation("[JellyseerrBridge] GetPluginConfiguration endpoint called");
            
            try
            {
                var config = Plugin.Instance.Configuration;
                
                // Convert the internal list format to dictionary format for JavaScript
                var configForFrontend = new
                {
                    JellyseerrUrl = config.JellyseerrUrl,
                    ApiKey = config.ApiKey,
                    LibraryDirectory = config.LibraryDirectory,
                    UserId = config.UserId.HasValue ? config.UserId.Value : (int)PluginConfiguration.DefaultValues[nameof(config.UserId)],
                    IsEnabled = config.IsEnabled.HasValue ? config.IsEnabled.Value : (bool)PluginConfiguration.DefaultValues[nameof(config.IsEnabled)],
                    SyncIntervalHours = config.SyncIntervalHours.HasValue ? config.SyncIntervalHours.Value : (int)PluginConfiguration.DefaultValues[nameof(config.SyncIntervalHours)],
                    CreateSeparateLibraries = config.CreateSeparateLibraries ?? (bool)PluginConfiguration.DefaultValues[nameof(config.CreateSeparateLibraries)],
                    LibraryPrefix = config.LibraryPrefix,
                    ExcludeFromMainLibraries = config.ExcludeFromMainLibraries ?? (bool)PluginConfiguration.DefaultValues[nameof(config.ExcludeFromMainLibraries)],
                    AutoSyncOnStartup = config.AutoSyncOnStartup ?? (bool)PluginConfiguration.DefaultValues[nameof(config.AutoSyncOnStartup)],
                    RequestTimeout = config.RequestTimeout.HasValue ? config.RequestTimeout.Value : (int)PluginConfiguration.DefaultValues[nameof(config.RequestTimeout)],
                    RetryAttempts = config.RetryAttempts.HasValue ? config.RetryAttempts.Value : (int)PluginConfiguration.DefaultValues[nameof(config.RetryAttempts)],
                    MaxDiscoverPages = config.MaxDiscoverPages.HasValue ? config.MaxDiscoverPages.Value : (int)PluginConfiguration.DefaultValues[nameof(config.MaxDiscoverPages)],
                    EnableDebugLogging = config.EnableDebugLogging ?? (bool)PluginConfiguration.DefaultValues[nameof(config.EnableDebugLogging)],
                    Region = config.Region,
                    NetworkMap = config.GetNetworkMapDictionary(), // Convert to dictionary for JavaScript
                    DefaultValues = PluginConfiguration.DefaultValues
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
        public IActionResult UpdatePluginConfiguration([FromBody] JsonElement configData)
        {
            _logger.LogInformation("[JellyseerrBridge] UpdatePluginConfiguration endpoint called");
            
            try
            {
                var config = new PluginConfiguration();
                
                // Update configuration properties using simplified helper
                SetValueOrDefault(configData, nameof(config.JellyseerrUrl), config, (string value) => config.JellyseerrUrl = value);
                SetValueOrDefault(configData, nameof(config.ApiKey), config, (string value) => config.ApiKey = value);
                SetValueOrDefault(configData, nameof(config.LibraryDirectory), config, (string value) => config.LibraryDirectory = value);
                SetValueOrDefault(configData, nameof(config.UserId), config, (int value) => config.UserId = value);
                SetValueOrDefault(configData, nameof(config.SyncIntervalHours), config, (int value) => config.SyncIntervalHours = value);
                SetValueOrDefault(configData, nameof(config.LibraryPrefix), config, (string value) => config.LibraryPrefix = value);
                SetValueOrDefault(configData, nameof(config.RequestTimeout), config, (int value) => config.RequestTimeout = value);
                SetValueOrDefault(configData, nameof(config.RetryAttempts), config, (int value) => config.RetryAttempts = value);
                SetValueOrDefault(configData, nameof(config.MaxDiscoverPages), config, (int value) => config.MaxDiscoverPages = value);
                SetValueOrDefault(configData, nameof(config.IsEnabled), config, (bool? value) => config.IsEnabled = value);
                SetValueOrDefault(configData, nameof(config.CreateSeparateLibraries), config, (bool? value) => config.CreateSeparateLibraries = value);
                SetValueOrDefault(configData, nameof(config.ExcludeFromMainLibraries), config, (bool? value) => config.ExcludeFromMainLibraries = value);
                SetValueOrDefault(configData, nameof(config.AutoSyncOnStartup), config, (bool? value) => config.AutoSyncOnStartup = value);
                SetValueOrDefault(configData, nameof(config.EnableDebugLogging), config, (bool? value) => config.EnableDebugLogging = value);
                SetValueOrDefault(configData, nameof(config.Region), config, (string value) => config.Region = value);
                
                // Handle NetworkMap dictionary conversion
                if (configData.TryGetProperty(nameof(config.NetworkMap), out var networkMapElement))
                {
                    var networkDict = new Dictionary<int, string>();
                    
                    foreach (var property in networkMapElement.EnumerateObject())
                    {
                        if (property.Value.ValueKind == JsonValueKind.String && 
                            int.TryParse(property.Name, out int id))
                        {
                            networkDict[id] = property.Value.GetString() ?? string.Empty;
                        }
                    }
                    
                    config.SetNetworkMapDictionary(networkDict);
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
                var result = await _syncService.SyncAsync();
                
                _logger.LogInformation("[JellyseerrBridge] Manual sync completed successfully");
                return Ok(new { 
                    success = result.Success, 
                    message = result.Message,
                    details = result.Details,
                    moviesProcessed = result.MoviesProcessed,
                    moviesCreated = result.MoviesCreated,
                    moviesUpdated = result.MoviesUpdated,
                    tvShowsProcessed = result.TvShowsProcessed,
                    tvShowsCreated = result.TvShowsCreated,
                    tvShowsUpdated = result.TvShowsUpdated,
                    requestsProcessed = result.RequestsProcessed
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

        [HttpGet("Regions")]
        public async Task<IActionResult> GetRegions()
        {
            _logger.LogInformation("[JellyseerrBridge] Regions requested");
            
            try
            {
                var config = Plugin.Instance.Configuration;
                _logger.LogInformation("[JellyseerrBridge] Config - JellyseerrUrl: {Url}, ApiKey: {ApiKey}", 
                    config.JellyseerrUrl, 
                    string.IsNullOrEmpty(config.ApiKey) ? "EMPTY" : "SET");
                
                var regions = await _bridgeService.GetWatchRegionsAsync();
                
                _logger.LogInformation("[JellyseerrBridge] Retrieved {Count} regions", regions?.Count ?? 0);
                
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
                _logger.LogError(ex, "[JellyseerrBridge] Failed to get watch regions");
                return Ok(new { 
                    success = false, 
                    message = $"Failed to get watch regions: {ex.Message}" 
                });
            }
        }

        [HttpGet("Networks")]
        public async Task<IActionResult> GetNetworks([FromQuery] string? region = null)
        {
            _logger.LogInformation("[JellyseerrBridge] Networks requested for region: {Region}", region);
            
            try
            {
                var config = Plugin.Instance.Configuration;
                // Use provided region or default from config
                var targetRegion = region ?? config.Region ?? "US";
                
                var networks = await _bridgeService.GetWatchNetworksAsync(targetRegion);
                
                _logger.LogInformation("[JellyseerrBridge] Retrieved {Count} networks for region {Region}", networks?.Count ?? 0, targetRegion);
                
                return Ok(new { 
                    success = true, 
                    networks = networks 
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[JellyseerrBridge] Failed to get watch networks for region {Region}", region);
                return Ok(new { 
                    success = false, 
                    message = $"Failed to get networks: {ex.Message}" 
                });
            }
        }

        /// <summary>
        /// Gets a value from JsonElement and applies default if null/empty.
        /// </summary>
        /// <typeparam name="T">The type of the property.</typeparam>
        /// <param name="configData">The JSON data.</param>
        /// <param name="propertyName">The name of the property.</param>
        /// <param name="config">The configuration object.</param>
        /// <param name="setter">Action to set the property value.</param>
        private static void SetValueOrDefault<T>(JsonElement configData, string propertyName, object config, Action<T> setter)
        {
            if (!configData.TryGetProperty(propertyName, out var element))
                return;

            if (IsEmptyValue<T>(element))
                return;

            if (typeof(T) == typeof(string))
            {
                var stringValue = element.GetString()!;
                setter((T)(object)stringValue);
            }
            else if (typeof(T) == typeof(int?))
            {
                if (element.ValueKind == JsonValueKind.Null)
                {
                    setter((T)(object)(int?)null!);
                }
                else if (element.ValueKind == JsonValueKind.Number)
                {
                    setter((T)(object)element.GetInt32());
                }
                else if (element.ValueKind == JsonValueKind.String)
                {
                    var stringValue = element.GetString();
                    if (string.IsNullOrWhiteSpace(stringValue))
                    {
                        setter((T)(object)(int?)null!);
                    }
                    else if (int.TryParse(stringValue, out var intValue))
                    {
                        setter((T)(object)intValue);
                    }
                }
            }
            else if (typeof(T) == typeof(bool) || typeof(T) == typeof(bool?))
            {
                setter((T)(object)element.GetBoolean());
            }
        }

        private static bool IsEmptyValue<T>(JsonElement element)
        {
            if (element.ValueKind == JsonValueKind.Undefined)
                return true;

            // For nullable integers, null is a valid value (means "use default")
            if (element.ValueKind == JsonValueKind.Null && typeof(T) == typeof(int?))
                return false;

            if (element.ValueKind == JsonValueKind.Null)
                return true;

            if (element.ValueKind == JsonValueKind.String)
            {
                var stringValue = element.GetString();
                if (string.IsNullOrWhiteSpace(stringValue))
                    return true;

                // For integer types, check if the string can be parsed as an integer
                if (typeof(T) == typeof(int?) && !int.TryParse(stringValue, out _))
                    return true;
            }

            return false;
        }
    }

    public class TestConnectionRequest
    {
        public string JellyseerrUrl { get; set; } = string.Empty;
        public string ApiKey { get; set; } = string.Empty;
    }
}