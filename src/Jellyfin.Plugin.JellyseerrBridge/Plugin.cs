using Jellyfin.Plugin.JellyseerrBridge.Configuration;
using Jellyfin.Plugin.JellyseerrBridge.Api;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Model.Serialization;
using MediaBrowser.Controller.Plugins;
using MediaBrowser.Controller;

namespace Jellyfin.Plugin.JellyseerrBridge
{
    public class Plugin : BasePlugin<PluginConfiguration>, IHasPluginConfiguration, IHasWebPages, IPluginServiceRegistrator
    {
        public override Guid Id => new Guid("8ecc808c-d6e9-432f-9219-b638fbfb37e6");
        public override string Name => "Jellyseerr Bridge";
        
        public static Plugin Instance { get; private set; } = null!;
        
        public Plugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer) 
            : base(applicationPaths, xmlSerializer)
        {
            Instance = this;
        }

        public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)
        {
            serviceCollection.AddHttpClient();
            serviceCollection.AddScoped<ConfigurationController>();
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