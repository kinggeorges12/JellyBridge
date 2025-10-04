using Jellyfin.Plugin.JellyseerrBridge.Configuration;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace Jellyfin.Plugin.JellyseerrBridge.Services;

/// <summary>
/// Service for interacting with the Jellyseerr API.
/// </summary>
public class JellyseerrApiService
{
    private readonly ILogger<JellyseerrApiService> _logger;
    private readonly HttpClient _httpClient;

    /// <summary>
    /// Initializes a new instance of the <see cref="JellyseerrApiService"/> class.
    /// </summary>
    /// <param name="logger">The logger.</param>
    /// <param name="httpClient">The HTTP client.</param>
    public JellyseerrApiService(ILogger<JellyseerrApiService> logger, HttpClient httpClient)
    {
        _logger = logger;
        _httpClient = httpClient;
    }

    /// <summary>
    /// Tests the connection to Jellyseerr.
    /// </summary>
    /// <param name="configuration">The plugin configuration.</param>
    /// <returns>True if connection is successful.</returns>
    public async Task<bool> TestConnectionAsync(PluginConfiguration configuration)
    {
        try
        {
            _logger.LogInformation("Testing connection to Jellyseerr at {Url}", configuration.JellyseerrUrl);
            
            // Simple health check
            var response = await _httpClient.GetAsync($"{configuration.JellyseerrUrl}/api/v1/status");
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to connect to Jellyseerr");
            return false;
        }
    }

    /// <summary>
    /// Gets shows from Jellyseerr.
    /// </summary>
    /// <param name="configuration">The plugin configuration.</param>
    /// <returns>List of shows.</returns>
    public async Task<List<object>> GetShowsAsync(PluginConfiguration configuration)
    {
        try
        {
            _logger.LogInformation("Fetching shows from Jellyseerr");
            
            // Placeholder implementation
            await Task.Delay(100); // Simulate API call
            return new List<object>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch shows from Jellyseerr");
            return new List<object>();
        }
    }
}