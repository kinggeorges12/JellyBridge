using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Jellyfin.Plugin.JellyseerrBridge.Configuration;

namespace Jellyfin.Plugin.JellyseerrBridge.Services;

/// <summary>
/// Service for interacting with the Jellyseerr API.
/// </summary>
public class JellyseerrApiService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<JellyseerrApiService> _logger;

    public JellyseerrApiService(HttpClient httpClient, ILogger<JellyseerrApiService> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    /// <summary>
    /// Test connection to Jellyseerr API.
    /// </summary>
    public async Task<bool> TestConnectionAsync(PluginConfiguration config)
    {
        try
        {
            var statusUrl = $"{config.JellyseerrUrl.TrimEnd('/')}/api/v1/status";
            var requestMessage = new HttpRequestMessage(HttpMethod.Get, statusUrl);
            requestMessage.Headers.Add("X-Api-Key", config.ApiKey);
            
            var response = await _httpClient.SendAsync(requestMessage);
            
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Jellyseerr status check failed with status: {StatusCode}", response.StatusCode);
                return false;
            }

            _logger.LogInformation("Successfully connected to Jellyseerr API");
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to connect to Jellyseerr API");
            return false;
        }
    }

    /// <summary>
    /// Get all requests from Jellyseerr.
    /// </summary>
    public async Task<List<JellyseerrRequest>> GetRequestsAsync(PluginConfiguration config)
    {
        try
        {
            var requestsUrl = $"{config.JellyseerrUrl.TrimEnd('/')}/api/v1/request";
            var requestMessage = new HttpRequestMessage(HttpMethod.Get, requestsUrl);
            requestMessage.Headers.Add("X-Api-Key", config.ApiKey);
            
            var response = await _httpClient.SendAsync(requestMessage);
            
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Failed to get requests with status: {StatusCode}", response.StatusCode);
                return new List<JellyseerrRequest>();
            }

            var content = await response.Content.ReadAsStringAsync();
            
            _logger.LogDebug("[JellyseerrBridge] Requests API Response: {Content}", content);
            
            // Handle empty response
            if (string.IsNullOrWhiteSpace(content))
            {
                _logger.LogInformation("Empty response received for requests");
                return new List<JellyseerrRequest>();
            }

            try
            {
                // First, deserialize as a paginated response
                var paginatedResponse = JsonSerializer.Deserialize<JellyseerrPaginatedResponse>(content, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                var requests = paginatedResponse?.Results ?? new List<JellyseerrRequest>();

                _logger.LogInformation("Retrieved {Count} requests from Jellyseerr", requests.Count);
                return requests;
            }
            catch (JsonException jsonEx)
            {
                _logger.LogWarning(jsonEx, "Failed to deserialize requests JSON. Content: {Content}", content);
                return new List<JellyseerrRequest>();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get requests from Jellyseerr");
            return new List<JellyseerrRequest>();
        }
    }

    /// <summary>
    /// Get all movies from Jellyseerr.
    /// </summary>
    public async Task<List<JellyseerrMovie>> GetMoviesAsync(PluginConfiguration config)
    {
        try
        {
            var moviesUrl = $"{config.JellyseerrUrl.TrimEnd('/')}/api/v1/movie";
            var requestMessage = new HttpRequestMessage(HttpMethod.Get, moviesUrl);
            requestMessage.Headers.Add("X-Api-Key", config.ApiKey);
            
            var response = await _httpClient.SendAsync(requestMessage);
            
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Failed to get movies with status: {StatusCode}", response.StatusCode);
                return new List<JellyseerrMovie>();
            }

            var content = await response.Content.ReadAsStringAsync();
            
            _logger.LogDebug("[JellyseerrBridge] Movies API Response: {Content}", content);
            
            // Handle empty response
            if (string.IsNullOrWhiteSpace(content))
            {
                _logger.LogInformation("Empty response received for movies");
                return new List<JellyseerrMovie>();
            }

            try
            {
                var movies = JsonSerializer.Deserialize<List<JellyseerrMovie>>(content, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                _logger.LogInformation("Retrieved {Count} movies from Jellyseerr", movies?.Count ?? 0);
                return movies ?? new List<JellyseerrMovie>();
            }
            catch (JsonException jsonEx)
            {
                _logger.LogWarning(jsonEx, "Failed to deserialize movies JSON. Content: {Content}", content);
                return new List<JellyseerrMovie>();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get movies from Jellyseerr");
            return new List<JellyseerrMovie>();
        }
    }

    /// <summary>
    /// Get all TV shows from Jellyseerr.
    /// </summary>
    public async Task<List<JellyseerrTvShow>> GetTvShowsAsync(PluginConfiguration config)
    {
        try
        {
            var tvUrl = $"{config.JellyseerrUrl.TrimEnd('/')}/api/v1/tv";
            var requestMessage = new HttpRequestMessage(HttpMethod.Get, tvUrl);
            requestMessage.Headers.Add("X-Api-Key", config.ApiKey);
            
            var response = await _httpClient.SendAsync(requestMessage);
            
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Failed to get TV shows with status: {StatusCode}", response.StatusCode);
                return new List<JellyseerrTvShow>();
            }

            var content = await response.Content.ReadAsStringAsync();
            
            _logger.LogDebug("[JellyseerrBridge] TV Shows API Response: {Content}", content);
            
            // Handle empty response
            if (string.IsNullOrWhiteSpace(content))
            {
                _logger.LogInformation("Empty response received for TV shows");
                return new List<JellyseerrTvShow>();
            }

            try
            {
                var shows = JsonSerializer.Deserialize<List<JellyseerrTvShow>>(content, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                _logger.LogInformation("Retrieved {Count} TV shows from Jellyseerr", shows?.Count ?? 0);
                return shows ?? new List<JellyseerrTvShow>();
            }
            catch (JsonException jsonEx)
            {
                _logger.LogWarning(jsonEx, "Failed to deserialize TV shows JSON. Content: {Content}", content);
                return new List<JellyseerrTvShow>();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get TV shows from Jellyseerr");
            return new List<JellyseerrTvShow>();
        }
    }

    /// <summary>
    /// Get user information.
    /// </summary>
    public async Task<JellyseerrUser?> GetUserAsync(PluginConfiguration config)
    {
        try
        {
            var userUrl = $"{config.JellyseerrUrl.TrimEnd('/')}/api/v1/auth/me";
            var requestMessage = new HttpRequestMessage(HttpMethod.Get, userUrl);
            requestMessage.Headers.Add("X-Api-Key", config.ApiKey);
            
            var response = await _httpClient.SendAsync(requestMessage);
            
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Failed to get user info with status: {StatusCode}", response.StatusCode);
                return null;
            }

            var content = await response.Content.ReadAsStringAsync();
            var user = JsonSerializer.Deserialize<JellyseerrUser>(content, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            _logger.LogInformation("Retrieved user info: {Username}", user?.Username);
            return user;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get user info from Jellyseerr");
            return null;
        }
    }

    /// <summary>
    /// Get watch provider regions from Jellyseerr.
    /// </summary>
    public async Task<List<JellyseerrWatchProviderRegion>> GetWatchProviderRegionsAsync(PluginConfiguration config)
    {
        try
        {
            var regionsUrl = $"{config.JellyseerrUrl.TrimEnd('/')}/api/v1/watchproviders/regions";
            var requestMessage = new HttpRequestMessage(HttpMethod.Get, regionsUrl);
            requestMessage.Headers.Add("X-Api-Key", config.ApiKey);
            
            var response = await _httpClient.SendAsync(requestMessage);
            
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Failed to get watch provider regions with status: {StatusCode}", response.StatusCode);
                return new List<JellyseerrWatchProviderRegion>();
            }

            var content = await response.Content.ReadAsStringAsync();
            
            _logger.LogDebug("[JellyseerrBridge] Watch Provider Regions API Response: {Content}", content);
            
            
            var regions = JsonSerializer.Deserialize<List<JellyseerrWatchProviderRegion>>(content, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = false
            });

            _logger.LogInformation("Retrieved {Count} watch provider regions from Jellyseerr", regions?.Count ?? 0);
            
            // Log first region to see what we got
            if (regions != null && regions.Count > 0)
            {
                var firstRegion = regions[0];
                _logger.LogDebug("[JellyseerrBridge] First region: Iso31661='{Iso31661}', EnglishName='{EnglishName}', NativeName='{NativeName}'", 
                    firstRegion.Iso31661, firstRegion.EnglishName, firstRegion.NativeName);
            }
            
            return regions ?? new List<JellyseerrWatchProviderRegion>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get watch provider regions from Jellyseerr");
            return new List<JellyseerrWatchProviderRegion>();
        }
    }

    public async Task<List<JellyseerrWatchProvider>> GetWatchProvidersAsync(PluginConfiguration config, string region = "US")
    {
        try
        {
            var movieProvidersUrl = $"{config.JellyseerrUrl.TrimEnd('/')}/api/v1/watchproviders/movies?watchRegion={region}";
            var tvProvidersUrl = $"{config.JellyseerrUrl.TrimEnd('/')}/api/v1/watchproviders/tv?watchRegion={region}";
            
            var movieRequest = new HttpRequestMessage(HttpMethod.Get, movieProvidersUrl);
            movieRequest.Headers.Add("X-Api-Key", config.ApiKey);
            
            var tvRequest = new HttpRequestMessage(HttpMethod.Get, tvProvidersUrl);
            tvRequest.Headers.Add("X-Api-Key", config.ApiKey);
            
            // Fetch both movie and TV providers concurrently
            var movieTask = _httpClient.SendAsync(movieRequest);
            var tvTask = _httpClient.SendAsync(tvRequest);
            
            await Task.WhenAll(movieTask, tvTask);
            
            var movieResponse = await movieTask;
            var tvResponse = await tvTask;
            
            var allProviders = new List<JellyseerrWatchProvider>();
            var providerIds = new HashSet<int>(); // To avoid duplicates
            
            // Process movie providers
            if (movieResponse.IsSuccessStatusCode)
            {
                var movieContent = await movieResponse.Content.ReadAsStringAsync();
                _logger.LogDebug("[JellyseerrBridge] Movie Providers API Response: {Content}", movieContent);
                
                var movieProviders = JsonSerializer.Deserialize<List<JellyseerrWatchProvider>>(movieContent, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = false
                });
                
                if (movieProviders != null)
                {
                    foreach (var provider in movieProviders)
                    {
                        if (providerIds.Add(provider.Id)) // Only add if not already present
                        {
                            allProviders.Add(provider);
                        }
                    }
                }
            }
            else
            {
                _logger.LogWarning("Failed to get movie watch providers with status: {StatusCode}", movieResponse.StatusCode);
            }
            
            // Process TV providers
            if (tvResponse.IsSuccessStatusCode)
            {
                var tvContent = await tvResponse.Content.ReadAsStringAsync();
                _logger.LogDebug("[JellyseerrBridge] TV Providers API Response: {Content}", tvContent);
                
                var tvProviders = JsonSerializer.Deserialize<List<JellyseerrWatchProvider>>(tvContent, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = false
                });
                
                if (tvProviders != null)
                {
                    foreach (var provider in tvProviders)
                    {
                        if (providerIds.Add(provider.Id)) // Only add if not already present
                        {
                            allProviders.Add(provider);
                        }
                    }
                }
            }
            else
            {
                _logger.LogWarning("Failed to get TV watch providers with status: {StatusCode}", tvResponse.StatusCode);
            }
            
            // Log combined results
            var preview = allProviders.Count > 0 ? $"Found {allProviders.Count} unique providers" : "No providers found";
            _logger.LogWarning("[JellyseerrBridge] Combined Watch Providers Result: {Preview}", preview);
            
            _logger.LogInformation("Retrieved {Count} combined watch providers for region {Region} from Jellyseerr", allProviders.Count, region);
            
            return allProviders;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get watch providers from Jellyseerr for region {Region}", region);
            return new List<JellyseerrWatchProvider>();
        }
    }
}

