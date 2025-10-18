using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Jellyfin.Plugin.JellyseerrBridge.Configuration;
using System.Net.Http;
using System.Text.Json;
using Jellyfin.Plugin.JellyseerrBridge.Services;
using Microsoft.Extensions.DependencyInjection;
using Jellyfin.Plugin.JellyseerrBridge.BridgeModels;
using Jellyfin.Plugin.JellyseerrBridge.JellyseerrModel;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;

namespace Jellyfin.Plugin.JellyseerrBridge.Controllers
{
    [ApiController]
    [Route("JellyseerrBridge")]
    public class ConfigurationController : ControllerBase
    {
        private readonly ILogger<ConfigurationController> _logger;
        private readonly JellyseerrSyncService _syncService;
        private readonly JellyseerrApiService _apiService;
        private readonly JellyseerrBridgeService _bridgeService;

        public ConfigurationController(ILoggerFactory loggerFactory, JellyseerrSyncService syncService, JellyseerrApiService apiService, JellyseerrBridgeService bridgeService)
        {
            _logger = loggerFactory.CreateLogger<ConfigurationController>();
            _syncService = syncService;
            _apiService = apiService;
            _bridgeService = bridgeService;
            _logger.LogInformation("[JellyseerrBridge] ConfigurationController initialized");
        }


