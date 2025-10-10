using Jellyfin.Plugin.JellyseerrBridge.Configuration;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JellyseerrBridge.Services;

/// <summary>
/// Service that handles Jellyseerr Bridge configuration and data management.
/// This service acts as a bridge between the plugin configuration and the external Jellyseerr API.
/// </summary>
public class JellyseerrBridgeService
{
    private readonly ILogger<JellyseerrBridgeService> _logger;
    private readonly JellyseerrApiService _apiService;

    public JellyseerrBridgeService(
        ILogger<JellyseerrBridgeService> logger,
        JellyseerrApiService apiService)
    {
        _logger = logger;
        _apiService = apiService;
    }

    /// <summary>
    /// Tests the connection to Jellyseerr API.
    /// </summary>
    /// <param name="config">The plugin configuration containing URL and API key.</param>
    /// <returns>True if connection is successful, false otherwise.</returns>
    public async Task<bool> TestConnectionAsync(PluginConfiguration config)
    {
        return await _apiService.TestConnectionAsync(config);
    }

    /// <summary>
    /// Gets watch regions from Jellyseerr.
    /// </summary>
    /// <returns>List of available watch regions.</returns>
    public async Task<List<JellyseerrWatchRegion>> GetWatchRegionsAsync()
    {
        return await _apiService.GetWatchRegionsAsync();
    }

    /// <summary>
    /// Gets watch networks from Jellyseerr for a specific region.
    /// </summary>
    /// <param name="region">The region to get networks for. If null, uses the configured region.</param>
    /// <returns>List of available watch networks.</returns>
    public async Task<List<JellyseerrWatchNetwork>> GetWatchNetworksAsync(string? region = null)
    {
        return await _apiService.GetNetworksAsync(region);
    }

    /// <summary>
    /// Gets all movies from Jellyseerr for all active networks.
    /// </summary>
    /// <returns>List of movies from all configured networks.</returns>
    public async Task<List<JellyseerrMovie>> GetAllMoviesAsync()
    {
        return await _apiService.GetAllMoviesAsync();
    }

    /// <summary>
    /// Gets all TV shows from Jellyseerr for all active networks.
    /// </summary>
    /// <returns>List of TV shows from all configured networks.</returns>
    public async Task<List<JellyseerrTvShow>> GetAllTvShowsAsync()
    {
        return await _apiService.GetAllTvShowsAsync();
    }

    /// <summary>
    /// Gets all requests from Jellyseerr.
    /// </summary>
    /// <returns>List of requests from Jellyseerr.</returns>
    public async Task<List<JellyseerrRequest>> GetRequestsAsync()
    {
        var response = await _apiService.GetRequestsAsync();
        return response?.Results ?? new List<JellyseerrRequest>();
    }

    /// <summary>
    /// Gets user information from Jellyseerr.
    /// </summary>
    /// <param name="config">The plugin configuration containing URL and API key.</param>
    /// <returns>User information or null if not found.</returns>
    public async Task<JellyseerrUser?> GetUserAsync(PluginConfiguration config)
    {
        return await _apiService.GetUserAsync(config);
    }

    /// <summary>
    /// Gets movies from Jellyseerr for specific watch networks.
    /// </summary>
    /// <param name="watchNetworkIds">List of network IDs to get movies for.</param>
    /// <returns>List of movies from the specified networks.</returns>
    public async Task<List<JellyseerrMovie>> GetMoviesAsync(List<int> watchNetworkIds)
    {
        _logger.LogInformation("Making API call to movies endpoint for networks {Networks}", string.Join(",", watchNetworkIds));
        var parameters = new Dictionary<string, string> 
        { 
            { "watchProviders", string.Join("|", watchNetworkIds) }
        };
        var paginatedResponse = await _apiService.MakeTypedApiCallAsync<JellyseerrPaginatedResponse<JellyseerrMovie>>(JellyseerrEndpoint.Movies, parameters: parameters, operationName: "movies");
        return paginatedResponse?.Results ?? new List<JellyseerrMovie>();
    }

    /// <summary>
    /// Gets TV shows from Jellyseerr for specific watch networks.
    /// </summary>
    /// <param name="watchNetworkIds">List of network IDs to get TV shows for.</param>
    /// <returns>List of TV shows from the specified networks.</returns>
    public async Task<List<JellyseerrTvShow>> GetTvShowsAsync(List<int> watchNetworkIds)
    {
        _logger.LogInformation("Making API call to TV shows endpoint for networks {Networks}", string.Join(",", watchNetworkIds));
        
        var parameters = new Dictionary<string, string>
        {
            { "watchProviders", string.Join("|", watchNetworkIds) }
        };
        
        var paginatedResponse = await _apiService.MakeTypedApiCallAsync<JellyseerrPaginatedResponse<JellyseerrTvShow>>(JellyseerrEndpoint.TvShows, parameters: parameters, operationName: "TV shows");
        return paginatedResponse?.Results ?? new List<JellyseerrTvShow>();
    }
}
