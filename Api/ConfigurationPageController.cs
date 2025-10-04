using Jellyfin.Plugin.JellyseerrBridge.Configuration;
using Jellyfin.Plugin.JellyseerrBridge.Services;
using MediaBrowser.Controller.Plugins;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JellyseerrBridge.Api;

/// <summary>
/// Configuration page controller.
/// </summary>
[ApiController]
[Route("Plugins/JellyseerrBridge")]
public class ConfigurationPageController : ControllerBase
{
    private readonly ConfigurationService _configurationService;
    private readonly ILogger<ConfigurationPageController> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="ConfigurationPageController"/> class.
    /// </summary>
    /// <param name="configurationService">The configuration service.</param>
    /// <param name="logger">The logger.</param>
    public ConfigurationPageController(ConfigurationService configurationService, ILogger<ConfigurationPageController> logger)
    {
        _configurationService = configurationService;
        _logger = logger;
    }

    /// <summary>
    /// Gets the configuration page.
    /// </summary>
    /// <returns>The configuration page HTML.</returns>
    [HttpGet]
    public ActionResult GetConfigurationPage()
    {
        try
        {
            var config = _configurationService.GetConfiguration();
            var html = GenerateConfigurationPage(config);
            _logger.LogInformation("Configuration page served");
            return Content(html, "text/html");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error serving configuration page");
            return StatusCode(500, "Error serving configuration page");
        }
    }