/// <summary>
/// Jellyseerr request model.
/// </summary>
public class JellyseerrRequest
{
    public int Id { get; set; }
    public int Status { get; set; }
    public string Type { get; set; } = string.Empty;
    public bool Is4k { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public JellyseerrRequestMedia? Media { get; set; }
    public JellyseerrUser? ModifiedBy { get; set; }
    public JellyseerrUser? RequestedBy { get; set; }
    public int SeasonCount { get; set; }
    public bool CanRemove { get; set; }
}

/// <summary>
/// Jellyseerr request media model.
/// </summary>
public class JellyseerrRequestMedia
{
    public int Id { get; set; }
    public string MediaType { get; set; } = string.Empty;
    public int TmdbId { get; set; }
    public int? TvdbId { get; set; }
    public string? ImdbId { get; set; }
    public int Status { get; set; }
    public int Status4k { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime? MediaAddedAt { get; set; }
    public int ServiceId { get; set; }
    public int? ServiceId4k { get; set; }
    public int ExternalServiceId { get; set; }
    public int? ExternalServiceId4k { get; set; }
    public string ExternalServiceSlug { get; set; } = string.Empty;
    public string? ExternalServiceSlug4k { get; set; }
    public string? RatingKey { get; set; }
    public string? RatingKey4k { get; set; }
    public string? JellyfinMediaId { get; set; }
    public string? JellyfinMediaId4k { get; set; }
    public string? MediaUrl { get; set; }
    public string? ServiceUrl { get; set; }
}

/// <summary>
/// Jellyseerr movie model.
/// </summary>
public class JellyseerrMovie
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Overview { get; set; } = string.Empty;
    public DateTime? ReleaseDate { get; set; }
    public string PosterPath { get; set; } = string.Empty;
    public string BackdropPath { get; set; } = string.Empty;
    public List<int> GenreIds { get; set; } = new();
    public double VoteAverage { get; set; }
    public int VoteCount { get; set; }
    public string Status { get; set; } = string.Empty;
    public bool Available { get; set; }
}

/// <summary>
/// Jellyseerr TV show model.
/// </summary>
public class JellyseerrTvShow
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Overview { get; set; } = string.Empty;
    public DateTime? FirstAirDate { get; set; }
    public string PosterPath { get; set; } = string.Empty;
    public string BackdropPath { get; set; } = string.Empty;
    public List<int> GenreIds { get; set; } = new();
    public double VoteAverage { get; set; }
    public int VoteCount { get; set; }
    public string Status { get; set; } = string.Empty;
    public bool Available { get; set; }
    public List<JellyseerrSeason> Seasons { get; set; } = new();
}

