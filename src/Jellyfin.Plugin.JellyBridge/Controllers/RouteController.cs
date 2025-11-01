using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Jellyfin.Plugin.JellyBridge.Configuration;
using System.Net.Http;
using System.Text.Json;
using Jellyfin.Plugin.JellyBridge.Services;
using Microsoft.Extensions.DependencyInjection;
using Jellyfin.Plugin.JellyBridge.BridgeModels;
using Jellyfin.Plugin.JellyBridge.JellyseerrModel;
using Jellyfin.Plugin.JellyBridge.Utils;
using MediaBrowser.Model.Tasks;
using Jellyfin.Plugin.JellyBridge.JellyfinModels;

namespace Jellyfin.Plugin.JellyBridge.Controllers
{
    [ApiController]
    [Route("JellyBridge")]
    public class RouteController : ControllerBase
    {
    private readonly DebugLogger<RouteController> _logger;
    private readonly SyncService _syncService;
    private readonly ApiService _apiService;
    private readonly BridgeService _bridgeService;
    private readonly LibraryService _libraryService;
    private readonly MetadataService _metadataService;
    private readonly ITaskManager _taskManager;

    public RouteController(ILoggerFactory loggerFactory, SyncService syncService, ApiService apiService, BridgeService bridgeService, LibraryService libraryService, MetadataService metadataService, ITaskManager taskManager)
    {
        _logger = new DebugLogger<RouteController>(loggerFactory.CreateLogger<RouteController>());
        _syncService = syncService;
        _apiService = apiService;
        _bridgeService = bridgeService;
        _libraryService = libraryService;
        _metadataService = metadataService;
        _taskManager = taskManager;
    }

