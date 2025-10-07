using MediaBrowser.Controller;
using MediaBrowser.Controller.Plugins;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Jellyfin.Plugin.JellyseerrBridge.Controllers;
using Jellyfin.Plugin.JellyseerrBridge.Configuration;
using Jellyfin.Plugin.JellyseerrBridge.Tasks;

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
            
            // Register the API service
            serviceCollection.AddScoped<JellyseerrApiService>();
            
            // Register the sync service
            serviceCollection.AddScoped<JellyseerrSyncService>();
            
            // Register the scheduled task
            serviceCollection.AddScoped<JellyseerrSyncTask>();
        }
    }
}
