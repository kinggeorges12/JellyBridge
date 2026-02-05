using Jellyfin.Plugin.JellyBridge.Configuration;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;
using Jellyfin.Plugin.JellyBridge.JellyfinModels;
using Jellyfin.Plugin.JellyBridge.Utils;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Logging;
using System.Collections.Generic;
using System.Linq;

namespace Jellyfin.Plugin.JellyBridge
{
    public class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages
    {
        private readonly DebugLogger<Plugin> _logger;
        private readonly ILoggerFactory _loggerFactory;
        private readonly ITaskManager _taskManager;

        public override Guid Id => Guid.Parse("8ecc808c-d6e9-432f-9219-b638fbfb37e6");
        public override string Name => "JellyBridge";

        public static Plugin Instance { get; private set; } = null!;

        public ILoggerFactory LoggerFactory => _loggerFactory;

        // Locking: Only one operation (any name) can run at a time, but allow one queued per operation name
        private static readonly object _operationSyncLock = new object();
        private static bool _isOperationRunning = false;
        private static readonly Dictionary<string, bool> _isOperationQueuedByName = new Dictionary<string, bool>();

        public Plugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer, ILoggerFactory loggerFactory, ITaskManager taskManager)
            : base(applicationPaths, xmlSerializer)
        {
            _loggerFactory = loggerFactory;
            _logger = new DebugLogger<Plugin>(loggerFactory.CreateLogger<Plugin>());
            _taskManager = taskManager;
            Instance = this;

            _logger.LogInformation("Plugin initialized successfully - Version {Version}", GetType().Assembly.GetName().Version);
            _logger.LogInformation("Plugin ID: {PluginId}", Id);
            _logger.LogInformation("Plugin Name: {PluginName}", Name);
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
            _logger.LogTrace("Configuration update requested");
            _logger.LogTrace("Configuration update method called - this means the save button was clicked!");

            var pluginConfig = (PluginConfiguration)configuration;
            
            // Preserve persisted values for fields not present in the submitted payload
            // Specifically ensure RanFirstTime isn't reset to null when saving
            // if (pluginConfig.RanFirstTime == null)
            // {
            //     pluginConfig.RanFirstTime = GetConfigOrDefault<bool>(nameof(PluginConfiguration.RanFirstTime));
            //     _logger.LogTrace("Preserved existing RanFirstTime value: {RanFirstTime}", pluginConfig.RanFirstTime);
            // }
            
            _logger.LogDebug("Configuration details - Enabled: {Enabled}, URL: {Url}, HasApiKey: {HasApiKey}, LibraryDir: {LibraryDir}, SyncInterval: {SyncInterval}", 
                pluginConfig.IsEnabled, 
                pluginConfig.JellyseerrUrl, 
                !string.IsNullOrEmpty(pluginConfig.ApiKey),
                pluginConfig.LibraryDirectory,
                pluginConfig.SyncIntervalHours ?? (double)PluginConfiguration.DefaultValues[nameof(pluginConfig.SyncIntervalHours)]);
            
            // Persist the updated configuration (including ScheduledTaskTimestamp when applicable)
            base.UpdateConfiguration(pluginConfig);
            _logger.LogInformation("Configuration updated successfully");
            
        }

        /// <summary>
        /// Sets the RanFirstTime flag in the configuration.
        /// </summary>
        // public static void SetRanFirstTime(bool value)
        // {
        //     var config = GetConfiguration();
        //     config.RanFirstTime = value;
        //     Instance.UpdateConfiguration(config);
        // }

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
        /// Executes an operation with Jellyfin-style locking that pauses instead of canceling, per operation name.
        /// Only one running and one queued per operation name.
        /// </summary>
        public static async Task<T> ExecuteWithLockAsync<T>(Func<Task<T>> operation, ILogger logger, string operationName, TimeSpan? timeout = null)
        {
            timeout ??= TimeSpan.FromMinutes(Plugin.GetConfigOrDefault<int>(nameof(PluginConfiguration.TaskTimeoutMinutes))); // Use configured task timeout
            var startTime = DateTime.UtcNow;
            var isQueued = false;

            // Try to acquire or queue the operation (global running, per-name queue, only one queued per name)
            while (true)
            {
                lock (_operationSyncLock)
                {
                    if (!_isOperationRunning)
                    {
                        _isOperationRunning = true;
                        _isOperationQueuedByName[operationName] = false;
                        logger.LogTrace("Acquiring global operation lock for {OperationName}", operationName);
                        break;
                    } else if (!isQueued && _isOperationQueuedByName.TryGetValue(operationName, out var queued))
                    {
                        if (!queued)
                        {
                            // Not queued, so queue it
                            isQueued = true;
                            _isOperationQueuedByName[operationName] = true;
                            logger.LogTrace("Queuing operation for {OperationName}", operationName);
                        } else {
                            // If already queued, skip
                            logger.LogWarning("Operation for {OperationName} is already queued. Skipping duplicate request.", operationName);
                            return default!;
                        }
                    } else if (DateTime.UtcNow - startTime >= timeout.Value) {
                        _isOperationQueuedByName[operationName] = false;
                        throw new TimeoutException($"Operation '{operationName}' timed out after {timeout.Value.TotalMinutes} minutes waiting for lock");
                    }
                }
                
                logger.LogWarning("Another operation is running, pausing {OperationName} until it completes", operationName);
                await Task.Delay(10000); // Small delay to prevent busy waiting
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
                    logger.LogTrace("Releasing global operation lock for {OperationName}", operationName);
                }
            }
        }

    }
}