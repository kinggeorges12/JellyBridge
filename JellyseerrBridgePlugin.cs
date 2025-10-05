using Jellyfin.Plugin.JellyseerrBridge.Configuration;
using Jellyfin.Plugin.JellyseerrBridge.Services;
using Jellyfin.Plugin.JellyseerrBridge.Tasks;
using Jellyfin.Plugin.JellyseerrBridge.Api;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JellyseerrBridge
{
    public class JellyseerrBridgePlugin
    {
        public string Name => "Jellyseerr Bridge";

        public string Description => "Bridge Jellyfin with Jellyseerr for seamless show discovery and download requests";

        public string Version => "0.4";

        public Guid Id => new Guid("12345678-1234-1234-1234-123456789012");

        public PluginConfiguration Configuration { get; set; } = new PluginConfiguration();

        public JellyseerrBridgePlugin()
        {
            Instance = this;
        }

        public static JellyseerrBridgePlugin Instance { get; private set; }
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