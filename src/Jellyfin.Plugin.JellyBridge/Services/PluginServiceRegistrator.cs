using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Jellyfin.Plugin.JellyBridge.Controllers;
using Jellyfin.Plugin.JellyBridge.Configuration;
using Jellyfin.Plugin.JellyBridge.Tasks;
using Jellyfin.Plugin.JellyBridge.Services;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Plugins;
using Jellyfin.Plugin.JellyBridge.JellyfinModels;

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
            
            // Register Jellyfin wrapper classes (only those with version differences)
            serviceCollection.AddScoped<JellyfinILibraryManager>(provider => 
                new JellyfinILibraryManager(provider.GetRequiredService<MediaBrowser.Controller.Library.ILibraryManager>()));
            serviceCollection.AddScoped<JellyfinIUserDataManager>(provider => 
                new JellyfinIUserDataManager(provider.GetRequiredService<MediaBrowser.Controller.Library.IUserDataManager>()));
            serviceCollection.AddScoped<JellyfinIUserManager>(provider =>
                new JellyfinIUserManager(provider.GetRequiredService<MediaBrowser.Controller.Library.IUserManager>()));
            serviceCollection.AddScoped<JellyfinIProviderManager>(provider =>
                new JellyfinIProviderManager(provider.GetRequiredService<MediaBrowser.Controller.Providers.IProviderManager>()));
            
            // Register the base services
            serviceCollection.AddScoped<ApiService>();
            serviceCollection.AddScoped<SyncService>();
            serviceCollection.AddScoped<MetadataService>();
            serviceCollection.AddScoped<SortService>();
            serviceCollection.AddScoped<CleanupService>();
            
            // Register the bridge service
            serviceCollection.AddScoped<BridgeService>();
            
            // Register the discover service
            serviceCollection.AddScoped<DiscoverService>();
            
            // Register the favorite service
            serviceCollection.AddScoped<FavoriteService>();
            
            // Register the library service
            serviceCollection.AddScoped<LibraryService>();
            
            // Register placeholder video generator as transient to avoid early initialization
            serviceCollection.AddTransient<PlaceholderVideoGenerator>();
            
            // Register hosted services
            serviceCollection.AddHostedService<FavoriteEventHandler>();

            // Register controllers (organized by configuration page sections)
            serviceCollection.AddScoped<Controllers.PluginConfigurationController>();
            serviceCollection.AddScoped<Controllers.GeneralSettingsController>();
            serviceCollection.AddScoped<Controllers.TaskStatusController>();
            serviceCollection.AddScoped<Controllers.ImportDiscoverContentController>();
            serviceCollection.AddScoped<Controllers.SortDiscoverContentController>();
            serviceCollection.AddScoped<Controllers.ManageDiscoverLibraryController>();
            serviceCollection.AddScoped<Controllers.AdvancedSettingsController>();
        }
    }
}
