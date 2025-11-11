using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Jellyfin.Plugin.JellyBridge.Configuration;
using System.Text.Json;
using Jellyfin.Plugin.JellyBridge.BridgeModels;
using Jellyfin.Plugin.JellyBridge.JellyseerrModel;
using Jellyfin.Plugin.JellyBridge.Utils;
using Jellyfin.Plugin.JellyBridge.Tasks;
using MediaBrowser.Model.Tasks;
using System.Linq;
using static Jellyfin.Plugin.JellyBridge.BridgeModels.BridgeConfiguration;

namespace Jellyfin.Plugin.JellyBridge.Controllers
{
    [ApiController]
    [Route("JellyBridge")]
    public class PluginConfigurationController : ControllerBase
    {
        private readonly DebugLogger<PluginConfigurationController> _logger;
        private readonly ITaskManager _taskManager;

        public PluginConfigurationController(ILoggerFactory loggerFactory, ITaskManager taskManager)
        {
            _logger = new DebugLogger<PluginConfigurationController>(loggerFactory.CreateLogger<PluginConfigurationController>());
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
                    UseNetworkFolders = config.UseNetworkFolders,
                    AddDuplicateContent = config.AddDuplicateContent,
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
                    PlaceholderDurationSeconds = config.PlaceholderDurationSeconds,
                    EnableDebugLogging = config.EnableDebugLogging,
                    EnableTraceLogging = config.EnableTraceLogging,
                    SortOrder = config.SortOrder,
                    MarkMediaPlayed = config.MarkMediaPlayed,
                    EnableAutomatedSortTask = config.EnableAutomatedSortTask,
                    SortTaskIntervalHours = config.SortTaskIntervalHours,
                    Region = config.Region,
                    NetworkMap = Plugin.GetConfigOrDefault<List<JellyseerrNetwork>>(nameof(PluginConfiguration.NetworkMap), config),
                    ConfigOptions = BridgeConfiguration.ToJson(),
                    ConfigDefaults = PluginConfiguration.DefaultValues
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
                var result = Plugin.ExecuteWithLockAsync<bool>(async () =>
                {
                    // Start from existing configuration so unspecified fields are preserved
                    var config = Plugin.GetConfiguration();

                    // Capture old values BEFORE mutating config
                    var oldEnabled = Plugin.GetConfigOrDefault<bool>(nameof(PluginConfiguration.IsEnabled), config);
                    var oldInterval = Plugin.GetConfigOrDefault<double>(nameof(PluginConfiguration.SyncIntervalHours), config);
                    var oldStartupSync = Plugin.GetConfigOrDefault<bool>(nameof(PluginConfiguration.EnableStartupSync), config);
                    var oldSortOrder = Plugin.GetConfigOrDefault<SortOrderOptions>(nameof(PluginConfiguration.SortOrder), config);
                    var oldRandomizeSortInterval = Plugin.GetConfigOrDefault<double>(nameof(PluginConfiguration.SortTaskIntervalHours), config);

                    // Update configuration properties using simplified helper
                    
                    // General Settings
                    SetJsonValue<bool?>(configData, nameof(config.IsEnabled), config);
                    SetJsonValue<string>(configData, nameof(config.JellyseerrUrl), config);
                    SetJsonValue<string>(configData, nameof(config.ApiKey), config);
                    SetJsonValue<string>(configData, nameof(config.LibraryDirectory), config);
                    SetJsonValue<double?>(configData, nameof(config.SyncIntervalHours), config);
                    SetJsonValue<bool?>(configData, nameof(config.EnableStartupSync), config);
                    
                    // Import Discover Content
                    SetJsonValue<string>(configData, nameof(config.Region), config);
                    SetNetworkMap(configData, config);
                    
                    // Manage Discover Library
                    SetJsonValue<bool?>(configData, nameof(config.ExcludeFromMainLibraries), config);
                    SetJsonValue<bool?>(configData, nameof(config.RemoveRequestedFromFavorites), config);
                    SetJsonValue<bool?>(configData, nameof(config.UseNetworkFolders), config);
                    SetJsonValue<bool?>(configData, nameof(config.AddDuplicateContent), config);
                    SetJsonValue<string>(configData, nameof(config.LibraryPrefix), config);
                    SetJsonValue<bool?>(configData, nameof(config.ManageJellyseerrLibrary), config);
                    
                    // Sort Content
                    SetJsonValue<bool?>(configData, nameof(config.EnableAutomatedSortTask), config);
                    SetJsonValue<SortOrderOptions?>(configData, nameof(config.SortOrder), config);
                    SetJsonValue<bool?>(configData, nameof(config.MarkMediaPlayed), config);
                    SetJsonValue<double?>(configData, nameof(config.SortTaskIntervalHours), config);
                    
                    // Advanced Settings
                    SetJsonValue<int?>(configData, nameof(config.RequestTimeout), config);
                    SetJsonValue<int?>(configData, nameof(config.RetryAttempts), config);
                    SetJsonValue<int?>(configData, nameof(config.MaxDiscoverPages), config);
                    SetJsonValue<int?>(configData, nameof(config.MaxRetentionDays), config);
                    SetJsonValue<int?>(configData, nameof(config.PlaceholderDurationSeconds), config);
                    SetJsonValue<int?>(configData, nameof(config.StartupDelaySeconds), config);
                    SetJsonValue<int?>(configData, nameof(config.TaskTimeoutMinutes), config);
                    SetJsonValue<bool?>(configData, nameof(config.EnableDebugLogging), config);
                    SetJsonValue<bool?>(configData, nameof(config.EnableTraceLogging), config);

                    // Compute effective old vs new values (new values AFTER edits)
                    var newEnabled = Plugin.GetConfigOrDefault<bool>(nameof(PluginConfiguration.IsEnabled), config);
                    var newInterval = Plugin.GetConfigOrDefault<double>(nameof(PluginConfiguration.SyncIntervalHours), config);
                    var newStartupSync = Plugin.GetConfigOrDefault<bool>(nameof(PluginConfiguration.EnableStartupSync), config);
                    var newSortOrder = Plugin.GetConfigOrDefault<SortOrderOptions>(nameof(PluginConfiguration.SortOrder), config);
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
                        // - the sort task when SortOrder/SortTaskIntervalHours changes
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

                        // Update sort task triggers only if SortOrder or SortTaskIntervalHours changed
                        var sortOrderChanged = oldSortOrder != newSortOrder || Math.Abs(oldRandomizeSortInterval - newRandomizeSortInterval) > double.Epsilon;
                        if (sortOrderChanged && randomizeSortWorker != null && randomizeSortWorker.ScheduledTask is Tasks.SortTask randomizeSortTask)
                        {
                            _logger.LogDebug("Reloading sort task triggers due to config change. Old: sortOrder={OldSortOrder}, interval={OldInterval}; New: sortOrder={NewSortOrder}, interval={NewInterval}", oldSortOrder, oldRandomizeSortInterval, newSortOrder, newRandomizeSortInterval);
                            
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
                    
                    await Task.CompletedTask; // Satisfy async requirement for consistency
                    return true;
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

        /// <summary>
        /// Handles NetworkMap property: supports explicit null (reset), array deserialization, or preserves existing value.
        /// </summary>
        /// <param name="configData">The JSON data.</param>
        /// <param name="config">The configuration object.</param>
        private void SetNetworkMap(JsonElement configData, PluginConfiguration config)
        {
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
            else if (typeof(T).IsEnum || (typeof(T).IsGenericType && typeof(T).GetGenericTypeDefinition() == typeof(Nullable<>) && typeof(T).GetGenericArguments()[0].IsEnum))
            {
                // Handle enum types (including nullable enums)
                var enumType = typeof(T).IsGenericType ? typeof(T).GetGenericArguments()[0] : typeof(T);
                
                if (element.ValueKind == JsonValueKind.Null)
                {
                    value = null;
                }
                else if (element.ValueKind == JsonValueKind.Number)
                {
                    var intValue = element.GetInt32();
                    if (Enum.IsDefined(enumType, intValue))
                    {
                        value = Enum.ToObject(enumType, intValue);
                    }
                }
                else if (element.ValueKind == JsonValueKind.String)
                {
                    var str = element.GetString();
                    if (!string.IsNullOrWhiteSpace(str) && Enum.TryParse(enumType, str, true, out var parsedEnum))
                    {
                        value = parsedEnum;
                    }
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
                
                // For nullable enums, only accept strings that parse as the enum; otherwise treat as "empty"
                if (typeof(T).IsGenericType && typeof(T).GetGenericTypeDefinition() == typeof(Nullable<>) && typeof(T).GetGenericArguments()[0].IsEnum)
                {
                    var enumType = typeof(T).GetGenericArguments()[0];
                    if (!Enum.TryParse(enumType, stringValue, true, out _))
                        return true;
                }
            }

            return false;
        }
    }
}

