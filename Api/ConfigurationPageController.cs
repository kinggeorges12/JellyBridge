using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JellyseerrBridge.Api;

/// <summary>
/// Configuration page API controller.
/// </summary>
[ApiController]
[Route("Plugins/JellyseerrBridge/ConfigurationPage")]
public class ConfigurationPageController : ControllerBase
{
    private readonly ILogger<ConfigurationPageController> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="ConfigurationPageController"/> class.
    /// </summary>
    /// <param name="logger">The logger.</param>
    public ConfigurationPageController(ILogger<ConfigurationPageController> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Gets the configuration page HTML.
    /// </summary>
    /// <returns>The configuration page HTML.</returns>
    [HttpGet]
    public ActionResult GetConfigurationPage()
    {
        try
        {
            _logger.LogInformation("Configuration page requested");
            
            var html = @"
<!DOCTYPE html>
<html>
<head>
    <title>Jellyseerr Bridge Configuration</title>
    <style>
        body { font-family: Arial, sans-serif; margin: 20px; }
        .form-group { margin-bottom: 15px; }
        label { display: block; margin-bottom: 5px; font-weight: bold; }
        input[type='text'], input[type='password'] { width: 300px; padding: 5px; }
        button { padding: 10px 20px; background: #007bff; color: white; border: none; cursor: pointer; }
        button:hover { background: #0056b3; }
    </style>
</head>
<body>
    <h1>Jellyseerr Bridge Configuration</h1>
    <p>Plugin is installed and running. Full configuration interface coming soon.</p>
    <div class='form-group'>
        <label>Status:</label>
        <span style='color: green;'>✓ Plugin Active</span>
    </div>
</body>
</html>";
            
            return Content(html, "text/html");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting configuration page");
            return StatusCode(500, "Error getting configuration page");
        }
    }
}