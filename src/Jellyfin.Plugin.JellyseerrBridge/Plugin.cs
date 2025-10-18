using Jellyfin.Plugin.JellyseerrBridge.Configuration;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;
using Microsoft.Extensions.Logging;
using System.Collections.Generic;
using System.Linq;

namespace Jellyfin.Plugin.JellyseerrBridge
{
    public class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages
    {
        private readonly ILogger<Plugin> _logger;
        private readonly ILoggerFactory _loggerFactory;

        public override Guid Id => Guid.Parse("8ecc808c-d6e9-432f-9219-b638fbfb37e6");
        public override string Name => "Jellyseerr Bridge";
        
        public static Plugin Instance { get; private set; } = null!;
        
        public ILoggerFactory LoggerFactory => _loggerFactory;
        
        // Jellyfin-style locking for operations that should be mutually exclusive
        private static readonly object _operationSyncLock = new object();
        private static bool _isOperationRunning = false;
        
        public Plugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer, ILoggerFactory loggerFactory) 
            : base(applicationPaths, xmlSerializer)
        {
            _loggerFactory = loggerFactory;
            _logger = loggerFactory.CreateLogger<Plugin>();
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
            _logger.LogInformation("[JellyseerrBridge] Configuration update requested");
            _logger.LogInformation("[JellyseerrBridge] Configuration update method called - this means the save button was clicked!");
            
            var pluginConfig = (PluginConfiguration)configuration;
            _logger.LogInformation("[JellyseerrBridge] Configuration details - Enabled: {Enabled}, URL: {Url}, HasApiKey: {HasApiKey}, LibraryDir: {LibraryDir}, UserId: {UserId}, SyncInterval: {SyncInterval}", 
                pluginConfig.IsEnabled, 
                pluginConfig.JellyseerrUrl, 
                !string.IsNullOrEmpty(pluginConfig.ApiKey),
                pluginConfig.LibraryDirectory,
                pluginConfig.UserId ?? (int)PluginConfiguration.DefaultValues[nameof(pluginConfig.UserId)],
                pluginConfig.SyncIntervalHours ?? (int)PluginConfiguration.DefaultValues[nameof(pluginConfig.SyncIntervalHours)]);
            
            base.UpdateConfiguration(configuration);
            _logger.LogInformation("[JellyseerrBridge] Configuration updated successfully");
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
            if (rawValue != null && rawValue is T t) {
                return t;
            }

            // Try to get default value from dictionary
            if (PluginConfiguration.DefaultValues?.TryGetValue(propertyName, out var defaultValue) == true)
            {
                return (T)defaultValue;
            }

            // Reference type string may be null
            if (typeof(T) == typeof(string))
            {
                return (T)(object)string.Empty;
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
                        logger.LogInformation("Acquiring operation lock for {OperationName}", operationName);
                        break;
                    }
                }
                
                logger.LogInformation("Another operation is running, pausing {OperationName} until it completes", operationName);
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
                    logger.LogInformation("Releasing operation lock for {OperationName}", operationName);
                }
            }
        }

        /// <summary>
        /// Executes an operation with Jellyfin-style locking that pauses instead of canceling (no return value).
        /// </summary>
        public static async Task ExecuteWithLockAsync(Func<Task> operation, ILogger logger, string operationName, TimeSpan? timeout = null)
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
                        logger.LogInformation("Acquiring operation lock for {OperationName}", operationName);
                        break;
                    }
                }
                
                logger.LogInformation("Another operation is running, pausing {OperationName} until it completes", operationName);
                await Task.Delay(100); // Small delay to prevent busy waiting
            }
            
            // Check if we timed out
            if (DateTime.UtcNow - startTime >= timeout.Value)
            {
                throw new TimeoutException($"Operation '{operationName}' timed out after {timeout.Value.TotalMinutes} minutes waiting for lock");
            }
            
            try
            {
                await operation();
            }
            finally
            {
                lock (_operationSyncLock)
                {
                    _isOperationRunning = false;
                    logger.LogInformation("Releasing operation lock for {OperationName}", operationName);
                }
            }
        }

        /// <summary>
        /// Executes a synchronous operation with Jellyfin-style locking that pauses instead of canceling.
        /// </summary>
        public static void ExecuteWithLock(Action operation, ILogger logger, string operationName, TimeSpan? timeout = null)
        {
            timeout ??= TimeSpan.FromMinutes(1); // Default 1 minute timeout
            var startTime = DateTime.UtcNow;
            bool lockAcquired = false;
            
            try
            {
                // Wait for any running operation to complete (pausing, not canceling)
                while (DateTime.UtcNow - startTime < timeout.Value)
                {
                    lock (_operationSyncLock)
                    {
                        if (!_isOperationRunning)
                        {
                            _isOperationRunning = true;
                            lockAcquired = true;
                            logger.LogInformation("Acquiring operation lock for {OperationName}", operationName);
                            break;
                        }
                    }
                    
                    logger.LogInformation("Another operation is running, pausing {OperationName} until it completes", operationName);
                    Thread.Sleep(100); // Small delay to prevent busy waiting
                }
                
                // Check if we timed out
                if (!lockAcquired)
                {
                    throw new TimeoutException($"Operation '{operationName}' timed out after {timeout.Value.TotalMinutes} minutes waiting for lock");
                }
                
                // Execute the operation
                operation();
            }
            finally
            {
                // Always release the lock if we acquired it
                if (lockAcquired)
                {
                    lock (_operationSyncLock)
                    {
                        _isOperationRunning = false;
                        logger.LogInformation("Releasing operation lock for {OperationName}", operationName);
                    }
                }
            }
        }
    }
}