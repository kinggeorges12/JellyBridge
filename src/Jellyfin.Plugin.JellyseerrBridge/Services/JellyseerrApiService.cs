using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Jellyfin.Plugin.JellyseerrBridge.Configuration;

namespace Jellyfin.Plugin.JellyseerrBridge.Services;

/// <summary>
/// Enumeration of Jellyseerr API endpoints.
/// </summary>
public enum JellyseerrEndpoint
{
    Status,
    Requests,
    Movies,
    TvShows,
    User,
    WatchProviderRegions,
    WatchProviderMovies,
    WatchProviderTv
}

/// <summary>
/// Response type enumeration for API calls.
/// </summary>
public enum JellyseerrResponseType
{
    JsonList,
    JsonObject,
    PaginatedList,
    RawContent
}

/// <summary>
/// URL builder for Jellyseerr API endpoints.
/// </summary>
public static class JellyseerrUrlBuilder
{
    /// <summary>
    /// Builds a complete URL for a Jellyseerr API endpoint.
    /// </summary>
    /// <param name="baseUrl">The base Jellyseerr URL</param>
    /// <param name="endpoint">The API endpoint</param>
    /// <param name="parameters">Optional query parameters</param>
    /// <returns>Complete URL string</returns>
    public static string BuildUrl(string baseUrl, JellyseerrEndpoint endpoint, Dictionary<string, string>? parameters = null)
    {
        var cleanBaseUrl = baseUrl.TrimEnd('/');
        var endpointPath = GetEndpointPath(endpoint);
        var url = $"{cleanBaseUrl}{endpointPath}";
        
        if (parameters != null && parameters.Count > 0)
        {
            var queryString = string.Join("&", parameters.Select(kvp => $"{kvp.Key}={kvp.Value}"));
            url = $"{url}?{queryString}";
        }
        
        return url;
    }
    
    /// <summary>
    /// Creates an HTTP request message for a Jellyseerr API endpoint.
    /// </summary>
    /// <param name="baseUrl">The base Jellyseerr URL</param>
    /// <param name="endpoint">The API endpoint</param>
    /// <param name="apiKey">The API key</param>
    /// <param name="method">HTTP method (defaults to GET)</param>
    /// <param name="parameters">Optional query parameters</param>
    /// <returns>Configured HttpRequestMessage</returns>
    public static HttpRequestMessage CreateRequest(string baseUrl, JellyseerrEndpoint endpoint, string apiKey, HttpMethod? method = null, Dictionary<string, string>? parameters = null)
    {
        var url = BuildUrl(baseUrl, endpoint, parameters);
        var requestMessage = new HttpRequestMessage(method ?? HttpMethod.Get, url);
        requestMessage.Headers.Add("X-Api-Key", apiKey);
        return requestMessage;
    }
    
    /// <summary>
    /// Gets the expected response type for a given endpoint.
    /// </summary>
    /// <param name="endpoint">The endpoint enum</param>
    /// <returns>Response type</returns>
    public static JellyseerrResponseType GetResponseType(JellyseerrEndpoint endpoint)
    {
        return endpoint switch
        {
            JellyseerrEndpoint.Status => JellyseerrResponseType.RawContent,
            JellyseerrEndpoint.Requests => JellyseerrResponseType.PaginatedList,
            JellyseerrEndpoint.Movies => JellyseerrResponseType.JsonList,
            JellyseerrEndpoint.TvShows => JellyseerrResponseType.JsonList,
            JellyseerrEndpoint.User => JellyseerrResponseType.JsonObject,
            JellyseerrEndpoint.WatchProviderRegions => JellyseerrResponseType.JsonList,
            JellyseerrEndpoint.WatchProviderMovies => JellyseerrResponseType.JsonList,
            JellyseerrEndpoint.WatchProviderTv => JellyseerrResponseType.JsonList,
            _ => JellyseerrResponseType.RawContent
        };
    }
    