    private string GenerateConfigurationPage(PluginConfiguration config)
    {
        return $@"
<!DOCTYPE html>
<html>
<head>
    <title>Jellyseerr Bridge Configuration</title>
    <style>
        body {{ font-family: Arial, sans-serif; margin: 20px; }}
        .container {{ max-width: 800px; margin: 0 auto; }}
        .form-group {{ margin-bottom: 15px; }}
        label {{ display: block; margin-bottom: 5px; font-weight: bold; }}
        input[type='text'], input[type='password'], input[type='number'], textarea {{ 
            width: 100%; padding: 8px; border: 1px solid #ccc; border-radius: 4px; 
        }}
        button {{ 
            background-color: #007bff; color: white; padding: 10px 20px; 
            border: none; border-radius: 4px; cursor: pointer; margin-right: 10px; 
        }}
        button:hover {{ background-color: #0056b3; }}
        .success {{ color: green; margin-top: 10px; }}
        .error {{ color: red; margin-top: 10px; }}
        .section {{ margin-bottom: 30px; padding: 20px; border: 1px solid #ddd; border-radius: 8px; }}
        .section h3 {{ margin-top: 0; color: #333; }}
    </style>
</head>
<body>
    <div class='container'>
        <h1>Jellyseerr Bridge Configuration</h1>
        
        <form id='configForm'>
            <div class='section'>
                <h3>Jellyseerr Connection</h3>
                <div class='form-group'>
                    <label for='jellyseerrUrl'>Jellyseerr URL:</label>
                    <input type='text' id='jellyseerrUrl' name='jellyseerrUrl' value='{config.JellyseerrUrl}' placeholder='http://localhost:5055' required>
                </div>
                <div class='form-group'>
                    <label for='apiKey'>API Key:</label>
                    <input type='password' id='apiKey' name='apiKey' value='{config.ApiKey}' required>
                </div>
                <div class='form-group'>
                    <label for='email'>Email:</label>
                    <input type='text' id='email' name='email' value='{config.Email}' required>
                </div>
                <div class='form-group'>
                    <label for='password'>Password:</label>
                    <input type='password' id='password' name='password' value='{config.Password}' required>
                </div>
            </div>

            <div class='section'>
                <h3>Directory Configuration</h3>
                <div class='form-group'>
                    <label for='showsDirectory'>Shows Directory:</label>
                    <input type='text' id='showsDirectory' name='showsDirectory' value='{config.ShowsDirectory}' placeholder='/path/to/shows' required>
                </div>
                <div class='form-group'>
                    <label for='rootFolder'>Root Folder for Downloads:</label>
                    <input type='text' id='rootFolder' name='rootFolder' value='{config.RootFolder}' placeholder='/media/movie/Movies' required>
                </div>
            </div>

            <div class='section'>
                <h3>Service Configuration</h3>
                <div class='form-group'>
                    <label for='servicesToFetch'>Services to Fetch (comma-separated):</label>
                    <input type='text' id='servicesToFetch' name='servicesToFetch' value='{string.Join(", ", config.ServicesToFetch)}' placeholder='Netflix, Prime Video'>
                </div>
                <div class='form-group'>
                    <label for='serviceDirectories'>Service Directories (JSON):</label>
                    <textarea id='serviceDirectories' name='serviceDirectories' rows='4'>{System.Text.Json.JsonSerializer.Serialize(config.ServiceDirectories, new System.Text.Json.JsonSerializerOptions { WriteIndented = true })}</textarea>
                </div>
                <div class='form-group'>
                    <label for='serviceIds'>Service IDs (JSON):</label>
                    <textarea id='serviceIds' name='serviceIds' rows='4'>{System.Text.Json.JsonSerializer.Serialize(config.ServiceIds, new System.Text.Json.JsonSerializerOptions { WriteIndented = true })}</textarea>
                </div>
            </div>

            <div class='section'>
                <h3>Advanced Settings</h3>
                <div class='form-group'>
                    <label for='syncIntervalHours'>Sync Interval (hours):</label>
                    <input type='number' id='syncIntervalHours' name='syncIntervalHours' value='{config.SyncIntervalHours}' min='1' max='168'>
                </div>
                <div class='form-group'>
                    <label for='webhookPort'>Webhook Port:</label>
                    <input type='number' id='webhookPort' name='webhookPort' value='{config.WebhookPort}' min='1000' max='65535'>
                </div>
                <div class='form-group'>
                    <label for='userId'>User ID:</label>
                    <input type='number' id='userId' name='userId' value='{config.UserId}' min='1'>
                </div>
                <div class='form-group'>
                    <label>
                        <input type='checkbox' id='isEnabled' name='isEnabled' {(config.IsEnabled ? "checked" : "")}>
                        Enable Plugin
                    </label>
                </div>
                <div class='form-group'>
                    <label>
                        <input type='checkbox' id='request4K' name='request4K' {(config.Request4K ? "checked" : "")}>
                        Request 4K Content
                    </label>
                </div>
                <div class='form-group'>
                    <label>
                        <input type='checkbox' id='createSeparateLibraries' name='createSeparateLibraries' {(config.CreateSeparateLibraries ? "checked" : "")}>
                        Create Separate Libraries for Streaming Services
                    </label>
                </div>
                <div class='form-group'>
                    <label for='libraryPrefix'>Library Prefix:</label>
                    <input type='text' id='libraryPrefix' name='libraryPrefix' value='{config.LibraryPrefix}' placeholder='Streaming - '>
                </div>
                <div class='form-group'>
                    <label>
                        <input type='checkbox' id='excludeFromMainLibraries' name='excludeFromMainLibraries' {(config.ExcludeFromMainLibraries ? "checked" : "")}>
                        Exclude Placeholder Shows from Main Libraries
                    </label>
                </div>
            </div>

            <button type='button' onclick='saveConfiguration()'>Save Configuration</button>
            <button type='button' onclick='testConnection()'>Test Connection</button>
            <button type='button' onclick='triggerSync()'>Trigger Sync</button>
        </form>

        <div id='message'></div>
    </div>

    <script>
        function showMessage(message, isError = false) {{
            const messageDiv = document.getElementById('message');
            messageDiv.innerHTML = '<div class='' + (isError ? 'error' : 'success') + '''>' + message + '</div>';
            setTimeout(() => messageDiv.innerHTML = '', 5000);
        }}

        function saveConfiguration() {{
            const formData = new FormData(document.getElementById('configForm'));
            const config = {{
                jellyseerrUrl: formData.get('jellyseerrUrl'),
                apiKey: formData.get('apiKey'),
                email: formData.get('email'),
                password: formData.get('password'),
                showsDirectory: formData.get('showsDirectory'),
                rootFolder: formData.get('rootFolder'),
                servicesToFetch: formData.get('servicesToFetch').split(',').map(s => s.trim()).filter(s => s),
                serviceDirectories: JSON.parse(formData.get('serviceDirectories')),
                serviceIds: JSON.parse(formData.get('serviceIds')),
                syncIntervalHours: parseInt(formData.get('syncIntervalHours')),
                webhookPort: parseInt(formData.get('webhookPort')),
                userId: parseInt(formData.get('userId')),
                isEnabled: document.getElementById('isEnabled').checked,
                request4K: document.getElementById('request4K').checked,
                createSeparateLibraries: document.getElementById('createSeparateLibraries').checked,
                libraryPrefix: formData.get('libraryPrefix'),
                excludeFromMainLibraries: document.getElementById('excludeFromMainLibraries').checked
            }};

            fetch('/Plugins/JellyseerrBridge/Configuration', {{
                method: 'POST',
                headers: {{ 'Content-Type': 'application/json' }},
                body: JSON.stringify(config)
            }})
            .then(response => response.json())
            .then(data => {{
                if (data.success !== false) {{
                    showMessage('Configuration saved successfully!');
                }} else {{
                    showMessage('Error saving configuration: ' + (data.message || 'Unknown error'), true);
                }}
            }})
            .catch(error => {{
                showMessage('Error saving configuration: ' + error.message, true);
            }});
        }}

        function testConnection() {{
            fetch('/Plugins/JellyseerrBridge/TestConnection', {{ method: 'POST' }})
            .then(response => response.json())
            .then(data => {{
                showMessage(data.message || 'Connection test completed');
            }})
            .catch(error => {{
                showMessage('Error testing connection: ' + error.message, true);
            }});
        }}

        function triggerSync() {{
            fetch('/Plugins/JellyseerrBridge/Sync', {{ method: 'POST' }})
            .then(response => response.json())
            .then(data => {{
                showMessage(data.message || 'Sync triggered successfully');
            }})
            .catch(error => {{
                showMessage('Error triggering sync: ' + error.message, true);
            }});
        }}
    </script>
</body>
</html>";
    }
}
