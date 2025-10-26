using MediaBrowser.Controller;
using MediaBrowser.Controller.Plugins;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Jellyfin.Plugin.JellyseerrBridge.Controllers;
using Jellyfin.Plugin.JellyseerrBridge.Configuration;
using Jellyfin.Plugin.JellyseerrBridge.Tasks;
using Jellyfin.Plugin.JellyseerrBridge.Services;

namespace Jellyfin.Plugin.JellyseerrBridge.Services
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
            
            // Register HTTP client for Jellyseerr API
            serviceCollection.AddHttpClient<JellyseerrApiService>();
            
            // Register the base services
            serviceCollection.AddScoped<JellyseerrApiService>();
            serviceCollection.AddScoped<JellyseerrSyncService>();
            
            // Register the bridge service
            serviceCollection.AddScoped<JellyseerrBridgeService>();
            
            // Register the library service
            serviceCollection.AddScoped<JellyseerrLibraryService>();
            
            // Register placeholder video generator as transient to avoid early initialization
            serviceCollection.AddTransient<PlaceholderVideoGenerator>();
            
            // Register the route controller
            serviceCollection.AddScoped<Controllers.RouteController>();
        }
    }
}