    /// <summary>
    /// Gets the API path for a given endpoint.
    /// </summary>
    /// <param name="endpoint">The endpoint enum</param>
    /// <returns>API path string</returns>
    private static string GetEndpointPath(JellyseerrEndpoint endpoint)
    {
        return endpoint switch
        {
            JellyseerrEndpoint.Status => "/api/v1/status",
            JellyseerrEndpoint.Requests => "/api/v1/request",
            JellyseerrEndpoint.Movies => "/api/v1/movie",
            JellyseerrEndpoint.TvShows => "/api/v1/tv",
            JellyseerrEndpoint.User => "/api/v1/auth/me",
            JellyseerrEndpoint.WatchProviderRegions => "/api/v1/watchproviders/regions",
            JellyseerrEndpoint.WatchProviderMovies => "/api/v1/watchproviders/movies",
            JellyseerrEndpoint.WatchProviderTv => "/api/v1/watchproviders/tv",
            _ => throw new ArgumentException($"Unknown endpoint: {endpoint}")
        };
    }
}

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
    /// Makes a typed API call to Jellyseerr with automatic response handling.
    /// </summary>
    /// <typeparam name="T">The expected return type</typeparam>
    /// <param name="endpoint">The API endpoint</param>
    /// <param name="config">Plugin configuration</param>
    /// <param name="parameters">Optional query parameters</param>
    /// <param name="operationName">Name for logging purposes</param>
    /// <returns>Deserialized response or default value</returns>
    private async Task<T> MakeTypedApiCallAsync<T>(JellyseerrEndpoint endpoint, PluginConfiguration config, Dictionary<string, string>? parameters = null, string operationName = "data")
    {
        try
        {
            var requestMessage = JellyseerrUrlBuilder.CreateRequest(config.JellyseerrUrl, endpoint, config.ApiKey, parameters: parameters);
            var content = await MakeApiRequestAsync(requestMessage, config);
            
            if (content == null)
            {
                return GetDefaultValue<T>();
            }
            
            // Handle empty response
            if (string.IsNullOrWhiteSpace(content))
            {
                _logger.LogInformation("Empty response received for {Operation}", operationName);
                return GetDefaultValue<T>();
            }
            
            var responseType = JellyseerrUrlBuilder.GetResponseType(endpoint);
            
            return responseType switch
            {
                JellyseerrResponseType.JsonList => DeserializeJsonList<T>(content, operationName),
                JellyseerrResponseType.JsonObject => DeserializeJsonObject<T>(content, operationName),
                JellyseerrResponseType.PaginatedList => DeserializePaginatedList<T>(content, operationName),
                JellyseerrResponseType.RawContent => GetDefaultValue<T>(),
                _ => GetDefaultValue<T>()
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get {Operation} from Jellyseerr", operationName);
            return GetDefaultValue<T>();
        }
    }
    
    /// <summary>
    /// Deserializes JSON list response.
    /// </summary>
    private T DeserializeJsonList<T>(string content, string operationName)
    {
        try
        {
            // Special handling for watch provider regions which need case-sensitive deserialization
            var caseInsensitive = operationName != "watch provider regions";
            
            var items = JsonSerializer.Deserialize<List<T>>(content, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = caseInsensitive
            });
            
            _logger.LogInformation("Retrieved {Count} {Operation} from Jellyseerr", items?.Count ?? 0, operationName);
            return (T)(object)(items ?? new List<T>());
        }
        catch (JsonException jsonEx)
        {
            _logger.LogWarning(jsonEx, "Failed to deserialize {Operation} JSON. Content: {Content}", operationName, content);
            return GetDefaultValue<T>();
        }
    }
    
    /// <summary>
    /// Deserializes JSON object response.
    /// </summary>
    private T DeserializeJsonObject<T>(string content, string operationName)
    {
        try
        {
            var item = JsonSerializer.Deserialize<T>(content, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
            
            _logger.LogInformation("Retrieved {Operation} from Jellyseerr", operationName);
            return item ?? GetDefaultValue<T>();
        }
        catch (JsonException jsonEx)
        {
            _logger.LogWarning(jsonEx, "Failed to deserialize {Operation} JSON. Content: {Content}", operationName, content);
            return GetDefaultValue<T>();
        }
    }
    
    /// <summary>
    /// Deserializes paginated list response.
    /// </summary>
    private T DeserializePaginatedList<T>(string content, string operationName)
    {
        try
        {
            // For paginated responses, we need to extract the Results array
            var paginatedResponse = JsonSerializer.Deserialize<JellyseerrPaginatedResponse>(content, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
            
            var items = paginatedResponse?.Results ?? new List<JellyseerrRequest>();
            _logger.LogInformation("Retrieved {Count} {Operation} from Jellyseerr", items.Count, operationName);
            return (T)(object)items;
        }
        catch (JsonException jsonEx)
        {
            _logger.LogWarning(jsonEx, "Failed to deserialize {Operation} JSON. Content: {Content}", operationName, content);
            return GetDefaultValue<T>();
        }
    }
    
    /// <summary>
    /// Gets the default value for type T.
    /// </summary>
    private static T GetDefaultValue<T>()
    {
        if (typeof(T).IsGenericType && typeof(T).GetGenericTypeDefinition() == typeof(List<>))
        {
            return (T)Activator.CreateInstance(typeof(T))!;
        }
        
        return default(T)!;
    }
    
    /// <summary>
    /// Makes an HTTP request to the Jellyseerr API with retry logic, timeout, and debug logging.
    /// Returns the response content as a string, or null if the request failed.
    /// </summary>
    private async Task<string> MakeApiRequestAsync(HttpRequestMessage request, PluginConfiguration config)
    {
        var timeout = TimeSpan.FromSeconds(config.RequestTimeout);
        var retryAttempts = config.RetryAttempts;
        var enableDebugLogging = config.EnableDebugLogging;
        
        Exception? lastException = null;
        
        for (int attempt = 1; attempt <= retryAttempts; attempt++)
        {
            try
            {
                if (enableDebugLogging)
                {
                    _logger.LogDebug("[JellyseerrBridge] API Request Attempt {Attempt}/{MaxAttempts}: {Method} {Url}", 
                        attempt, retryAttempts, request.Method, request.RequestUri);
                }
                
                // Create a new HttpClient with timeout for this request
                using var timeoutClient = new HttpClient();
                timeoutClient.Timeout = timeout;
                
                // Copy headers from the original request
                foreach (var header in request.Headers)
                {
                    timeoutClient.DefaultRequestHeaders.Add(header.Key, header.Value);
                }
                
                var response = await timeoutClient.SendAsync(request);
                
                if (enableDebugLogging)
                {
                    _logger.LogDebug("[JellyseerrBridge] API Response Attempt {Attempt}: {StatusCode} {ReasonPhrase}", 
                        attempt, response.StatusCode, response.ReasonPhrase);
                }
                
                // Check if the response was successful
                if (!response.IsSuccessStatusCode)
                {
                    if (enableDebugLogging)
                    {
                        _logger.LogWarning("[JellyseerrBridge] API Request failed with status: {StatusCode}", response.StatusCode);
                    }
                    return null!;
                }
                
                // Read the response content
                var content = await response.Content.ReadAsStringAsync();
                
                if (enableDebugLogging)
                {
                    _logger.LogDebug("[JellyseerrBridge] API Response Content: {Content}", content);
                }
                
                return content;
            }
            catch (TaskCanceledException ex) when (ex.InnerException is TimeoutException)
            {
                lastException = ex;
                if (enableDebugLogging)
                {
                    _logger.LogWarning("[JellyseerrBridge] API Request Attempt {Attempt}/{MaxAttempts} timed out after {Timeout}s", 
                        attempt, retryAttempts, config.RequestTimeout);
                }
                
                if (attempt == retryAttempts)
                {
                    _logger.LogError("[JellyseerrBridge] All {MaxAttempts} API request attempts timed out", retryAttempts);
                    throw new TimeoutException($"API request timed out after {retryAttempts} attempts", ex);
                }
            }
            catch (HttpRequestException ex)
            {
                lastException = ex;
                if (enableDebugLogging)
                {
                    _logger.LogWarning("[JellyseerrBridge] API Request Attempt {Attempt}/{MaxAttempts} failed: {Error}", 
                        attempt, retryAttempts, ex.Message);
                }
                
                if (attempt == retryAttempts)
                {
                    _logger.LogError("[JellyseerrBridge] All {MaxAttempts} API request attempts failed", retryAttempts);
                    throw;
                }
            }
            catch (Exception ex)
            {
                lastException = ex;
                if (enableDebugLogging)
                {
                    _logger.LogWarning("[JellyseerrBridge] API Request Attempt {Attempt}/{MaxAttempts} failed with unexpected error: {Error}", 
                        attempt, retryAttempts, ex.Message);
                }
                
                if (attempt == retryAttempts)
                {
                    _logger.LogError("[JellyseerrBridge] All {MaxAttempts} API request attempts failed with unexpected error", retryAttempts);
                    throw;
                }
            }
            
            // Wait before retry (exponential backoff)
            if (attempt < retryAttempts)
            {
                var delay = TimeSpan.FromSeconds(Math.Pow(2, attempt - 1)); // 1s, 2s, 4s, etc.
                if (enableDebugLogging)
                {
                    _logger.LogDebug("[JellyseerrBridge] Waiting {Delay}s before retry attempt {NextAttempt}", delay.TotalSeconds, attempt + 1);
                }
                await Task.Delay(delay);
            }
        }
        
        throw lastException ?? new InvalidOperationException("All retry attempts failed");
    }

    /// <summary>
    /// Test connection to Jellyseerr API.
    /// </summary>
    public async Task<bool> TestConnectionAsync(PluginConfiguration config)
    {
        try
        {
            var requestMessage = JellyseerrUrlBuilder.CreateRequest(config.JellyseerrUrl, JellyseerrEndpoint.Status, config.ApiKey);
            var content = await MakeApiRequestAsync(requestMessage, config);
            
            if (content == null)
            {
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
        return await MakeTypedApiCallAsync<List<JellyseerrRequest>>(JellyseerrEndpoint.Requests, config, operationName: "requests");
    }

    /// <summary>
    /// Get all movies from Jellyseerr.
    /// </summary>
    public async Task<List<JellyseerrMovie>> GetMoviesAsync(PluginConfiguration config)
    {
        return await MakeTypedApiCallAsync<List<JellyseerrMovie>>(JellyseerrEndpoint.Movies, config, operationName: "movies");
    }

    /// <summary>
    /// Get all TV shows from Jellyseerr.
    /// </summary>
    public async Task<List<JellyseerrTvShow>> GetTvShowsAsync(PluginConfiguration config)
    {
        return await MakeTypedApiCallAsync<List<JellyseerrTvShow>>(JellyseerrEndpoint.TvShows, config, operationName: "TV shows");
    }

    /// <summary>
    /// Get user information.
    /// </summary>
    public async Task<JellyseerrUser?> GetUserAsync(PluginConfiguration config)
    {
        return await MakeTypedApiCallAsync<JellyseerrUser?>(JellyseerrEndpoint.User, config, operationName: "user info");
    }

    /// <summary>
    /// Get watch provider regions from Jellyseerr.
    /// </summary>
    public async Task<List<JellyseerrWatchProviderRegion>> GetWatchProviderRegionsAsync(PluginConfiguration config)
    {
        var regions = await MakeTypedApiCallAsync<List<JellyseerrWatchProviderRegion>>(JellyseerrEndpoint.WatchProviderRegions, config, operationName: "watch provider regions");
        
        // Log first region to see what we got
        if (regions != null && regions.Count > 0)
        {
            var firstRegion = regions[0];
            _logger.LogDebug("[JellyseerrBridge] First region: Iso31661='{Iso31661}', EnglishName='{EnglishName}', NativeName='{NativeName}'", 
                firstRegion.Iso31661, firstRegion.EnglishName, firstRegion.NativeName);
        }
        
        return regions ?? new List<JellyseerrWatchProviderRegion>();
    }

    public async Task<List<JellyseerrWatchProvider>> GetWatchProvidersAsync(PluginConfiguration config, string region = "US")
    {
        try
        {
            var parameters = new Dictionary<string, string> { { "watchRegion", region } };
            
            // Fetch both movie and TV providers concurrently using the generic method
            var movieTask = MakeTypedApiCallAsync<List<JellyseerrWatchProvider>>(JellyseerrEndpoint.WatchProviderMovies, config, parameters, "movie watch providers");
            var tvTask = MakeTypedApiCallAsync<List<JellyseerrWatchProvider>>(JellyseerrEndpoint.WatchProviderTv, config, parameters, "TV watch providers");
            
            await Task.WhenAll(movieTask, tvTask);
            
            var movieProviders = await movieTask;
            var tvProviders = await tvTask;
            
            var allProviders = new List<JellyseerrWatchProvider>();
            var providerIds = new HashSet<int>(); // To avoid duplicates
            
            // Process movie providers
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
            
            // Process TV providers
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
