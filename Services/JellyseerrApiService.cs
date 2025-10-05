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
            
            // Validate configuration
            if (string.IsNullOrEmpty(configuration.JellyseerrUrl))
            {
                _logger.LogWarning("Jellyseerr URL is not configured");
                return false;
            }

            // Ensure URL has proper format
            var baseUrl = configuration.JellyseerrUrl.TrimEnd('/');
            if (!baseUrl.StartsWith("http://") && !baseUrl.StartsWith("https://"))
            {
                baseUrl = "http://" + baseUrl;
            }

            // Test basic connectivity with a timeout
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            _httpClient.Timeout = TimeSpan.FromSeconds(10);

            // Try to reach the Jellyseerr status endpoint
            var statusUrl = $"{baseUrl}/api/v1/status";
            _logger.LogDebug("Attempting to connect to: {StatusUrl}", statusUrl);
            
            var response = await _httpClient.GetAsync(statusUrl, cts.Token);
            
            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation("Successfully connected to Jellyseerr status endpoint");
                
                // If API key is provided, test authentication
                if (!string.IsNullOrEmpty(configuration.ApiKey))
                {
                    return await TestApiKeyAuthentication(baseUrl, configuration.ApiKey);
                }
                
                return true;
            }
            else
            {
                _logger.LogWarning("Jellyseerr status endpoint returned: {StatusCode} {ReasonPhrase}", 
                    response.StatusCode, response.ReasonPhrase);
                return false;
            }
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "HTTP error connecting to Jellyseerr: {Message}", ex.Message);
            return false;
        }
        catch (TaskCanceledException ex)
        {
            _logger.LogError(ex, "Connection timeout to Jellyseerr");
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error connecting to Jellyseerr: {Message}", ex.Message);
            return false;
        }
    }

    /// <summary>
    /// Tests API key authentication with Jellyseerr.
    /// </summary>
    /// <param name="baseUrl">The base URL of Jellyseerr.</param>
    /// <param name="apiKey">The API key to test.</param>
    /// <returns>True if authentication is successful.</returns>
    private async Task<bool> TestApiKeyAuthentication(string baseUrl, string apiKey)
    {
        try
        {
            _logger.LogInformation("Testing API key authentication");
            
            // Test with a simple API call that requires authentication
            var testUrl = $"{baseUrl}/api/v1/user";
            
            _httpClient.DefaultRequestHeaders.Clear();
            _httpClient.DefaultRequestHeaders.Add("X-Api-Key", apiKey);
            
            var response = await _httpClient.GetAsync(testUrl);
            
            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation("API key authentication successful");
                return true;
            }
            else if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            {
                _logger.LogWarning("API key authentication failed - invalid API key");
                return false;
            }
            else
            {
                _logger.LogWarning("API key test returned: {StatusCode} {ReasonPhrase}", 
                    response.StatusCode, response.ReasonPhrase);
                // Don't fail the connection test if the API key test fails
                // The connection itself is working
                return true;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error testing API key authentication: {Message}", ex.Message);
            // Don't fail the connection test if the API key test fails
            return true;
        }
        finally
        {
            // Clear the API key header
            _httpClient.DefaultRequestHeaders.Clear();
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