        [HttpGet("PluginConfiguration")]
        public IActionResult GetPluginConfiguration()
        {
            _logger.LogTrace("PluginConfiguration GET endpoint called");
            
            try
            {
                var config = Plugin.GetConfiguration();
                
                // Convert the internal list format to dictionary format for JavaScript
                var configForFrontend = new
                {
                    JellyseerrUrl = config.JellyseerrUrl,
                    ApiKey = config.ApiKey,
                    LibraryDirectory = config.LibraryDirectory,
                    IsEnabled = config.IsEnabled,
                    SyncIntervalHours = config.SyncIntervalHours,
                    CreateSeparateLibraries = config.CreateSeparateLibraries,
                    LibraryPrefix = config.LibraryPrefix,
                    ExcludeFromMainLibraries = config.ExcludeFromMainLibraries,
                    RemoveRequestedFromFavorites = config.RemoveRequestedFromFavorites,
                    PluginVersion = Plugin.Instance.GetType().Assembly.GetName().Version?.ToString(),
                    EnableStartupSync = config.EnableStartupSync,
                    StartupDelaySeconds = config.StartupDelaySeconds,
                    TaskTimeoutMinutes = config.TaskTimeoutMinutes,
                    RequestTimeout = config.RequestTimeout,
                    RetryAttempts = config.RetryAttempts,
                    MaxDiscoverPages = config.MaxDiscoverPages,
                    MaxRetentionDays = config.MaxRetentionDays,
                    EnableDebugLogging = config.EnableDebugLogging,
                    EnableTraceLogging = config.EnableTraceLogging,
                    RandomizeDiscoverSortOrder = config.RandomizeDiscoverSortOrder,
                    SortTaskIntervalHours = config.SortTaskIntervalHours,
                    Region = config.Region,
                    NetworkMap = config.NetworkMap,
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

		[HttpPost("PluginConfiguration")]
		public IActionResult UpdatePluginConfiguration([FromBody] JsonElement configData)
        {
            _logger.LogTrace("PluginConfiguration POST endpoint called");
            
            try
            {
                // Use Jellyfin-style locking that pauses instead of canceling
				var result = Plugin.ExecuteWithLockAsync<bool>(() =>
                {

                    // Start from existing configuration so unspecified fields are preserved
                    var config = Plugin.GetConfiguration();

                    // Capture old values BEFORE mutating config
                    var oldEnabled = Plugin.GetConfigOrDefault<bool>(nameof(PluginConfiguration.IsEnabled), config);
                    var oldInterval = Plugin.GetConfigOrDefault<double>(nameof(PluginConfiguration.SyncIntervalHours), config);
                    var oldStartupSync = Plugin.GetConfigOrDefault<bool>(nameof(PluginConfiguration.EnableStartupSync), config);
                    var oldRandomizeSort = Plugin.GetConfigOrDefault<bool>(nameof(PluginConfiguration.RandomizeDiscoverSortOrder), config);
                    var oldRandomizeSortInterval = Plugin.GetConfigOrDefault<double>(nameof(PluginConfiguration.SortTaskIntervalHours), config);

                    // Update configuration properties using simplified helper
                    SetJsonValue<bool?>(configData, nameof(config.IsEnabled), config);
                    SetJsonValue<string>(configData, nameof(config.JellyseerrUrl), config);
                    SetJsonValue<string>(configData, nameof(config.ApiKey), config);
                    SetJsonValue<string>(configData, nameof(config.LibraryDirectory), config);
                    SetJsonValue<double?>(configData, nameof(config.SyncIntervalHours), config);
                    SetJsonValue<string>(configData, nameof(config.LibraryPrefix), config);
                    SetJsonValue<int?>(configData, nameof(config.RequestTimeout), config);
                    SetJsonValue<int?>(configData, nameof(config.RetryAttempts), config);
                    SetJsonValue<int?>(configData, nameof(config.MaxDiscoverPages), config);
                    SetJsonValue<int?>(configData, nameof(config.MaxRetentionDays), config);
                    SetJsonValue<bool?>(configData, nameof(config.CreateSeparateLibraries), config);
                    SetJsonValue<bool?>(configData, nameof(config.ExcludeFromMainLibraries), config);
                    SetJsonValue<bool?>(configData, nameof(config.RemoveRequestedFromFavorites), config);
                    SetJsonValue<bool?>(configData, nameof(config.EnableStartupSync), config);
                    SetJsonValue<int?>(configData, nameof(config.StartupDelaySeconds), config);
                    SetJsonValue<int?>(configData, nameof(config.TaskTimeoutMinutes), config);
                    SetJsonValue<bool?>(configData, nameof(config.EnableDebugLogging), config);
                    SetJsonValue<bool?>(configData, nameof(config.EnableTraceLogging), config);
                    SetJsonValue<bool?>(configData, nameof(config.RandomizeDiscoverSortOrder), config);
                    SetJsonValue<double?>(configData, nameof(config.SortTaskIntervalHours), config);
                    SetJsonValue<string>(configData, nameof(config.Region), config);
					// Handle NetworkMap: support explicit null (reset), or array of JellyseerrNetwork objects
					if (configData.TryGetProperty(nameof(config.NetworkMap), out var networkMapElement))
					{
						if (networkMapElement.ValueKind == JsonValueKind.Null)
						{
							// Explicit reset: set to null so defaults are applied on next GET/UI load
							config.NetworkMap = null;
							_logger.LogInformation("NetworkMap explicitly set to null by client (reset to defaults on next load).");
						}
						else if (networkMapElement.ValueKind == JsonValueKind.Array)
						{
							try
							{
								config.NetworkMap = networkMapElement.Deserialize<List<JellyseerrNetwork>>() ?? new List<JellyseerrNetwork>();
								_logger.LogTrace("Successfully deserialized NetworkMap as array with {Count} networks", config.NetworkMap.Count);
							}
							catch (Exception ex)
							{
								_logger.LogError(ex, "Failed to deserialize NetworkMap as array. JSON: {Json}", networkMapElement.GetRawText());
							}
						}
					}
					else
					{
						// If NetworkMap wasn't provided, preserve existing value; only fall back to defaults if null/empty
						if (config.NetworkMap == null || config.NetworkMap.Count == 0)
						{
							_logger.LogWarning("NetworkMap not provided in payload and existing value is empty. Using defaults.");
							config.NetworkMap = new List<JellyseerrNetwork>((List<JellyseerrNetwork>)PluginConfiguration.DefaultValues[nameof(config.NetworkMap)]);
						}
					}

                    // Compute effective old vs new values (new values AFTER edits)
                    var newEnabled = Plugin.GetConfigOrDefault<bool>(nameof(PluginConfiguration.IsEnabled), config);
                    var newInterval = Plugin.GetConfigOrDefault<double>(nameof(PluginConfiguration.SyncIntervalHours), config);
                    var newStartupSync = Plugin.GetConfigOrDefault<bool>(nameof(PluginConfiguration.EnableStartupSync), config);
                    var newRandomizeSort = Plugin.GetConfigOrDefault<bool>(nameof(PluginConfiguration.RandomizeDiscoverSortOrder), config);
                    var newRandomizeSortInterval = Plugin.GetConfigOrDefault<double>(nameof(PluginConfiguration.SortTaskIntervalHours), config);

                    // Debug snapshot of old vs new
                    _logger.LogDebug("Config snapshot (old): enabled={OldEnabled}, interval={OldInterval}, autoStartup={OldStartupSync}", oldEnabled, oldInterval, oldStartupSync);
                    _logger.LogDebug("Config snapshot (new): enabled={NewEnabled}, interval={NewInterval}, autoStartup={NewStartupSync}", newEnabled, newInterval, newStartupSync);
            
                    // If the scheduled sync configuration changed, stamp when triggers will be reloaded
                    var scheduledChanged = oldEnabled != newEnabled || Math.Abs(oldInterval - newInterval) > double.Epsilon;
                    if (scheduledChanged)
                    {
                        config.ScheduledTaskTimestamp = DateTimeOffset.UtcNow;
                        _logger.LogDebug("ScheduledTaskTimestamp set pre-save: {Timestamp}", config.ScheduledTaskTimestamp);
                    }

                    // Reload triggers selectively based on changed properties
                    try
                    {
                        // Locate our task workers by their keys so we can update triggers precisely without affecting other tasks.
                        // We intentionally avoid reloading all tasks because Jellyfin defers interval tasks until after the trigger reload time + sync interval.
                        // To prevent unnecessary deferrals, we only touch:
                        // - the scheduled sync task when Enabled/Interval changes
                        // - the startup task when EnableStartupSync changes
                        // - the sort task when RandomizeDiscoverSortOrder/SortTaskIntervalHours changes
                        var syncWorker = _taskManager.ScheduledTasks.FirstOrDefault(t => t.ScheduledTask.Key == "JellyBridgeSync");
                        var startupWorker = _taskManager.ScheduledTasks.FirstOrDefault(t => t.ScheduledTask.Key == "JellyBridgeStartup");
                        var randomizeSortWorker = _taskManager.ScheduledTasks.FirstOrDefault(t => t.ScheduledTask.Key == "JellyBridgeSort");

                        // Update scheduled sync task triggers only if IsEnabled or SyncInterval changed
                        if (scheduledChanged &&
                            syncWorker != null && syncWorker.ScheduledTask is Tasks.SyncTask syncTask)
                        {
                            _logger.LogDebug("Reloading sync task triggers due to config change (Enabled/Interval). Old: enabled={OldEnabled}, interval={OldInterval}; New: enabled={NewEnabled}, interval={NewInterval}", oldEnabled, oldInterval, newEnabled, newInterval);
                            
                            var newTriggers = syncTask.GetDefaultTriggers();
                            syncWorker.Triggers = newTriggers.ToList();
                            syncWorker.ReloadTriggerEvents();
                            _logger.LogDebug("Sync task triggers reloaded.");
                        }

                        // Update startup task triggers only if EnableStartupSync changed
                        if (oldStartupSync != newStartupSync && startupWorker != null)
                        {
                            _logger.LogDebug("Reloading startup task triggers due to EnableStartupSync change. Old={Old}, New={New}", oldStartupSync, newStartupSync);
                            // Startup task exposes default triggers; always a startup trigger
                            if (startupWorker.ScheduledTask is Tasks.StartupTask startupTask)
                            {
                                var triggers = startupTask.GetDefaultTriggers();
                                startupWorker.Triggers = triggers.ToList();
                                startupWorker.ReloadTriggerEvents();
                                _logger.LogDebug("Startup task triggers reloaded successfully");
                            }
                        }

                        // Update sort task triggers only if RandomizeDiscoverSortOrder or SortTaskIntervalHours changed
                        var randomizeSortChanged = oldRandomizeSort != newRandomizeSort || Math.Abs(oldRandomizeSortInterval - newRandomizeSortInterval) > double.Epsilon;
                        if (randomizeSortChanged && randomizeSortWorker != null && randomizeSortWorker.ScheduledTask is Tasks.SortTask randomizeSortTask)
                        {
                            _logger.LogDebug("Reloading randomize sort task triggers due to config change. Old: enabled={OldEnabled}, interval={OldInterval}; New: enabled={NewEnabled}, interval={NewInterval}", oldRandomizeSort, oldRandomizeSortInterval, newRandomizeSort, newRandomizeSortInterval);
                            
                            var newTriggers = randomizeSortTask.GetDefaultTriggers();
                            randomizeSortWorker.Triggers = newTriggers.ToList();
                            randomizeSortWorker.ReloadTriggerEvents();
                            _logger.LogDebug("Randomize sort task triggers reloaded.");
                        }
                    
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to reload task triggers");
                    }
                    
                    // Save the configuration
                    Plugin.Instance.UpdateConfiguration(config);
                    
                    _logger.LogDebug("Configuration updated successfully");
                    
                    return Task.FromResult(true);
                }, _logger, "Update Plugin Configuration").GetAwaiter().GetResult();
                
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

        [HttpPost("SyncFavorites")]
        public async Task<IActionResult> SyncFavorites()
        {
            _logger.LogDebug("SyncFavorites endpoint called");
            
            try
            {
                // Use Jellyfin-style locking that pauses instead of canceling
                var result = await Plugin.ExecuteWithLockAsync(async () =>
                {
                    _logger.LogTrace("Starting favorites sync to Jellyseerr...");
                    
                    var syncResult = await _syncService.SyncToJellyseerr();
                    await _syncService.ApplyRefreshAsync(syncToResult: syncResult);
                    
                    _logger.LogTrace("Favorites sync completed successfully");
                    _logger.LogDebug("Favorites sync result: {Success} - {Message}", syncResult.Success, syncResult.Message);

                    return new
                    {
                        success = syncResult.Success,
                        message = syncResult.Message,
                        details = syncResult.Details,
                        moviesResult = new
                        {
                            moviesProcessed = syncResult.MoviesResult.Processed,
                            moviesCreated = syncResult.MoviesResult.Created,
                            moviesDeleted = syncResult.MoviesResult.Removed
                        },
                        showsResult = new
                        {
                            showsProcessed = syncResult.ShowsResult.Processed,
                            showsCreated = syncResult.ShowsResult.Created,
                            showsDeleted = syncResult.ShowsResult.Removed
                        }
                    };
                }, _logger, "Sync to Jellyseerr");
                
                return Ok(result);
            }
            catch (TimeoutException)
            {
                var taskTimeoutMinutes = Plugin.GetConfigOrDefault<int>(nameof(PluginConfiguration.TaskTimeoutMinutes));
                _logger.LogWarning("Favorites sync timed out after {TimeoutMinutes} minutes waiting for lock", taskTimeoutMinutes);
                return StatusCode(408, new { 
                    success = false,
                    error = "Request timeout",
                    message = "Favorites sync operation timed out while waiting for lock.",
                    details = $"Operation timed out after {taskTimeoutMinutes} minutes waiting for another operation to complete"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in favorites sync endpoint");
                return StatusCode(500, new { 
                    success = false,
                    error = "Internal server error", 
                    message = ex.Message,
                    details = $"Exception type: {ex.GetType().Name}\nStack trace: {ex.StackTrace}"
                });
            }
        }

        [HttpPost("SyncDiscover")]
        public async Task<IActionResult> SyncDiscover()
        {
            _logger.LogTrace("Sync discover requested");
            
            try
            {
                // Use Jellyfin-style locking that pauses instead of canceling
                var result = await Plugin.ExecuteWithLockAsync(async () =>
                {
                    var syncResult = await _syncService.SyncFromJellyseerr();
                    await _syncService.ApplyRefreshAsync(syncToResult: null, syncFromResult: syncResult);

                    _logger.LogTrace("Discover sync completed successfully");
                    _logger.LogDebug("Discover sync result: {Success} - {Message}", syncResult.Success, syncResult.Message);

                    return new
                    {
                        message = syncResult.Message,
                        details = syncResult.Details,
                        moviesResult = new {
                            moviesAdded = syncResult.AddedMovies.Count,
                            moviesUpdated = syncResult.UpdatedMovies.Count,
                            moviesDeleted = syncResult.DeletedMovies.Count
                        },
                        showsResult = new {
                            showsAdded = syncResult.AddedShows.Count,
                            showsUpdated = syncResult.UpdatedShows.Count,
                            showsDeleted = syncResult.DeletedShows.Count
                        }
                    };
                }, _logger, "Sync Discover");

                _logger.LogInformation("Sync discover completed");
                return Ok(result);
            }
            catch (TimeoutException)
            {
                var taskTimeoutMinutes = Plugin.GetConfigOrDefault<int>(nameof(PluginConfiguration.TaskTimeoutMinutes));
                _logger.LogWarning("Sync discover timed out after {TimeoutMinutes} minutes waiting for lock", taskTimeoutMinutes);
                return StatusCode(408, new { 
                    success = false,
                    error = "Request timeout",
                    message = "Discover sync operation timed out while waiting for lock.",
                    details = $"Operation timed out after {taskTimeoutMinutes} minutes waiting for another operation to complete"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Sync discover failed");
                return StatusCode(500, new { 
                    message = $"Sync failed: {ex.Message}", 
                    details = $"Sync operation exception: {ex.GetType().Name} - {ex.Message}" 
                });
            }
        }

        [HttpPost("SortLibrary")]
        public async Task<IActionResult> SortLibrary()
        {
            _logger.LogTrace("Sort library requested");
            
            try
            {
                // Use Jellyfin-style locking that pauses instead of canceling
                var result = await Plugin.ExecuteWithLockAsync(async () =>
                {
                    // Randomize play counts for all users to enable random sorting
                    var (successes, failures, skipped) = await _metadataService.RandomizePlayCountAsync();

                    _logger.LogTrace("Sort library completed successfully - {SuccessCount} successes, {FailureCount} failures, {SkippedCount} skipped", successes.Count, failures.Count, skipped.Count);

                    // Build detailed message
                    var detailsBuilder = new System.Text.StringBuilder();
                    detailsBuilder.AppendLine($"Items randomized: {successes.Count}");
                    
                    if (skipped.Count > 0)
                    {
                        detailsBuilder.AppendLine($"Items skipped (ignored): {skipped.Count}");
                    }
                    
                    // Sort successes by playCount (ascending - lowest play count first, which will appear first in sort order)
                    var sortedSuccesses = successes.OrderBy(s => s.playCount).Take(10).ToList();
                    
                    if (sortedSuccesses.Count > 0)
                    {
                        detailsBuilder.AppendLine("\nTop 10 by sort order (lowest play count first):");
                        for (int i = 0; i < sortedSuccesses.Count; i++)
                        {
                            var item = sortedSuccesses[i];
                            detailsBuilder.AppendLine($"  {i + 1}. {item.name} ({item.type}) - Play Count: {item.playCount}");
                        }
                    }
                    
                    if (failures.Count > 0)
                    {
                        detailsBuilder.AppendLine($"\nFailures: {failures.Count}");
                        detailsBuilder.AppendLine("Failed items:");
                        foreach (var failure in failures.Take(10))
                        {
                            detailsBuilder.AppendLine($"  - {failure}");
                        }
                        if (failures.Count > 10)
                        {
                            detailsBuilder.AppendLine($"  ... and {failures.Count - 10} more");
                        }
                    }

                    return new
                    {
                        success = true,
                        message = "Sort library randomization completed successfully",
                        details = detailsBuilder.ToString().TrimEnd()
                    };
                }, _logger, "Sort Library");

                _logger.LogInformation("Sort library completed");
                return Ok(result);
            }
            catch (TimeoutException)
            {
                var taskTimeoutMinutes = Plugin.GetConfigOrDefault<int>(nameof(PluginConfiguration.TaskTimeoutMinutes));
                _logger.LogWarning("Sort library timed out after {TimeoutMinutes} minutes waiting for lock", taskTimeoutMinutes);
                return StatusCode(408, new { 
                    success = false,
                    error = "Request timeout",
                    message = "Sort library operation timed out while waiting for lock.",
                    details = $"Operation timed out after {taskTimeoutMinutes} minutes waiting for another operation to complete"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Sort library failed");
                return StatusCode(500, new { 
                    success = false,
                    error = "Sort library failed",
                    message = $"Sort library operation failed: {ex.Message}", 
                    details = $"Exception: {ex.GetType().Name} - {ex.Message}" 
                });
            }
        }
        
        [HttpGet("Regions")]
        public async Task<IActionResult> GetRegions()
        {
            _logger.LogDebug("Regions requested");
            
            try
            {
                var config = Plugin.GetConfiguration();
                _logger.LogDebug("Config - JellyseerrUrl: {Url}, ApiKey: {ApiKey}", 
                    config.JellyseerrUrl, 
                    string.IsNullOrEmpty(config.ApiKey) ? "EMPTY" : "SET");
                
                var regions = await _apiService.CallEndpointAsync(JellyseerrEndpoint.WatchProvidersRegions, config);
                var typedRegions = (List<JellyseerrWatchProviderRegion>)regions ?? new List<JellyseerrWatchProviderRegion>();
                
                _logger.LogInformation("Retrieved {Count} regions", typedRegions.Count);
                
                if (typedRegions == null || typedRegions.Count == 0)
                {
                    _logger.LogWarning("No regions returned from API service");
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
                _logger.LogError(ex, "Failed to get watch regions");
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
            _logger.LogDebug("Networks requested for region: {Region}", region);
            
            try
            {
                var config = Plugin.GetConfiguration();
                // Use provided region or default from config
                var targetRegion = region ?? Plugin.GetConfigOrDefault<string>(nameof(PluginConfiguration.Region));
                config.Region = targetRegion;
                
                // Get networks for both movies and TV, then combine and deduplicate
                var movieNetworksList = (List<JellyseerrNetwork>)await _apiService.CallEndpointAsync(JellyseerrEndpoint.WatchProvidersMovies, config);
                var showNetworksList = (List<JellyseerrNetwork>)await _apiService.CallEndpointAsync(JellyseerrEndpoint.WatchProvidersTv, config);
                
                _logger.LogTrace("MovieNetworks response type: {Type}, Count: {Count}", 
                    movieNetworksList?.GetType().Name ?? "null", 
                    movieNetworksList?.Count ?? 0);
                
                _logger.LogTrace("showNetworks response type: {Type}, Count: {Count}", 
                    showNetworksList?.GetType().Name ?? "null", 
                    showNetworksList?.Count ?? 0);
                
                // Combine networks and add country
                var combinedNetworks = new List<JellyseerrNetwork>();
                combinedNetworks.AddRange(movieNetworksList ?? new List<JellyseerrNetwork>());
                combinedNetworks.AddRange(showNetworksList ?? new List<JellyseerrNetwork>());
                combinedNetworks.ForEach(network => network.Country = targetRegion);
                
                _logger.LogInformation("Retrieved {Count} networks for region {Region}", combinedNetworks.Count, targetRegion);
                
                return Ok(combinedNetworks);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get watch networks for region {Region}", region);
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
        /// Get the current status of the scheduled sync task.
        /// </summary>
        [HttpGet("TaskStatus")]
        public IActionResult GetTaskStatus()
        {
            _logger.LogTrace("Task status requested");
            
            try
            {
                // Check if any operation is currently running
                var isRunning = Plugin.IsOperationRunning;
                
                // Try to get the scheduled task workers (used only for progress and nextRun interval)
                var syncTaskWrapper = _taskManager.ScheduledTasks.FirstOrDefault(t => t.ScheduledTask.Key == "JellyBridgeSync");
                DateTimeOffset? lastRun;
                DateTimeOffset? nextRun;
                string? lastRunSource; // "Scheduled" or "Startup"

                // Determine last run from TaskManager: consider scheduled and startup tasks
                var startupTaskWrapper = _taskManager.ScheduledTasks.FirstOrDefault(t => t.ScheduledTask.Key == "JellyBridgeStartup");

                // Config flags
                var autoSyncOnStartupEnabled = Plugin.GetConfigOrDefault<bool>(nameof(PluginConfiguration.EnableStartupSync));
                var isPluginEnabled = Plugin.GetConfigOrDefault<bool>(nameof(PluginConfiguration.IsEnabled));
                // Read nullable timestamp directly from configuration as it is nullable
                var scheduledTaskTimestamp = Plugin.GetConfiguration().ScheduledTaskTimestamp;

                // Delegate timestamp calculation to JellyfinModels helper
                (lastRun, lastRunSource, nextRun) = JellyfinTaskTrigger.CalculateTimestamps(
                    syncTaskWrapper,
                    startupTaskWrapper,
                    isPluginEnabled,
                    autoSyncOnStartupEnabled,
                    scheduledTaskTimestamp
                );
                
                // Normalize times to consistent UTC ISO strings for cross-version compatibility (10.10 vs 10.11)
                // Determine status: Disabled takes precedence, then Running, then Idle
                string status;
                string message;
                if (!isPluginEnabled)
                {
                    status = "Disabled";
                    message = "Plugin is disabled";
                }
                else if (isRunning)
                {
                    status = "Running";
                    message = "Sync operation in progress...";
                }
                else
                {
                    status = "Idle";
                    message = "No active sync operation";
                }
                
                var result = new
                {
                    isRunning = isRunning,
                    status = status,
                    progress = syncTaskWrapper?.CurrentProgress,
                    message = message,
                    // Return UTC offsets; frontend will localize
                    lastRun = lastRun,
                    nextRun = nextRun,
                    lastRunSource = lastRunSource
                };
                
                _logger.LogTrace("Task status: {Status}, LastRun: {LastRun}, NextRun: {NextRun}", result.status, lastRun, nextRun);
                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get task status");
                return StatusCode(500, new { 
                    isRunning = false,
                    status = "Error",
                    progress = 0,
                    message = $"Failed to get task status: {ex.Message}",
                    lastRun = (DateTime?)null,
                    nextRun = (DateTime?)null
                });
            }
        }
        
        /// <summary>
        /// Recycle all Jellyseerr library data.
        /// </summary>
        [HttpPost("RecycleLibrary")]
        public async Task<IActionResult> RecycleLibrary()
        {
            _logger.LogDebug("RecycleLibrary endpoint called - recycling library data");
            
            try
            {
                // Get library directory from saved configuration
                var libraryDir = Plugin.GetConfigOrDefault<string>(nameof(PluginConfiguration.LibraryDirectory));
                
                // Use Jellyfin-style locking that pauses instead of canceling
                var success = await Plugin.ExecuteWithLockAsync<bool>(async () =>
                {
                    _logger.LogInformation("Starting data deletion - Library directory: {LibraryDir}", libraryDir);
                    
                    // Delete all contents inside library directory if it exists
                    if (System.IO.Directory.Exists(libraryDir))
                    {
                        _logger.LogTrace("Deleting all contents inside library directory: {LibraryDir}", libraryDir);
                        
                        try
                        {
                            // Get all subdirectories and files
                            var subdirs = System.IO.Directory.GetDirectories(libraryDir);
                            var files = System.IO.Directory.GetFiles(libraryDir);
                            
                            // Delete all files in the root directory
                            foreach (var file in files)
                            {
                                System.IO.File.Delete(file);
                                _logger.LogTrace("Deleted file: {File}", file);
                            }
                            
                            // Delete all subdirectories (recursively)
                            foreach (var subdir in subdirs)
                            {
                                System.IO.Directory.Delete(subdir, true);
                                _logger.LogTrace("Deleted subdirectory: {Subdir}", subdir);
                            }
                            
                            _logger.LogInformation("Successfully deleted all contents inside library directory: {LibraryDir}", libraryDir);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Failed to delete contents of library directory: {LibraryDir}", libraryDir);
                            throw new InvalidOperationException($"Failed to delete contents of library directory: {ex.Message}");
                        }
                    }
                    else
                    {
                        _logger.LogError("Library directory does not exist: {LibraryDir}", libraryDir);
                        throw new InvalidOperationException($"Library directory does not exist: {libraryDir}");
                    }
                    
                    _logger.LogDebug("Data deletion completed successfully");
                    
                    // Refresh the Jellyseerr library after data deletion
                    _logger.LogDebug("Starting Jellyseerr library refresh after data deletion...");
                    
                    // Call the refresh method (fire-and-await, no return value)
                    await _libraryService.RefreshBridgeLibrary(fullRefresh: true, refreshImages: true);

                    _logger.LogInformation("Jellyseerr library refresh initiated");

                    return true;
                }, _logger, "Delete Library Data");
                
                return Ok(new { 
                    success = true, 
                    message = "All Jellyseerr library data has been deleted successfully and library has been refreshed." 
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting library data");
                return StatusCode(500, new { 
                    error = "Failed to delete library data",
                    details = $"Data deletion failed: {ex.GetType().Name} - {ex.Message}",
                    stackTrace = ex.StackTrace
                });
            }
        }

        /// <summary>
        /// Sets a value from JsonElement directly onto the config object.
        /// If a string is null/empty, sets it to string.Empty. Nullable primitives accept null.
        /// </summary>
        /// <typeparam name="T">The type of the property.</typeparam>
        /// <param name="configData">The JSON data.</param>
        /// <param name="propertyName">The name of the property.</param>
        /// <param name="config">The configuration object.</param>
        private static void SetJsonValue<T>(JsonElement configData, string propertyName, object config)
        {
            if (!configData.TryGetProperty(propertyName, out var element))
                return;

            if (IsInvalidValue<T>(element))
                return;

            var property = config.GetType().GetProperty(propertyName);
            if (property == null || !property.CanWrite)
                return;

            object? value = null;

            if (typeof(T) == typeof(string))
            {
                var s = element.ValueKind == JsonValueKind.Null ? null : element.GetString();
                value = string.IsNullOrEmpty(s) ? string.Empty : s;
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
                    var str = element.GetString();
                    value = string.IsNullOrWhiteSpace(str) ? (int?)null : (int.TryParse(str, out var iv) ? iv : (int?)null);
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
                    var str = element.GetString();
                    value = string.IsNullOrWhiteSpace(str) ? (double?)null : (double.TryParse(str, out var dv) ? dv : (double?)null);
                }
            }
            else if (typeof(T) == typeof(bool?))
            {
                if (element.ValueKind == JsonValueKind.Null)
                {
                    value = (bool?)null;
                }
                else if (element.ValueKind == JsonValueKind.True || element.ValueKind == JsonValueKind.False)
                {
                    value = element.GetBoolean();
                }
                else if (element.ValueKind == JsonValueKind.String)
                {
                    var str = element.GetString();
                    value = bool.TryParse(str, out var bv) ? bv : (bool?)null;
                }
            }
            // Always set the property when provided, even if null for nullable types
            property.SetValue(config, value);
        }

        // Returning true here causes the caller to skip setting the value, preserving the
        // previously stored config or its default. We only treat values as non-empty when
        // they are meaningful and parseable for the expected type parameter T.
        // Basically, empty values could be unparseable.
        private static bool IsInvalidValue<T>(JsonElement element)
        {
            // Null JSON value: treat as valid value for all types
            if (element.ValueKind == JsonValueKind.Null)
                return false;

            // Undefined JSON value: treat as invalid value, doesn't change the config
            if (element.ValueKind == JsonValueKind.Undefined)
                return true;

            // String JSON value: apply additional validation rules
            if (element.ValueKind == JsonValueKind.String)
            {
                var stringValue = element.GetString();
                // For nullable integers, only accept strings that parse as integers; otherwise treat as "empty"
                if (typeof(T) == typeof(int?) && !int.TryParse(stringValue, out _))
                    return true;
                
                // For nullable doubles, only accept strings that parse as doubles; otherwise treat as "empty"
                if (typeof(T) == typeof(double?) && !double.TryParse(stringValue, out _))
                    return true;
            }

            return false;
        }
    }
    
}
