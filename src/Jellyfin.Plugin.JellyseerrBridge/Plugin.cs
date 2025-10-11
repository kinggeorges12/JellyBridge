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

        public override Guid Id => Guid.Parse("8ecc808c-d6e9-432f-9219-b638fbfb37e6");
        public override string Name => "Jellyseerr Bridge";
        
        public static Plugin Instance { get; private set; } = null!;
        
        public Plugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer, ILoggerFactory loggerFactory) 
            : base(applicationPaths, xmlSerializer)
        {
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
    }
}