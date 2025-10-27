using Jellyfin.Plugin.JellyseerrBridge.Configuration;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Logging;
using System.Collections.Generic;
using System.Linq;

namespace Jellyfin.Plugin.JellyseerrBridge
{
    public class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages
    {
        private readonly ILogger<Plugin> _logger;
        private readonly ILoggerFactory _loggerFactory;
        private readonly ITaskManager _taskManager;

        public override Guid Id => Guid.Parse("8ecc808c-d6e9-432f-9219-b638fbfb37e6");
        public override string Name => "Jellyseerr Bridge";
        
        public static Plugin Instance { get; private set; } = null!;
        
        public ILoggerFactory LoggerFactory => _loggerFactory;
        
        // Jellyfin-style locking for operations that should be mutually exclusive
        private static readonly object _operationSyncLock = new object();
        private static bool _isOperationRunning = false;
        
        public Plugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer, ILoggerFactory loggerFactory, ITaskManager taskManager) 
            : base(applicationPaths, xmlSerializer)
        {
            _loggerFactory = loggerFactory;
            _logger = loggerFactory.CreateLogger<Plugin>();
            _taskManager = taskManager;
            Instance = this;
            _logger.LogInformation("[JellyseerrBridge] Plugin initialized successfully - Version {Version}", GetType().Assembly.GetName().Version);
            _logger.LogInformation("[JellyseerrBridge] Plugin ID: {PluginId}", Id);
            _logger.LogInformation("[JellyseerrBridge] Plugin Name: {PluginName}", Name);
        }

        /// <summary>
        /// Gets the current plugin configuration, ensuring it's always initialized with defaults.
        /// </summary>
        public static PluginConfiguration GetConfiguration()
        {
            return Instance.Configuration ?? new PluginConfiguration();
        }

        public override void UpdateConfiguration(BasePluginConfiguration configuration)
        {
            _logger.LogTrace("[JellyseerrBridge] Configuration update requested");
            _logger.LogTrace("[JellyseerrBridge] Configuration update method called - this means the save button was clicked!");
            
            var pluginConfig = (PluginConfiguration)configuration;
            _logger.LogDebug("[JellyseerrBridge] Configuration details - Enabled: {Enabled}, URL: {Url}, HasApiKey: {HasApiKey}, LibraryDir: {LibraryDir}, SyncInterval: {SyncInterval}", 
                pluginConfig.IsEnabled, 
                pluginConfig.JellyseerrUrl, 
                !string.IsNullOrEmpty(pluginConfig.ApiKey),
                pluginConfig.LibraryDirectory,
                pluginConfig.SyncIntervalHours ?? (double)PluginConfiguration.DefaultValues[nameof(pluginConfig.SyncIntervalHours)]);
            
            base.UpdateConfiguration(configuration);
            _logger.LogInformation("[JellyseerrBridge] Configuration updated successfully");
            
            // Reload the scheduled task triggers to apply new configuration
            try
            {
                var task = _taskManager.ScheduledTasks.FirstOrDefault(t => t.ScheduledTask.Key == "JellyseerrBridgeSync");
                if (task != null && task.ScheduledTask is Tasks.JellyseerrSyncTask syncTask)
                {
                    _logger.LogDebug("[JellyseerrBridge] Reloading task triggers for new configuration");
                    
                    // Log current configuration values
                    var newSyncInterval = Plugin.GetConfigOrDefault<double>(nameof(PluginConfiguration.SyncIntervalHours));
                    var newAutoSync = Plugin.GetConfigOrDefault<bool>(nameof(PluginConfiguration.AutoSyncOnStartup));
                    _logger.LogDebug("[JellyseerrBridge] New config - SyncInterval: {Interval} hours, AutoSync: {AutoSync}", newSyncInterval, newAutoSync);
                    
                    // Get new triggers from GetDefaultTriggers
                    var newTriggers = syncTask.GetDefaultTriggers();
                    _logger.LogDebug("[JellyseerrBridge] New triggers count: {Count}", newTriggers.Count());
                    
                    // Set the new triggers
                    task.Triggers = newTriggers.ToList();
                    
                    // Reload trigger events
                    task.ReloadTriggerEvents();
                    _logger.LogDebug("[JellyseerrBridge] Task triggers reloaded successfully");
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[JellyseerrBridge] Failed to reload task triggers");
            }
        }

        /// <summary>
        /// Gets a configuration value or its default value.
        /// </summary>
        public static T GetConfigOrDefault<T>(string propertyName, PluginConfiguration? config = null) where T : notnull
        {
            config ??= GetConfiguration();
            var propertyInfo = typeof(PluginConfiguration).GetProperty(propertyName);
            var rawValue = propertyInfo?.GetValue(config);

            // If the value is not null and is of type T, return it
            if (rawValue != null && rawValue is T t &&
                ((rawValue is not string) || (rawValue is string str && !string.IsNullOrEmpty(str)))) {
                return t;
            }

            // Try to get default value from dictionary
            if (PluginConfiguration.DefaultValues?.TryGetValue(propertyName, out var defaultValue) == true) {
                return (T)defaultValue;
            }
            
            // Return default value for the type
            throw new InvalidOperationException($"Cannot provide default value for type {typeof(T)}");
        }

        /// <summary>
        /// Sets the RanFirstTime flag in the configuration.
        /// </summary>
        public static void SetRanFirstTime(bool value)
        {
            var config = GetConfiguration();
            config.RanFirstTime = value;
            Instance.UpdateConfiguration(config);
        }

        /// <summary>
        /// Gets the plugin pages. Required for Jellyfin plugin system.
        /// </summary>
        public IEnumerable<PluginPageInfo> GetPages()
        {
            return new[]
            {
                new PluginPageInfo
                {
                    Name = Name,
                    EmbeddedResourcePath = GetType().Namespace + ".Configuration.ConfigurationPage.html",
                },
                new PluginPageInfo
                {
                    Name = "ConfigurationPage.js",
                    EmbeddedResourcePath = GetType().Namespace + ".Configuration.ConfigurationPage.js"
                }
            };
        }

        /// <summary>
        /// Checks if any operation is currently running.
        /// </summary>
        public static bool IsOperationRunning => _isOperationRunning;

        /// <summary>
        /// Executes an operation with Jellyfin-style locking that pauses instead of canceling.
        /// </summary>
        public static async Task<T> ExecuteWithLockAsync<T>(Func<Task<T>> operation, ILogger logger, string operationName, TimeSpan? timeout = null)
        {
            timeout ??= TimeSpan.FromMinutes(1); // Default 1 minute timeout
            var startTime = DateTime.UtcNow;
            
            // Wait for any running operation to complete (pausing, not canceling)
            while (DateTime.UtcNow - startTime < timeout.Value)
            {
                lock (_operationSyncLock)
                {
                    if (!_isOperationRunning)
                    {
                        _isOperationRunning = true;
                        logger.LogTrace("Acquiring operation lock for {OperationName}", operationName);
                        break;
                    }
                }
                
                logger.LogWarning("Another operation is running, pausing {OperationName} until it completes", operationName);
                await Task.Delay(100); // Small delay to prevent busy waiting
            }
            
            // Check if we timed out
            if (DateTime.UtcNow - startTime >= timeout.Value)
            {
                throw new TimeoutException($"Operation '{operationName}' timed out after {timeout.Value.TotalMinutes} minutes waiting for lock");
            }
            
            try
            {
                return await operation();
            }
            finally
            {
                lock (_operationSyncLock)
                {
                    _isOperationRunning = false;
                    logger.LogTrace("Releasing operation lock for {OperationName}", operationName);
                }
            }
        }

    }
}