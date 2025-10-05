using Jellyfin.Plugin.JellyseerrBridge.Configuration;
using Jellyfin.Plugin.JellyseerrBridge.Services;
using Jellyfin.Plugin.JellyseerrBridge.Tasks;
using Jellyfin.Plugin.JellyseerrBridge.Api;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Model.Serialization;

namespace Jellyfin.Plugin.JellyseerrBridge
{
    public class JellyseerrBridgePlugin : BasePlugin<PluginConfiguration>, IHasPluginConfiguration, IHasWebPages
    {
        public override Guid Id => new Guid("8ecc808c-d6e9-432f-9219-b638fbfb37e6");
        public override string Name => "Jellyseerr Bridge";
        
        public string? Image => null;
        
        public static JellyseerrBridgePlugin Instance { get; private set; } = null!;
        
        public JellyseerrBridgePlugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer) 
            : base(applicationPaths, xmlSerializer)
        {
            Instance = this;
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

    public class JellyseerrBridgeServiceRegistrator
    {
        public void RegisterServices(IServiceCollection serviceCollection)
        {
            serviceCollection.AddScoped<ConfigurationService>();
            serviceCollection.AddScoped<JellyseerrApiService>();
            serviceCollection.AddScoped<LibraryManagementService>();
            serviceCollection.AddScoped<ShowSyncService>();
            serviceCollection.AddScoped<WebhookHandlerService>();
            serviceCollection.AddScoped<ShowSyncTask>();
        }
    }
}