using Jellyfin.Plugin.JellyseerrBridge.Configuration;
using Microsoft.Extensions.Logging;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Model.Serialization;

namespace Jellyfin.Plugin.JellyseerrBridge
{
    public class JellyseerrBridgePlugin : BasePlugin<PluginConfiguration>, IHasPluginConfiguration, IHasWebPages
    {
        public override Guid Id => new Guid("088e8efc-6855-42ec-bcc9-24fde6da7149");
        public override string Name => "Jellyseerr Bridge";
        
        public string? Image => null;
        
        public static JellyseerrBridgePlugin Instance { get; private set; } = null!;
        
        public JellyseerrBridgePlugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer, ILoggerFactory loggerFactory) 
            : base(applicationPaths, xmlSerializer)
        {
            Instance = this;
            var logger = loggerFactory.CreateLogger<JellyseerrBridgePlugin>();
            var version = GetType().Assembly.GetName().Version?.ToString() ?? "Unknown";
            logger.LogInformation("Jellyseerr Bridge Plugin v{Version} initialized successfully", version);
        }

        public IEnumerable<PluginPageInfo> GetPages()
        {
            string? prefix = GetType().Namespace;

            yield return new PluginPageInfo
            {
                Name = Name,
                EmbeddedResourcePath = $"{prefix}.Configuration.ConfigurationPage.html"
            };
        }
    }
}