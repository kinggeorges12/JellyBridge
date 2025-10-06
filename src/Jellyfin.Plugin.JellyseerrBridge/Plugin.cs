using Jellyfin.Plugin.JellyseerrBridge.Configuration;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JellyseerrBridge
{
    public class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages
    {
        private readonly ILogger<Plugin> _logger;

        public override Guid Id => new Guid("8ecc808c-d6e9-432f-9219-b638fbfb37e6");
        public override string Name => "Jellyseerr Bridge";
        
        public static Plugin Instance { get; private set; } = null!;
        
        public Plugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer, ILogger<Plugin> logger) 
            : base(applicationPaths, xmlSerializer)
        {
            _logger = logger;
            Instance = this;
            _logger.LogInformation("[JellyseerrBridge] Plugin initialized successfully");
        }

        public override void UpdateConfiguration(BasePluginConfiguration configuration)
        {
            _logger.LogInformation("[JellyseerrBridge] Configuration update requested");
            
            var pluginConfig = (PluginConfiguration)configuration;
            _logger.LogInformation("[JellyseerrBridge] Configuration details - Enabled: {Enabled}, URL: {Url}, HasApiKey: {HasApiKey}, LibraryDir: {LibraryDir}, UserId: {UserId}, SyncInterval: {SyncInterval}", 
                pluginConfig.IsEnabled, 
                pluginConfig.JellyseerrUrl, 
                !string.IsNullOrEmpty(pluginConfig.ApiKey),
                pluginConfig.LibraryDirectory,
                pluginConfig.UserId,
                pluginConfig.SyncIntervalHours);
            
            base.UpdateConfiguration(configuration);
            _logger.LogInformation("[JellyseerrBridge] Configuration updated successfully");
        }

        public IEnumerable<PluginPageInfo> GetPages()
        {
            string? prefix = GetType().Namespace;

            yield return new PluginPageInfo
            {
                Name = Name,
                EmbeddedResourcePath = $"{prefix}.ConfigurationPage.html"
            };
        }
    }
}