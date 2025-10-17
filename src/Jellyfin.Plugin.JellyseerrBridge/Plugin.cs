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
        
        // Central semaphore to block all operations when any operation is running
        private static readonly SemaphoreSlim _operationLock = new SemaphoreSlim(1, 1);
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
            
            // Block other operations during configuration update
            _isOperationRunning = true;
            _logger.LogInformation("[JellyseerrBridge] Acquiring operation lock to block other operations");
            
            try
            {
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
            finally
            {
                _isOperationRunning = false;
                _logger.LogInformation("[JellyseerrBridge] Operation lock released, other operations can resume");
            }
        }

        /// <summary>
        /// Gets a configuration value or its default value.
        /// </summary>
        public static T GetConfigOrDefault<T>(string propertyName, PluginConfiguration? config = null)
        {
            config ??= GetConfiguration();
            var propertyInfo = typeof(PluginConfiguration).GetProperty(propertyName);
            T? value = propertyInfo?.GetValue(config) is T t ? t : default;
            
            // For string types, also check if the value is empty
            if (value != null && !(value is string str && string.IsNullOrEmpty(str)))
            {
                return value;
            }

            // Try to get default value from dictionary
            if (PluginConfiguration.DefaultValues?.TryGetValue(propertyName, out var defaultValue) == true)
            {
                return (T)defaultValue;
            }

            // Return default value for the type if no default is found
            return default(T)!;
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
        /// Acquires the operation lock to block other operations.
        /// </summary>
        public static async Task<bool> TryAcquireOperationLockAsync(TimeSpan timeout = default)
        {
            if (timeout == default)
                timeout = TimeSpan.FromSeconds(30);

            return await _operationLock.WaitAsync(timeout);
        }

        /// <summary>
        /// Releases the operation lock.
        /// </summary>
        public static void ReleaseOperationLock()
        {
            _operationLock.Release();
        }

        /// <summary>
        /// Checks if any operation is currently running.
        /// </summary>
        public static bool IsOperationRunning => _isOperationRunning;

        /// <summary>
        /// Executes an operation with automatic locking.
        /// </summary>
        public static async Task<T> ExecuteWithLockAsync<T>(Func<Task<T>> operation, ILogger logger, string operationName)
        {
            if (_isOperationRunning)
            {
                logger.LogInformation("Another operation is running, waiting for lock before executing {OperationName}", operationName);
                await _operationLock.WaitAsync();
                _operationLock.Release();
            }

            _isOperationRunning = true;
            logger.LogInformation("Acquiring operation lock for {OperationName}", operationName);
            
            try
            {
                return await operation();
            }
            finally
            {
                _isOperationRunning = false;
                logger.LogInformation("Releasing operation lock for {OperationName}", operationName);
            }
        }

        /// <summary>
        /// Executes an operation with automatic locking (no return value).
        /// </summary>
        public static async Task ExecuteWithLockAsync(Func<Task> operation, ILogger logger, string operationName)
        {
            if (_isOperationRunning)
            {
                logger.LogInformation("Another operation is running, waiting for lock before executing {OperationName}", operationName);
                await _operationLock.WaitAsync();
                _operationLock.Release();
            }

            _isOperationRunning = true;
            logger.LogInformation("Acquiring operation lock for {OperationName}", operationName);
            
            try
            {
                await operation();
            }
            finally
            {
                _isOperationRunning = false;
                logger.LogInformation("Releasing operation lock for {OperationName}", operationName);
            }
        }
    }
}