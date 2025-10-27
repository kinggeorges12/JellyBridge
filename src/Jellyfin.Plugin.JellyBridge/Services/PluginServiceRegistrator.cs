using MediaBrowser.Controller;
using MediaBrowser.Controller.Plugins;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Jellyfin.Plugin.JellyBridge.Controllers;
using Jellyfin.Plugin.JellyBridge.Configuration;
using Jellyfin.Plugin.JellyBridge.Tasks;
using Jellyfin.Plugin.JellyBridge.Services;

namespace Jellyfin.Plugin.JellyBridge.Services
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
            serviceCollection.AddHttpClient<ApiService>();
            
            // Register the base services
            serviceCollection.AddScoped<ApiService>();
            serviceCollection.AddScoped<SyncService>();
            
            // Register the bridge service
            serviceCollection.AddScoped<BridgeService>();
            
            // Register the library service
            serviceCollection.AddScoped<LibraryService>();
            
            // Register placeholder video generator as transient to avoid early initialization
            serviceCollection.AddTransient<PlaceholderVideoGenerator>();
            
            // Register the route controller
            serviceCollection.AddScoped<Controllers.RouteController>();
        }
    }
}
