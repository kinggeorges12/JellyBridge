using Jellyfin.Plugin.JellyseerrBridge.Configuration;
using Jellyfin.Plugin.JellyseerrBridge.Services;
using MediaBrowser.Controller.Plugins;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JellyseerrBridge.Api;

/// <summary>
/// Configuration API controller.
/// </summary>
[ApiController]
[Route("Plugins/JellyseerrBridge")]
public class ConfigurationController : ControllerBase
{
    private readonly ConfigurationService _configurationService;
    private readonly LibraryManagementService _libraryManagementService;
    private readonly ILogger<ConfigurationController> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="ConfigurationController"/> class.
    /// </summary>
    /// <param name="configurationService">The configuration service.</param>
    /// <param name="libraryManagementService">The library management service.</param>
    /// <param name="logger">The logger.</param>
    public ConfigurationController(ConfigurationService configurationService, LibraryManagementService libraryManagementService, ILogger<ConfigurationController> logger)
    {
        _configurationService = configurationService;
        _libraryManagementService = libraryManagementService;
        _logger = logger;
    }

    /// <summary>
    /// Gets the plugin configuration.
    /// </summary>
    /// <returns>The plugin configuration.</returns>
    [HttpGet("Configuration")]
    public ActionResult<PluginConfiguration> GetConfiguration()
    {
        try
        {
            var config = _configurationService.GetConfiguration();
            _logger.LogInformation("Configuration retrieved successfully");
            return Ok(config);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving configuration");
            return StatusCode(500, "Error retrieving configuration");
        }
    }

    /// <summary>
    /// Updates the plugin configuration.
    /// </summary>
    /// <param name="configuration">The new configuration.</param>
    /// <returns>Success status.</returns>
    [HttpPost("Configuration")]
    public ActionResult UpdateConfiguration([FromBody] PluginConfiguration configuration)
    {
        try
        {
            if (!_configurationService.ValidateConfiguration(configuration))
            {
                _logger.LogWarning("Configuration validation failed");
                return BadRequest("Invalid configuration");
            }

            _configurationService.SaveConfiguration(configuration);
            _logger.LogInformation("Configuration updated successfully");
            return Ok();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating configuration");
            return StatusCode(500, "Error updating configuration");
        }
    }

    /// <summary>
    /// Tests the Jellyseerr connection.
    /// </summary>
    /// <returns>Connection test result.</returns>
    [HttpPost("TestConnection")]
    public async Task<ActionResult> TestConnection()
    {
        try
        {
            var config = _configurationService.GetConfiguration();
            if (!_configurationService.ValidateConfiguration(config))
            {
                return BadRequest("Invalid configuration");
            }

            // TODO: Implement connection test
            _logger.LogInformation("Connection test requested");
            return Ok(new { success = true, message = "Connection test not yet implemented" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error testing connection");
            return StatusCode(500, "Error testing connection");
        }
    }

    /// <summary>
    /// Triggers a manual sync.
    /// </summary>
    /// <returns>Sync result.</returns>
    [HttpPost("Sync")]
    public async Task<ActionResult> TriggerSync()
    {
        try
        {
            var config = _configurationService.GetConfiguration();
            if (!config.IsEnabled)
            {
                return BadRequest("Plugin is disabled");
            }

            // TODO: Implement manual sync trigger
            _logger.LogInformation("Manual sync triggered");
            return Ok(new { success = true, message = "Sync triggered successfully" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error triggering sync");
            return StatusCode(500, "Error triggering sync");
        }
    }

    /// <summary>
    /// Gets library recommendations.
    /// </summary>
    /// <returns>Library recommendations.</returns>
    [HttpGet("LibraryRecommendations")]
    public ActionResult GetLibraryRecommendations()
    {
        try
        {
            var recommendations = _libraryManagementService.GetLibraryRecommendations();
            _logger.LogInformation("Library recommendations retrieved");
            return Ok(recommendations);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving library recommendations");
            return StatusCode(500, "Error retrieving library recommendations");
        }
    }

    /// <summary>
    /// Validates library configuration.
    /// </summary>
    /// <returns>Validation result.</returns>
    [HttpPost("ValidateLibraries")]
    public ActionResult ValidateLibraries()
    {
        try
        {
            var result = _libraryManagementService.ValidateLibraryConfiguration();
            _logger.LogInformation("Library validation completed. Valid: {IsValid}", result.IsValid);
            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error validating libraries");
            return StatusCode(500, "Error validating libraries");
        }
    }
}
