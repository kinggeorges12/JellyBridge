using Jellyfin.Plugin.JellyseerrBridge.Configuration;
using Jellyfin.Plugin.JellyseerrBridge.Services;
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
    /// Gets the configuration page HTML.
    /// </summary>
    /// <returns>The configuration page HTML.</returns>
    [HttpGet]
    public ActionResult GetConfigurationPage()
    {
        try
        {
            _logger.LogInformation("Configuration page requested");
            
            var config = _configurationService.GetConfiguration();
            var html = GenerateConfigurationPage(config);
            
            return Content(html, "text/html");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting configuration page");
            return StatusCode(500, "Error getting configuration page");
        }
    }

    /// <summary>
    /// Generates the configuration page HTML.
    /// </summary>
    /// <param name="config">The plugin configuration.</param>
    /// <returns>The HTML content.</returns>
    private string GenerateConfigurationPage(PluginConfiguration config)
    {
        return $@"
<!DOCTYPE html>
<html>
<head>
    <title>Jellyseerr Bridge Configuration</title>
    <style>
        body {{ 
            font-family: 'Segoe UI', Tahoma, Geneva, Verdana, sans-serif; 
            margin: 0; 
            padding: 20px; 
            background-color: #f5f5f5; 
        }}
        .container {{ 
            max-width: 800px; 
            margin: 0 auto; 
            background: white; 
            padding: 30px; 
            border-radius: 8px; 
            box-shadow: 0 2px 10px rgba(0,0,0,0.1); 
        }}
        h1 {{ 
            color: #333; 
            border-bottom: 2px solid #007bff; 
            padding-bottom: 10px; 
        }}
        .form-group {{ 
            margin-bottom: 20px; 
        }}
        label {{ 
            display: block; 
            margin-bottom: 5px; 
            font-weight: bold; 
            color: #555; 
        }}
        input[type='text'], input[type='password'], input[type='number'], select {{ 
            width: 100%; 
            max-width: 400px; 
            padding: 8px 12px; 
            border: 1px solid #ddd; 
            border-radius: 4px; 
            font-size: 14px; 
        }}
        input[type='checkbox'] {{ 
            margin-right: 8px; 
        }}
        button {{ 
            padding: 10px 20px; 
            background: #007bff; 
            color: white; 
            border: none; 
            border-radius: 4px; 
            cursor: pointer; 
            font-size: 14px; 
            margin-right: 10px; 
        }}
        button:hover {{ 
            background: #0056b3; 
        }}
        .btn-secondary {{ 
            background: #6c757d; 
        }}
        .btn-secondary:hover {{ 
            background: #545b62; 
        }}
        .status {{ 
            padding: 10px; 
            border-radius: 4px; 
            margin-bottom: 20px; 
        }}
        .status.success {{ 
            background: #d4edda; 
            color: #155724; 
            border: 1px solid #c3e6cb; 
        }}
        .status.error {{ 
            background: #f8d7da; 
            color: #721c24; 
            border: 1px solid #f5c6cb; 
        }}
        .section {{ 
            margin-bottom: 30px; 
            padding: 20px; 
            border: 1px solid #e9ecef; 
            border-radius: 4px; 
        }}
        .section h3 {{ 
            margin-top: 0; 
            color: #007bff; 
        }}
        .help-text {{ 
            font-size: 12px; 
            color: #666; 
            margin-top: 5px; 
        }}
    </style>
</head>
<body>
    <div class='container'>
        <h1>🎬 Jellyseerr Bridge Configuration</h1>
        
        <div id='status'></div>
        
        <form id='configForm'>
            <div class='section'>
                <h3>🔗 Jellyseerr Connection</h3>
                    <div class='form-group'>
                        <label for='jellyseerrUrl'>Jellyseerr URL:</label>
                        <input type='text' id='jellyseerrUrl' name='jellyseerrUrl' value='{config.JellyseerrUrl}' placeholder='http://localhost:5055' required>
                        <div class='help-text'>The base URL of your Jellyseerr instance</div>
                    </div>
                    
                    <div class='form-group'>
                        <label for='apiKey'>API Key:</label>
                        <input type='password' id='apiKey' name='apiKey' value='{config.ApiKey}' placeholder='Your Jellyseerr API key' required>
                        <div class='help-text'>Your Jellyseerr API key (found in Settings → General)</div>
                    </div>
            </div>
            
            <div class='section'>
                <h3>📁 Library Configuration</h3>
                <div class='form-group'>
                    <label for='libraryDirectory'>Library Directory:</label>
                    <input type='text' id='libraryDirectory' name='libraryDirectory' value='{config.LibraryDirectory}' placeholder='/data/Jellyseerr' required>
                    <div class='help-text'>Path to Jellyseerr's library directory</div>
                </div>
                
                <div class='form-group'>
                    <label for='rootFolder'>Root Folder:</label>
                    <input type='text' id='rootFolder' name='rootFolder' value='{config.RootFolder}' placeholder='/data/Jellyseerr' required>
                    <div class='help-text'>Root folder for downloads</div>
                </div>
                
                <div class='form-group'>
                    <label>
                        <input type='checkbox' id='createSeparateLibraries' name='createSeparateLibraries' {(config.CreateSeparateLibraries ? "checked" : "")}>
                        Create separate libraries for streaming services
                    </label>
                    <div class='help-text'>Creates dedicated libraries for each streaming service (e.g., 'Streaming - Netflix')</div>
                </div>
                
                <div class='form-group'>
                    <label for='libraryPrefix'>Library Prefix:</label>
                    <input type='text' id='libraryPrefix' name='libraryPrefix' value='{config.LibraryPrefix}' placeholder='Streaming - '>
                    <div class='help-text'>Prefix for streaming service libraries</div>
                </div>
                
                <div class='form-group'>
                    <label>
                        <input type='checkbox' id='excludeFromMainLibraries' name='excludeFromMainLibraries' {(config.ExcludeFromMainLibraries ? "checked" : "")}>
                        Exclude placeholder shows from main libraries
                    </label>
                    <div class='help-text'>Prevents placeholder shows from appearing in your main Jellyfin libraries</div>
                </div>
            </div>
            
            <div class='section'>
                <h3>⚙️ Plugin Settings</h3>
                <div class='form-group'>
                    <label>
                        <input type='checkbox' id='isEnabled' name='isEnabled' {(config.IsEnabled ? "checked" : "")}>
                        Enable Plugin
                    </label>
                    <div class='help-text'>Enable or disable the plugin</div>
                </div>
                
                <div class='form-group'>
                    <label for='syncIntervalHours'>Sync Interval (hours):</label>
                    <input type='number' id='syncIntervalHours' name='syncIntervalHours' value='{config.SyncIntervalHours}' min='1' max='168'>
                    <div class='help-text'>How often to sync shows (1-168 hours)</div>
                </div>
                
                <div class='form-group'>
                    <label for='webhookPort'>Webhook Port:</label>
                    <input type='number' id='webhookPort' name='webhookPort' value='{config.WebhookPort}' min='1024' max='65535'>
                    <div class='help-text'>Port for webhook events (1024-65535)</div>
                </div>
                
                <div class='form-group'>
                    <label for='userId'>User ID:</label>
                    <input type='number' id='userId' name='userId' value='{config.UserId}' min='1'>
                    <div class='help-text'>Jellyfin user ID for requests</div>
                </div>
                
                <div class='form-group'>
                    <label>
                        <input type='checkbox' id='request4K' name='request4K' {(config.Request4K ? "checked" : "")}>
                        Request 4K Content
                    </label>
                    <div class='help-text'>Request 4K quality content when available</div>
                </div>
            </div>
            
            <div class='form-group'>
                <button type='submit'>💾 Save Configuration</button>
                <button type='button' onclick='testConnection()' class='btn-secondary'>🔍 Test Connection</button>
                <button type='button' onclick='triggerSync()' class='btn-secondary'>🔄 Trigger Sync</button>
            </div>
        </form>
    </div>
    
    <script>
        document.getElementById('configForm').addEventListener('submit', function(e) {{
            e.preventDefault();
            saveConfiguration();
        }});
        
            function saveConfiguration() {{
                const formData = new FormData(document.getElementById('configForm'));
                const config = {{
                    jellyseerrUrl: formData.get('jellyseerrUrl'),
                    apiKey: formData.get('apiKey'),
                    libraryDirectory: formData.get('libraryDirectory'),
                    rootFolder: formData.get('rootFolder'),
                    createSeparateLibraries: formData.get('createSeparateLibraries') === 'on',
                    libraryPrefix: formData.get('libraryPrefix'),
                    excludeFromMainLibraries: formData.get('excludeFromMainLibraries') === 'on',
                    isEnabled: formData.get('isEnabled') === 'on',
                    syncIntervalHours: parseInt(formData.get('syncIntervalHours')),
                    webhookPort: parseInt(formData.get('webhookPort')),
                    userId: parseInt(formData.get('userId')),
                    request4K: formData.get('request4K') === 'on'
                }};
            
            fetch('/Plugins/JellyseerrBridge/Configuration', {{
                method: 'POST',
                headers: {{
                    'Content-Type': 'application/json',
                }},
                body: JSON.stringify(config)
            }})
            .then(response => response.json())
            .then(data => {{
                showStatus('Configuration saved successfully!', 'success');
            }})
            .catch(error => {{
                showStatus('Error saving configuration: ' + error.message, 'error');
            }});
        }}
        
        function testConnection() {{
            fetch('/Plugins/JellyseerrBridge/TestConnection', {{
                method: 'POST'
            }})
            .then(response => response.json())
            .then(data => {{
                if (data.success) {{
                    showStatus('Connection test successful!', 'success');
                }} else {{
                    showStatus('Connection test failed: ' + data.message, 'error');
                }}
            }})
            .catch(error => {{
                showStatus('Error testing connection: ' + error.message, 'error');
            }});
        }}
        
        function triggerSync() {{
            fetch('/Plugins/JellyseerrBridge/Sync', {{
                method: 'POST'
            }})
            .then(response => response.json())
            .then(data => {{
                if (data.success) {{
                    showStatus('Sync triggered successfully!', 'success');
                }} else {{
                    showStatus('Sync failed: ' + data.message, 'error');
                }}
            }})
            .catch(error => {{
                showStatus('Error triggering sync: ' + error.message, 'error');
            }});
        }}
        
        function showStatus(message, type) {{
            const statusDiv = document.getElementById('status');
            statusDiv.innerHTML = '<div class=""status "" + type + "">' + message + '</div>';
            setTimeout(() => {{
                statusDiv.innerHTML = '';
            }}, 5000);
        }}
    </script>
</body>
</html>";
    }
}