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
            // NOTE: AddHttpClient registers ApiService as transient with a properly
            // lifecycle-managed HttpClient from IHttpClientFactory. Do NOT add a
            // second AddScoped<ApiService>() as it would override this and cause
            // ObjectDisposedException in Jellyfin 10.11.5+.
            serviceCollection.AddHttpClient<ApiService>();

            // Register Jellyfin wrapper classes as transient to avoid scope disposal issues
            serviceCollection.AddTransient<JellyfinILibraryManager>(provider =>
                new JellyfinILibraryManager(provider.GetRequiredService<MediaBrowser.Controller.Library.ILibraryManager>()));
            serviceCollection.AddTransient<JellyfinIUserDataManager>(provider =>
                new JellyfinIUserDataManager(provider.GetRequiredService<MediaBrowser.Controller.Library.IUserDataManager>()));
            serviceCollection.AddTransient<JellyfinIUserManager>(provider =>
                new JellyfinIUserManager(provider.GetRequiredService<MediaBrowser.Controller.Library.IUserManager>()));
            serviceCollection.AddTransient<JellyfinIProviderManager>(provider =>
                new JellyfinIProviderManager(provider.GetRequiredService<MediaBrowser.Controller.Providers.IProviderManager>()));

            // Register the base services as transient to avoid scope disposal issues in 10.11.5+
            serviceCollection.AddTransient<SyncService>();
            serviceCollection.AddTransient<MetadataService>();
            serviceCollection.AddTransient<SortService>();
            serviceCollection.AddTransient<CleanupService>();

            // Register the bridge service
            serviceCollection.AddTransient<BridgeService>();

            // Register the discover service
            serviceCollection.AddTransient<DiscoverService>();

            // Register the favorite service
            serviceCollection.AddTransient<FavoriteService>();

            // Register the library service
            serviceCollection.AddTransient<LibraryService>();
            
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
