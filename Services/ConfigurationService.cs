using Jellyfin.Plugin.JellyseerrBridge.Configuration;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JellyseerrBridge.Services;

/// <summary>
/// Configuration service for managing plugin settings.
/// </summary>
public class ConfigurationService
{
    private readonly ILogger<ConfigurationService> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="ConfigurationService"/> class.
    /// </summary>
    /// <param name="logger">The logger.</param>
    public ConfigurationService(ILogger<ConfigurationService> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Gets the plugin configuration.
    /// </summary>
    /// <returns>The plugin configuration.</returns>
    public PluginConfiguration GetConfiguration()
    {
        return JellyseerrBridgePlugin.Instance.Configuration;
    }

    /// <summary>
    /// Saves the plugin configuration.
    /// </summary>
    /// <param name="configuration">The configuration to save.</param>
    public void SaveConfiguration(PluginConfiguration configuration)
    {
        JellyseerrBridgePlugin.Instance.UpdateConfiguration(configuration);
        _logger.LogInformation("Configuration saved");
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

        _logger.LogInformation("Configuration validation passed");
        return true;
    }
}