        [HttpPost("TestLibraryScan")]
        public async Task<IActionResult> TestLibraryScan()
        {
            _logger.LogInformation("[JellyseerrBridge] TestLibraryScan endpoint called");
            
            try
            {
                // Use Jellyfin-style locking that pauses instead of canceling
                var result = await Plugin.ExecuteWithLockAsync(async () =>
                {
                    // Add timeout to prevent hanging
                    using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(2));
                    
                    _logger.LogInformation("[JellyseerrBridge] Starting bridge items scan...");
                    var bridgeItems = await _bridgeService.TestLibraryScanAsync();
                    
                    _logger.LogInformation("[JellyseerrBridge] Getting plugin configuration...");
                    var config = Plugin.GetConfiguration();
                    
                    _logger.LogInformation("[JellyseerrBridge] Getting existing movies...");
                    var existingMovies = await _bridgeService.GetExistingItemsAsync<Movie>();
                    
                    _logger.LogInformation("[JellyseerrBridge] Getting existing shows...");
                    var existingShows = await _bridgeService.GetExistingItemsAsync<Series>();
                    
                    _logger.LogInformation("[JellyseerrBridge] Preparing response...");
                    
                    return new
                    {
                        bridgeItems = bridgeItems ?? new List<IJellyseerrItem>(),
                        syncDirectory = config?.LibraryDirectory ?? "Not configured",
                        excludeFromMainLibraries = config?.ExcludeFromMainLibraries ?? true,
                        message = $"Library scan test completed. Found {bridgeItems?.Count ?? 0} bridge items, {existingMovies?.Count ?? 0} movies, {existingShows?.Count ?? 0} shows in main libraries",
                        movieCount = existingMovies?.Count ?? 0,
                        tvShowCount = existingShows?.Count ?? 0,
                        movieIds = existingMovies?.Select(m => m.Id.ToString()).ToList() ?? new List<string>(),
                        tvShowIds = existingShows?.Select(s => s.Id.ToString()).ToList() ?? new List<string>()
                    };
                }, _logger, "Test Library Scan");
                
                return Ok(result);
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning("[JellyseerrBridge] TestLibraryScan timed out after 2 minutes");
                return StatusCode(408, new { 
                    error = "Request timeout",
                    details = "Library scan took too long and was cancelled. This might indicate a very large library or slow storage.",
                    bridgeItems = new List<IJellyseerrItem>(),
                    movieCount = 0,
                    tvShowCount = 0
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[JellyseerrBridge] Error testing library scan");
                return StatusCode(500, new { 
                    error = ex.Message,
                    details = $"Library scan failed: {ex.GetType().Name} - {ex.Message}",
                    stackTrace = ex.StackTrace,
                    bridgeItems = new List<IJellyseerrItem>(),
                    movieCount = 0,
                    tvShowCount = 0
                });
            }
        }

        [HttpPost("TestFavoritesScan")]
        public async Task<IActionResult> TestFavoritesScan()
        {
            _logger.LogInformation("[JellyseerrBridge] TestFavoritesScan endpoint called");
            
            try
            {
                // Use Jellyfin-style locking that pauses instead of canceling
                var result = await Plugin.ExecuteWithLockAsync(async () =>
                {
                    // Add timeout to prevent hanging
                    using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(2));
                    
                    _logger.LogInformation("[JellyseerrBridge] Starting favorites scan...");
                    var scanResult = await _bridgeService.TestFavoritesScanAsync();
                    
                    _logger.LogInformation("[JellyseerrBridge] TestFavoritesScan completed successfully");
                    
                    // Normalize casing for frontend (camelCase)
                    var userFavoritesCamel = scanResult.UserFavorites.Select(u => new
                    {
                        userId = u.UserId,
                        userName = u.UserName,
                        favoriteCount = u.FavoriteCount,
                        favorites = (u.Favorites ?? new List<FavoriteItem>()).Select(f => new
                        {
                            id = f.Id,
                            name = f.Name,
                            type = f.Type,
                            year = f.Year,
                            path = f.Path
                        }).ToList()
                    }).ToList();

                    return new
                    {
                        success = true,
                        message = "Favorites scan test completed successfully",
                        totalUsers = scanResult.TotalUsers,
                        usersWithFavorites = scanResult.UsersWithFavorites,
                        totalFavorites = scanResult.TotalFavorites,
                        userFavorites = userFavoritesCamel
                    };
                }, _logger, "Test Favorites Scan");
                
                return Ok(result);
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning("[JellyseerrBridge] TestFavoritesScan timed out after 2 minutes");
                return StatusCode(408, new { 
                    error = "Request timeout",
                    details = "Favorites scan took too long and was cancelled. This might indicate a very large library or many users.",
                    totalUsers = 0,
                    usersWithFavorites = 0,
                    totalFavorites = 0,
                    userFavorites = new List<UserFavorites>()
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[JellyseerrBridge] Error in TestFavoritesScan endpoint");
                return StatusCode(500, new { 
                    error = "Internal server error", 
                    details = ex.Message,
                    totalUsers = 0,
                    usersWithFavorites = 0,
                    totalFavorites = 0,
                    userFavorites = new List<UserFavorites>()
                });
            }
        }


        [HttpGet("GetPluginConfiguration")]
        public IActionResult GetPluginConfiguration()
        {
            _logger.LogInformation("[JellyseerrBridge] GetPluginConfiguration endpoint called");
            
            try
            {
                var config = Plugin.GetConfiguration();
                
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
                    MaxDiscoverPages = config.MaxDiscoverPages,
                    EnableDebugLogging = config.EnableDebugLogging,
                    Region = config.Region,
                    NetworkMap = config.NetworkMap, // Convert to dictionary for JavaScript
                    DefaultValues = PluginConfiguration.DefaultValues
                };
                
                return Ok(configForFrontend);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting plugin configuration");
                return StatusCode(500, new { 
                    error = "Failed to get configuration",
                    details = $"Configuration retrieval failed: {ex.GetType().Name} - {ex.Message}",
                    stackTrace = ex.StackTrace
                });
            }
        }

        [HttpPost("UpdatePluginConfiguration")]
        public IActionResult UpdatePluginConfiguration([FromBody] JsonElement configData)
        {
            _logger.LogInformation("[JellyseerrBridge] UpdatePluginConfiguration endpoint called");
            
            try
            {
                // Use Jellyfin-style locking that pauses instead of canceling
                Plugin.ExecuteWithLock(() =>
                {
                    var config = new PluginConfiguration();
                    
                    // Update configuration properties using simplified helper
                    SetValueOrDefault<string>(configData, nameof(config.JellyseerrUrl), config);
                    SetValueOrDefault<string>(configData, nameof(config.ApiKey), config);
                    SetValueOrDefault<string>(configData, nameof(config.LibraryDirectory), config);
                    SetValueOrDefault<int?>(configData, nameof(config.UserId), config);
                    SetValueOrDefault<double?>(configData, nameof(config.SyncIntervalHours), config);
                    SetValueOrDefault<string>(configData, nameof(config.LibraryPrefix), config);
                    SetValueOrDefault<int?>(configData, nameof(config.RequestTimeout), config);
                    SetValueOrDefault<int?>(configData, nameof(config.RetryAttempts), config);
                    SetValueOrDefault<int?>(configData, nameof(config.MaxDiscoverPages), config);
                    SetValueOrDefault<bool?>(configData, nameof(config.IsEnabled), config);
                    SetValueOrDefault<bool?>(configData, nameof(config.CreateSeparateLibraries), config);
                    SetValueOrDefault<bool?>(configData, nameof(config.ExcludeFromMainLibraries), config);
                    SetValueOrDefault<bool?>(configData, nameof(config.AutoSyncOnStartup), config);
                    SetValueOrDefault<bool?>(configData, nameof(config.EnableDebugLogging), config);
                    SetValueOrDefault<string>(configData, nameof(config.Region), config);
                    // Handle NetworkMap as array of JellyseerrNetwork objects
                    if (configData.TryGetProperty(nameof(config.NetworkMap), out var networkMapElement) &&
                        networkMapElement.ValueKind == JsonValueKind.Array)
                    {
                        try
                        {
                            config.NetworkMap = networkMapElement.Deserialize<List<JellyseerrNetwork>>() ?? new List<JellyseerrNetwork>();
                            _logger.LogInformation("[JellyseerrBridge] Successfully deserialized NetworkMap as array with {Count} networks", config.NetworkMap.Count);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "[JellyseerrBridge] Failed to deserialize NetworkMap as array. JSON: {Json}", networkMapElement.GetRawText());
                            config.NetworkMap = new List<JellyseerrNetwork>();
                        }
                    }
                    else
                    {
                        _logger.LogWarning("[JellyseerrBridge] NetworkMap is not an array or not found. ValueKind: {ValueKind}, JSON: {Json}", 
                            networkMapElement.ValueKind, config.NetworkMap);
                        config.NetworkMap = new List<JellyseerrNetwork>();
                    }
                    
                    // Save the configuration
                    Plugin.Instance.UpdateConfiguration(config);
                    
                    _logger.LogInformation("[JellyseerrBridge] Configuration updated successfully");
                }, _logger, "Update Plugin Configuration");
                
                return Ok(new { success = true, message = "Configuration updated successfully" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating plugin configuration");
                return StatusCode(500, new { 
                    error = "Failed to update configuration",
                    details = $"Configuration update failed: {ex.GetType().Name} - {ex.Message}",
                    stackTrace = ex.StackTrace
                });
            }
        }

        [HttpPost("TestConnection")]
        public async Task<IActionResult> TestConnection([FromBody] TestConnectionRequest request)
        {
            _logger.LogInformation("[JellyseerrBridge] TestConnection endpoint called");
            _logger.LogInformation("[JellyseerrBridge] Request details - URL: {Url}, HasApiKey: {HasApiKey}, ApiKeyLength: {ApiKeyLength}", 
                request.JellyseerrUrl, !string.IsNullOrEmpty(request.ApiKey), request.ApiKey?.Length ?? 0);

            try
            {
                if (string.IsNullOrEmpty(request.JellyseerrUrl) || string.IsNullOrEmpty(request.ApiKey))
                {
                    _logger.LogWarning("[JellyseerrBridge] TestConnection failed: Missing required fields");
                    return BadRequest(new { success = false, message = "Jellyseerr URL and API Key are required" });
                }

                // Create temporary config for testing
                var testConfig = new PluginConfiguration
                {
                    JellyseerrUrl = request.JellyseerrUrl,
                    ApiKey = request.ApiKey
                };

                _logger.LogInformation("[JellyseerrBridge] Testing connection using JellyseerrApiService");

                // Test basic connectivity using JellyseerrApiService
                var status = (SystemStatus)await _apiService.CallEndpointAsync(JellyseerrEndpoint.Status, testConfig);
                
                _logger.LogInformation("[JellyseerrBridge] Basic connectivity test successful");

                // Test authentication using JellyseerrApiService
                var userInfo = await _apiService.CallEndpointAsync(JellyseerrEndpoint.AuthMe, testConfig);
                _logger.LogInformation("[JellyseerrBridge] AuthMe response type: {Type}, Value: {Value}", userInfo?.GetType().Name ?? "null", userInfo?.ToString() ?? "null");
                
                var typedUserInfo = (JellyseerrUser?)userInfo;
                if (typedUserInfo == null)
                {
                    _logger.LogWarning("[JellyseerrBridge] API key authentication failed - userInfo is null");
                    return Unauthorized(new { 
                        success = false, 
                        message = "API key authentication failed",
                        details = "The provided API key is invalid or does not have sufficient permissions. Verify the API key in Jellyseerr settings.",
                        errorCode = "AUTH_FAILED"
                    });
                }
                
                _logger.LogInformation("[JellyseerrBridge] API key authentication successful for user: {Username}", typedUserInfo.DisplayName ?? typedUserInfo.JellyfinUsername ?? typedUserInfo.Username ?? "Unknown");

                // Test user list permissions using JellyseerrApiService
                var users = (List<JellyseerrUser>)await _apiService.CallEndpointAsync(JellyseerrEndpoint.UserList, testConfig);
                    
                _logger.LogInformation("[JellyseerrBridge] Successfully retrieved user list from object list. Found {UserCount} users", users.Count);

                return Ok(status);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[JellyseerrBridge] Connection test failed with exception");
                return StatusCode(500, new { 
                    success = false, 
                    message = $"Connection test failed: {ex.Message}",
                    details = $"Connection test exception: {ex.GetType().Name} - {ex.Message}",
                    stackTrace = ex.StackTrace,
                    errorCode = "CONNECTION_EXCEPTION"
                });
            }
        }

        [HttpPost("Sync")]
        public async Task<IActionResult> Sync()
        {
            _logger.LogInformation("[JellyseerrBridge] Manual sync requested");
            
            try
            {
                // Use Jellyfin-style locking that pauses instead of canceling
                var result = await Plugin.ExecuteWithLockAsync(async () =>
                {
                    return await _syncService.CreateBridgeFoldersAsync();
                }, _logger, "Manual Sync");
                
                if (result.Success)
                {
                    _logger.LogInformation("[JellyseerrBridge] Manual sync completed successfully");
                    return Ok(new { 
                        message = result.Message, 
                        details = result.Details 
                    });
                }
                else
                {
                    _logger.LogWarning("[JellyseerrBridge] Manual sync failed: {Message}", result.Message);
                    return StatusCode(500, new { 
                        message = result.Message, 
                        details = result.Details 
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[JellyseerrBridge] Manual sync failed");
                return StatusCode(500, new { 
                    message = $"Sync failed: {ex.Message}", 
                    details = $"Sync operation exception: {ex.GetType().Name} - {ex.Message}" 
                });
            }
        }

        [HttpGet("Regions")]
        public async Task<IActionResult> GetRegions()
        {
            _logger.LogInformation("[JellyseerrBridge] Regions requested");
            
            try
            {
                var config = Plugin.GetConfiguration();
                _logger.LogInformation("[JellyseerrBridge] Config - JellyseerrUrl: {Url}, ApiKey: {ApiKey}", 
                    config.JellyseerrUrl, 
                    string.IsNullOrEmpty(config.ApiKey) ? "EMPTY" : "SET");
                
                var regions = await _apiService.CallEndpointAsync(JellyseerrEndpoint.WatchProvidersRegions, config);
                var typedRegions = (List<JellyseerrWatchProviderRegion>)regions ?? new List<JellyseerrWatchProviderRegion>();
                
                _logger.LogInformation("[JellyseerrBridge] Retrieved {Count} regions", typedRegions.Count);
                
                if (typedRegions == null || typedRegions.Count == 0)
                {
                    _logger.LogWarning("[JellyseerrBridge] No regions returned from API service");
                    return NotFound(new { 
                        success = false, 
                        message = "No regions returned from Jellyseerr API",
                        details = "The Jellyseerr API returned an empty regions list. This may indicate a configuration issue or API version mismatch.",
                        regions = new List<object>(),
                        errorCode = "NO_REGIONS_FOUND"
                    });
                }
                
                return Ok(new { 
                    success = true, 
                    regions = typedRegions 
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[JellyseerrBridge] Failed to get watch regions");
                return StatusCode(500, new { 
                    success = false, 
                    message = $"Failed to get watch regions: {ex.Message}",
                    details = $"Regions retrieval exception: {ex.GetType().Name} - {ex.Message}",
                    stackTrace = ex.StackTrace,
                    errorCode = "REGIONS_EXCEPTION"
                });
            }
        }

        [HttpGet("Networks")]
        public async Task<IActionResult> GetNetworks([FromQuery] string? region = null)
        {
            _logger.LogInformation("[JellyseerrBridge] Networks requested for region: {Region}", region);
            
            try
            {
                var config = Plugin.GetConfiguration();
                // Use provided region or default from config
                var targetRegion = region ?? Plugin.GetConfigOrDefault<string>(nameof(PluginConfiguration.Region), config);
                config.Region = targetRegion;
                
                // Get networks for both movies and TV, then combine and deduplicate
                var movieNetworksList = (List<JellyseerrNetwork>)await _apiService.CallEndpointAsync(JellyseerrEndpoint.WatchProvidersMovies, config);
                var showNetworksList = (List<JellyseerrNetwork>)await _apiService.CallEndpointAsync(JellyseerrEndpoint.WatchProvidersTv, config);
                
                _logger.LogInformation("[JellyseerrBridge] MovieNetworks response type: {Type}, Count: {Count}", 
                    movieNetworksList?.GetType().Name ?? "null", 
                    movieNetworksList?.Count ?? 0);
                
                _logger.LogInformation("[JellyseerrBridge] showNetworks response type: {Type}, Count: {Count}", 
                    showNetworksList?.GetType().Name ?? "null", 
                    showNetworksList?.Count ?? 0);
                
                // Combine networks and add country
                var combinedNetworks = new List<JellyseerrNetwork>();
                combinedNetworks.AddRange(movieNetworksList ?? new List<JellyseerrNetwork>());
                combinedNetworks.AddRange(showNetworksList ?? new List<JellyseerrNetwork>());
                combinedNetworks.ForEach(network => network.Country = targetRegion);
                
                _logger.LogInformation("[JellyseerrBridge] Retrieved {Count} networks for region {Region}", combinedNetworks.Count, targetRegion);
                
                return Ok(combinedNetworks);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[JellyseerrBridge] Failed to get watch networks for region {Region}", region);
                return StatusCode(500, new { 
                    success = false, 
                    message = $"Failed to get networks: {ex.Message}",
                    details = $"Networks retrieval exception for region '{region}': {ex.GetType().Name} - {ex.Message}",
                    stackTrace = ex.StackTrace,
                    errorCode = "NETWORKS_EXCEPTION"
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
        private static void SetValueOrDefault<T>(JsonElement configData, string propertyName, object config)
        {
            if (!configData.TryGetProperty(propertyName, out var element))
                return;

            if (IsEmptyValue<T>(element))
                return;

            var property = config.GetType().GetProperty(propertyName);
            if (property == null || !property.CanWrite)
                return;

            object? value = null;

            if (typeof(T) == typeof(string))
            {
                value = element.GetString()!;
            }
            else if (typeof(T) == typeof(int?))
            {
                if (element.ValueKind == JsonValueKind.Null)
                {
                    value = (int?)null;
                }
                else if (element.ValueKind == JsonValueKind.Number)
                {
                    value = element.GetInt32();
                }
                else if (element.ValueKind == JsonValueKind.String)
                {
                    var stringValue = element.GetString();
                    if (string.IsNullOrWhiteSpace(stringValue))
                    {
                        value = (int?)null;
                    }
                    else if (int.TryParse(stringValue, out var intValue))
                    {
                        value = intValue;
                    }
                }
            }
            else if (typeof(T) == typeof(double?))
            {
                if (element.ValueKind == JsonValueKind.Null)
                {
                    value = (double?)null;
                }
                else if (element.ValueKind == JsonValueKind.Number)
                {
                    value = element.GetDouble();
                }
                else if (element.ValueKind == JsonValueKind.String)
                {
                    var stringValue = element.GetString();
                    if (string.IsNullOrWhiteSpace(stringValue))
                    {
                        value = (double?)null;
                    }
                    else if (double.TryParse(stringValue, out var doubleValue))
                    {
                        value = doubleValue;
                    }
                }
            }
            else if (typeof(T) == typeof(bool) || typeof(T) == typeof(bool?))
            {
                value = element.GetBoolean();
            }

            if (value != null)
            {
                property.SetValue(config, value);
            }
        }

        private static bool IsEmptyValue<T>(JsonElement element)
        {
            if (element.ValueKind == JsonValueKind.Undefined)
                return true;

            // For nullable integers and doubles, null is a valid value (means "use default")
            if (element.ValueKind == JsonValueKind.Null && (typeof(T) == typeof(int?) || typeof(T) == typeof(double?)))
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
                
                // For double types, check if the string can be parsed as a double
                if (typeof(T) == typeof(double?) && !double.TryParse(stringValue, out _))
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