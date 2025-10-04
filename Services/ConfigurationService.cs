using Jellyfin.Plugin.JellyseerrBridge.Configuration;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller.Plugins;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JellyseerrBridge.Services;

/// <summary>
/// Configuration service for managing plugin settings.
/// </summary>
public class ConfigurationService
{
    private readonly IConfigurationManager _configurationManager;
    private readonly ILogger<ConfigurationService> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="ConfigurationService"/> class.
    /// </summary>
    /// <param name="configurationManager">The configuration manager.</param>
    /// <param name="logger">The logger.</param>
    public ConfigurationService(IConfigurationManager configurationManager, ILogger<ConfigurationService> logger)
    {
        _configurationManager = configurationManager;
        _logger = logger;
    }

    /// <summary>
    /// Gets the plugin configuration.
    /// </summary>
    /// <returns>The plugin configuration.</returns>
    public PluginConfiguration GetConfiguration()
    {
        var plugin = JellyseerrBridgePlugin.Instance;
        if (plugin == null)
        {
            _logger.LogError("Plugin instance is null");
            return new PluginConfiguration();
        }

        return plugin.Configuration;
    }

    /// <summary>
    /// Saves the plugin configuration.
    /// </summary>
    /// <param name="configuration">The configuration to save.</param>
    public void SaveConfiguration(PluginConfiguration configuration)
    {
        var plugin = JellyseerrBridgePlugin.Instance;
        if (plugin == null)
        {
            _logger.LogError("Plugin instance is null, cannot save configuration");
            return;
        }

        plugin.UpdateConfiguration(configuration);
        _logger.LogInformation("Configuration saved successfully");
    }

    /// <summary>
    /// Validates the configuration.
    /// </summary>
    /// <param name="configuration">The configuration to validate.</param>
    /// <returns>True if valid, false otherwise.</returns>
    public bool ValidateConfiguration(PluginConfiguration configuration)
    {
        if (string.IsNullOrWhiteSpace(configuration.JellyseerrUrl))
        {
            _logger.LogWarning("Jellyseerr URL is not configured");
            return false;
        }

        if (string.IsNullOrWhiteSpace(configuration.ApiKey))
        {
            _logger.LogWarning("API key is not configured");
            return false;
        }

        if (string.IsNullOrWhiteSpace(configuration.Email))
        {
            _logger.LogWarning("Email is not configured");
            return false;
        }

        if (string.IsNullOrWhiteSpace(configuration.Password))
        {
            _logger.LogWarning("Password is not configured");
            return false;
        }

        if (string.IsNullOrWhiteSpace(configuration.ShowsDirectory))
        {
            _logger.LogWarning("Shows directory is not configured");
            return false;
        }

        if (!configuration.ServiceDirectories.Any())
        {
            _logger.LogWarning("No service directories configured");
            return false;
        }

        if (!configuration.ServiceIds.Any())
        {
            _logger.LogWarning("No service IDs configured");
            return false;
        }

        _logger.LogInformation("Configuration validation passed");
        return true;
    }
}
