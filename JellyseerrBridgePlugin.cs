using Jellyfin.Plugin.JellyseerrBridge.Configuration;
using Jellyfin.Plugin.JellyseerrBridge.Services;
using Jellyfin.Plugin.JellyseerrBridge.Tasks;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Controller.Plugins;
using Microsoft.Extensions.DependencyInjection;

namespace Jellyfin.Plugin.JellyseerrBridge;

/// <summary>
/// The main plugin class.
/// </summary>
public class JellyseerrBridgePlugin : BasePlugin<PluginConfiguration>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="JellyseerrBridgePlugin"/> class.
    /// </summary>
    /// <param name="applicationPaths">Instance of the <see cref="IApplicationPaths"/> interface.</param>
    /// <param name="loggerFactory">Instance of the <see cref="ILoggerFactory"/> interface.</param>
    public JellyseerrBridgePlugin(IApplicationPaths applicationPaths, ILoggerFactory loggerFactory)
        : base(applicationPaths, loggerFactory)
    {
        Instance = this;
    }

    /// <summary>
    /// Gets the plugin instance.
    /// </summary>
    public static JellyseerrBridgePlugin? Instance { get; private set; }

    /// <inheritdoc />
    public override string Name => "Jellyseerr Bridge";

    /// <inheritdoc />
    public override Guid Id => Guid.Parse("12345678-1234-1234-1234-123456789012");

    /// <inheritdoc />
    public override string Description => "Bridge Jellyfin with Jellyseerr for seamless show discovery and download requests";

    /// <inheritdoc />
    public override string ConfigurationFileName => "jellyseerr-bridge.json";

    /// <summary>
    /// Gets the plugin configuration.
    /// </summary>
    public PluginConfiguration PluginConfiguration => Configuration;
}

/// <summary>
/// Plugin service registration.
/// </summary>
public class JellyseerrBridgeServiceRegistrator : IPluginServiceRegistrator
{
    /// <inheritdoc />
    public void RegisterServices(IServiceCollection serviceCollection)
    {
        serviceCollection.AddSingleton<JellyseerrApiService>();
        serviceCollection.AddSingleton<ShowSyncService>();
        serviceCollection.AddSingleton<WebhookHandlerService>();
        serviceCollection.AddSingleton<ConfigurationService>();
        serviceCollection.AddSingleton<LibraryManagementService>();
        serviceCollection.AddSingleton<IScheduledTask, ShowSyncTask>();
    }
}