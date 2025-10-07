using MediaBrowser.Controller;
using MediaBrowser.Controller.Plugins;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JellyseerrBridge
{
    /// <summary>
    /// Register Jellyseerr Bridge services.
    /// </summary>
    public class PluginServiceRegistrator : IPluginServiceRegistrator
    {
        /// <inheritdoc />
        public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)
        {
            // Register logging services for the plugin
            serviceCollection.AddLogging();
        }
    }
}
