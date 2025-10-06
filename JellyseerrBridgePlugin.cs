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
        public override string Name => "Jellyseerr Bridge v0.21";
        
        public new string Version => "0.21.0.0";
        
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

    public class JellyseerrBridgeServiceRegistrator
    {
        public void RegisterServices(IServiceCollection serviceCollection)
        {
            serviceCollection.AddScoped<ConfigurationService>();
            serviceCollection.AddScoped<JellyseerrApiService>();
            serviceCollection.AddScoped<LibraryManagementService>();
            serviceCollection.AddScoped<LibraryFilterService>();
            serviceCollection.AddScoped<ShowSyncService>();
            serviceCollection.AddScoped<WebhookHandlerService>();
            serviceCollection.AddScoped<ShowSyncTask>();
            
            // Register API controllers
            serviceCollection.AddScoped<ConfigurationController>();
            serviceCollection.AddScoped<ConfigurationPageController>();
            serviceCollection.AddScoped<WebhookController>();
            serviceCollection.AddScoped<LibraryController>();
        }
    }
}