/// <summary>
/// Jellyseerr season model.
/// </summary>
public class JellyseerrSeason
{
    public int Id { get; set; }
    public int SeasonNumber { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Overview { get; set; } = string.Empty;
    public DateTime? AirDate { get; set; }
    public string PosterPath { get; set; } = string.Empty;
    public int EpisodeCount { get; set; }
    public bool Available { get; set; }
}

/// <summary>
/// Jellyseerr user model.
/// </summary>
public class JellyseerrUser
{
    public int Id { get; set; }
    public string Username { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public int UserType { get; set; }
    public int Permissions { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

/// <summary>
/// Jellyseerr watch provider region model.
/// </summary>
public class JellyseerrWatchProviderRegion
{
    [JsonPropertyName("iso_3166_1")]
    public string Iso31661 { get; set; } = string.Empty;
    
    [JsonPropertyName("english_name")]
    public string EnglishName { get; set; } = string.Empty;
    
    [JsonPropertyName("native_name")]
    public string NativeName { get; set; } = string.Empty;
}

/// <summary>
/// Jellyseerr watch provider model.
/// </summary>
public class JellyseerrWatchProvider
{
    [JsonPropertyName("id")]
    public int Id { get; set; }
    
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;
    
    [JsonPropertyName("logoPath")]
    public string LogoPath { get; set; } = string.Empty;
    
    [JsonPropertyName("displayPriority")]
    public int DisplayPriority { get; set; }
}

/// <summary>
/// Jellyseerr paginated response model.
/// </summary>
public class JellyseerrPaginatedResponse
{
    public JellyseerrPageInfo PageInfo { get; set; } = new();
    public List<JellyseerrRequest> Results { get; set; } = new();
}

/// <summary>
/// Jellyseerr page info model.
/// </summary>
public class JellyseerrPageInfo
{
    public int Pages { get; set; }
    public int PageSize { get; set; }
    public int Results { get; set; }
    public int Page { get; set; }
}
