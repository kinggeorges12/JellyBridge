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
    public static string BuildUrl(string baseUrl, JellyseerrEndpoint endpoint, Dictionary<string, string>? parameters = null, Dictionary<string, string>? templateValues = null)
    {
        var cleanBaseUrl = baseUrl.TrimEnd('/');
        var endpointPath = GetEndpointPath(endpoint);
        
        // Replace template placeholders in the endpoint path
        if (templateValues != null)
        {
            foreach (var kvp in templateValues)
            {
                var placeholder = $"{{{kvp.Key}}}";
                if (endpointPath.Contains(placeholder))
                {
                    endpointPath = endpointPath.Replace(placeholder, kvp.Value);
                }
            }
        }
        
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
    public static HttpRequestMessage CreateRequest(string baseUrl, JellyseerrEndpoint endpoint, string apiKey, HttpMethod? method = null, Dictionary<string, string>? parameters = null, Dictionary<string, string>? templateValues = null)
    {
        var url = BuildUrl(baseUrl, endpoint, parameters, templateValues);
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
            JellyseerrEndpoint.Movies => JellyseerrResponseType.PaginatedList,
            JellyseerrEndpoint.TvShows => JellyseerrResponseType.PaginatedList,
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
            JellyseerrEndpoint.Movies => "/api/v1/discover/movies",
            JellyseerrEndpoint.TvShows => "/api/v1/discover/tv",
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
    /// <param name="templateValues">Optional template values for URL path placeholders</param>
    /// <param name="operationName">Name for logging purposes</param>
    /// <returns>Deserialized response or default value</returns>
    private async Task<T> MakeTypedApiCallAsync<T>(JellyseerrEndpoint endpoint, PluginConfiguration? config = null, Dictionary<string, string>? parameters = null, Dictionary<string, string>? templateValues = null, string operationName = "data")
    {
        try
        {
            // Use default plugin config if none provided
            config ??= Plugin.Instance.Configuration;
            
            _logger.LogInformation("Making API call for {Operation} to endpoint: {Endpoint}", operationName, endpoint);
            var requestMessage = JellyseerrUrlBuilder.CreateRequest(config.JellyseerrUrl, endpoint, config.ApiKey, parameters: parameters, templateValues: templateValues);
            _logger.LogInformation("Request URL: {Url}", requestMessage.RequestUri);
            
            var content = await MakeApiRequestAsync(requestMessage, config);
            
            if (content == null)
            {
                _logger.LogWarning("Null content received for {Operation}", operationName);
                return GetDefaultValue<T>();
            }
            
            // Handle empty response
            if (string.IsNullOrWhiteSpace(content))
            {
                _logger.LogInformation("Empty response received for {Operation}", operationName);
                return GetDefaultValue<T>();
            }
            
            _logger.LogDebug("Received content for {Operation}, length: {Length}", operationName, content.Length);
            
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
    /// Generic pagination helper that fetches all pages for any endpoint.
    /// </summary>
    private async Task<List<T>> FetchAllPagesAsync<T>(JellyseerrEndpoint endpoint, Dictionary<string, string> baseParameters, string operationName)
    {
        var allItems = new List<T>();
        var config = Plugin.Instance.Configuration;
        var maxPages = config.MaxDiscoverPages > 0 ? config.MaxDiscoverPages : int.MaxValue;

        for (int page = 1; page <= maxPages; page++)
        {
            _logger.LogInformation("Fetching {Operation} page {Page}", operationName, page);
            
            var parameters = new Dictionary<string, string>(baseParameters)
            {
                { "page", page.ToString() }
            };
            
            var paginatedResponse = await MakeTypedApiCallAsync<JellyseerrPaginatedResponse<T>>(endpoint, parameters: parameters, operationName: operationName);
            var items = paginatedResponse?.Results ?? new List<T>();
            
            if (items.Count == 0)
                break;
                
            allItems.AddRange(items);
        }
        
        _logger.LogInformation("Retrieved {Count} total {Operation} across {Pages} pages", allItems.Count, operationName, Math.Min(maxPages, allItems.Count > 0 ? maxPages : 0));
        return allItems;
    }
    
    /// <summary>
    /// Deserializes JSON list response.
    /// </summary>
    private T DeserializeJsonList<T>(string content, string operationName)
    {
        _logger.LogInformation("Attempting to deserialize {Operation} JSON. Content length: {Length}", operationName, content?.Length ?? 0);
        _logger.LogDebug("JSON Content preview (first 200 chars): {Preview}", content?.Length > 200 ? content.Substring(0, 200) + "..." : content);
        
        try
        {
            // Get the element type from List<T>
            var elementType = typeof(T).GetGenericArguments()[0];
            
            // Deserialize as array of the element type
            var arrayType = elementType.MakeArrayType();
            
            if (string.IsNullOrEmpty(content))
            {
                _logger.LogWarning("Empty or null content provided for {Operation}", operationName);
                return default(T)!;
            }
            
            var deserializedArray = JsonSerializer.Deserialize(content, arrayType, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
            
            // Convert array to List<T>
            var listType = typeof(List<>).MakeGenericType(elementType);
            var list = Activator.CreateInstance(listType);
            var addMethod = listType.GetMethod("Add");
            
            if (deserializedArray is Array array)
            {
                foreach (var item in array)
                {
                    addMethod?.Invoke(list, new[] { item });
                }
            }
            
            _logger.LogInformation("Successfully deserialized {Count} {Operation} items", ((System.Collections.ICollection)list!).Count, operationName);
            
            if (((System.Collections.ICollection)list!).Count > 0)
            {
                var firstItem = ((System.Collections.IList)list)[0];
                _logger.LogDebug("First item type: {Type}, First item: {FirstItem}", elementType.Name, firstItem);
            }
            
            return (T)list!;
        }
        catch (JsonException jsonEx)
        {
            _logger.LogError(jsonEx, "JSON deserialization failed for {Operation}. Error: {Error}", operationName, jsonEx.Message);
            _logger.LogError("JSON Path: {Path}, Line: {Line}, Position: {Position}", jsonEx.Path, jsonEx.LineNumber, jsonEx.BytePositionInLine);
            _logger.LogError("Full JSON content: {Content}", content);
            return GetDefaultValue<T>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error during {Operation} deserialization: {Error}", operationName, ex.Message);
            _logger.LogError("Full JSON content: {Content}", content);
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
            // For paginated responses, we need to deserialize the full paginated response
            var paginatedResponse = JsonSerializer.Deserialize<T>(content, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
            
            _logger.LogInformation("Successfully deserialized paginated response for {Operation}", operationName);
            return paginatedResponse ?? GetDefaultValue<T>();
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
                
                // Log successful response
                if (enableDebugLogging)
                {
                    _logger.LogDebug("[JellyseerrBridge] API Request successful with status: {StatusCode}", response.StatusCode);
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
    public async Task<List<JellyseerrRequest>> GetRequestsAsync()
    {
        return await MakeTypedApiCallAsync<List<JellyseerrRequest>>(JellyseerrEndpoint.Requests, operationName: "requests");
    }

    /// <summary>
    /// Get watch providers/networks from Jellyseerr for a specific region.
    /// </summary>
    public async Task<List<JellyseerrWatchProvider>> GetNetworksAsync(string? region = null)
    {
        region ??= Plugin.Instance.Configuration.Region;
        _logger.LogInformation("Making API call to get networks for region {Region}", region);
        var parameters = new Dictionary<string, string> 
        { 
            { "watchRegion", region }
        };
        
        // Get both movie and TV providers and combine them
        var movieTask = MakeTypedApiCallAsync<List<JellyseerrWatchProvider>>(JellyseerrEndpoint.WatchProviderMovies, parameters: parameters, operationName: "movie watch providers");
        var tvTask = MakeTypedApiCallAsync<List<JellyseerrWatchProvider>>(JellyseerrEndpoint.WatchProviderTv, parameters: parameters, operationName: "TV watch providers");
        
        await Task.WhenAll(movieTask, tvTask);
        var movieProviders = await movieTask;
        var tvProviders = await tvTask;
        
        // Combine and deduplicate providers
        var allProviders = new List<JellyseerrWatchProvider>();
        var seenIds = new HashSet<int>();
        
        foreach (var provider in movieProviders.Concat(tvProviders))
        {
            if (seenIds.Add(provider.Id))
            {
                allProviders.Add(provider);
            }
        }
        
        _logger.LogInformation("Retrieved {Count} unique networks for region {Region}", allProviders.Count, region);
        return allProviders;
    }

    /// <summary>
    /// Get movies from Jellyseerr for specific watch providers (single page only).
    /// </summary>
    public async Task<List<JellyseerrMovie>> GetMoviesAsync(List<int> watchProviderIds)
    {
        _logger.LogInformation("Making API call to movies endpoint for providers {Providers}", string.Join(",", watchProviderIds));
        var parameters = new Dictionary<string, string> 
        { 
            { "watchProviders", string.Join("|", watchProviderIds) }
        };
        var paginatedResponse = await MakeTypedApiCallAsync<JellyseerrPaginatedResponse<JellyseerrMovie>>(JellyseerrEndpoint.Movies, parameters: parameters, operationName: "movies");
        return paginatedResponse?.Results ?? new List<JellyseerrMovie>();
    }


    /// <summary>
    /// Get all movies from Jellyseerr for all active networks (handles pagination automatically).
    /// </summary>
    public async Task<List<JellyseerrMovie>> GetAllMoviesAsync()
    {
        var allMovies = new List<JellyseerrMovie>();
        var config = Plugin.Instance.Configuration;
        
        // Get network name-to-ID mapping
        var networkDict = config.GetNetworkMapDictionary();
        
        // Loop through each active network
        foreach (var networkName in networkDict.Keys)
        {
            if (networkDict.TryGetValue(networkName, out var networkId))
            {
                _logger.LogInformation("Fetching movies for network: {NetworkName} (ID: {NetworkId})", networkName, networkId);
                
                var baseParameters = new Dictionary<string, string>
                {
                    { "watchProviders", networkId.ToString() }
                };
                
                var networkMovies = await FetchAllPagesAsync<JellyseerrMovie>(
                    JellyseerrEndpoint.Movies,
                    baseParameters,
                    $"movies for {networkName}"
                );
                
                allMovies.AddRange(networkMovies);
                _logger.LogInformation("Retrieved {MovieCount} movies for {NetworkName}", networkMovies.Count, networkName);
            }
            else
            {
                _logger.LogWarning("Network '{NetworkName}' not found in available networks", networkName);
            }
        }
        
        return allMovies;
    }


    /// <summary>
    /// Get TV shows from Jellyseerr for specific watch providers (single page only).
    /// </summary>
    public async Task<List<JellyseerrTvShow>> GetTvShowsAsync(List<int> watchProviderIds)
    {
        _logger.LogInformation("Making API call to TV shows endpoint for providers {Providers}", string.Join(",", watchProviderIds));
        
        var parameters = new Dictionary<string, string>
        {
            { "watchProviders", string.Join("|", watchProviderIds) }
        };
        
        var paginatedResponse = await MakeTypedApiCallAsync<JellyseerrPaginatedResponse<JellyseerrTvShow>>(JellyseerrEndpoint.TvShows, parameters: parameters, operationName: "TV shows");
        return paginatedResponse?.Results ?? new List<JellyseerrTvShow>();
    }


    /// <summary>
    /// Get all TV shows from Jellyseerr for all active networks (handles pagination automatically).
    /// </summary>
    public async Task<List<JellyseerrTvShow>> GetAllTvShowsAsync()
    {
        var allTvShows = new List<JellyseerrTvShow>();
        var config = Plugin.Instance.Configuration;
        
        // Get network name-to-ID mapping
        var networkDict = config.GetNetworkMapDictionary();
        
        // Loop through each active network
        foreach (var networkName in networkDict.Keys)
        {
            if (networkDict.TryGetValue(networkName, out var networkId))
            {
                _logger.LogInformation("Fetching TV shows for network: {NetworkName} (ID: {NetworkId})", networkName, networkId);
                
                var baseParameters = new Dictionary<string, string>
                {
                    { "watchProviders", networkId.ToString() }
                };
                
                var networkTvShows = await FetchAllPagesAsync<JellyseerrTvShow>(
                    JellyseerrEndpoint.TvShows,
                    baseParameters,
                    $"TV shows for {networkName}"
                );
                
                allTvShows.AddRange(networkTvShows);
                _logger.LogInformation("Retrieved {TvShowCount} TV shows for {NetworkName}", networkTvShows.Count, networkName);
            }
            else
            {
                _logger.LogWarning("Network '{NetworkName}' not found in available networks", networkName);
            }
        }
        
        return allTvShows;
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
    public async Task<List<JellyseerrWatchProviderRegion>> GetWatchProviderRegionsAsync()
    {
        var regions = await MakeTypedApiCallAsync<List<JellyseerrWatchProviderRegion>>(JellyseerrEndpoint.WatchProviderRegions, operationName: "watch provider regions");
        
        // Log first region to see what we got
        if (regions != null && regions.Count > 0)
        {
            var firstRegion = regions[0];
            _logger.LogDebug("[JellyseerrBridge] First region: Iso31661='{Iso31661}', EnglishName='{EnglishName}', NativeName='{NativeName}'", 
                firstRegion.Iso31661, firstRegion.EnglishName, firstRegion.NativeName);
        }
        
        return regions ?? new List<JellyseerrWatchProviderRegion>();
    }

    public async Task<List<JellyseerrWatchProvider>> GetWatchProvidersAsync(string region = "US")
    {
        try
        {
            _logger.LogInformation("Starting watch providers fetch for region: {Region}", region);
            var parameters = new Dictionary<string, string> { { "watchRegion", region } };
            
            // Fetch both movie and TV providers concurrently using the generic method
            _logger.LogDebug("Making concurrent API calls for movie and TV watch providers");
            var movieTask = MakeTypedApiCallAsync<List<JellyseerrWatchProvider>>(JellyseerrEndpoint.WatchProviderMovies, parameters: parameters, operationName: "movie watch providers");
            var tvTask = MakeTypedApiCallAsync<List<JellyseerrWatchProvider>>(JellyseerrEndpoint.WatchProviderTv, parameters: parameters, operationName: "TV watch providers");
            
            await Task.WhenAll(movieTask, tvTask);
            
            var movieProviders = await movieTask;
            var tvProviders = await tvTask;
            
            _logger.LogInformation("Movie providers count: {MovieCount}, TV providers count: {TvCount}", 
                movieProviders?.Count ?? 0, tvProviders?.Count ?? 0);
            
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
/// Paginated response wrapper for Jellyseerr API responses.
/// </summary>
public class JellyseerrPaginatedResponse<T>
{
    [JsonPropertyName("page")]
    public int Page { get; set; }
    
    [JsonPropertyName("totalPages")]
    public int TotalPages { get; set; }
    
    [JsonPropertyName("totalResults")]
    public int TotalResults { get; set; }
    
    [JsonPropertyName("results")]
    public List<T> Results { get; set; } = new();
}

/// <summary>
/// Jellyseerr movie model.
/// </summary>
public class JellyseerrMovie
{
    [JsonPropertyName("id")]
    public int Id { get; set; }
    
    [JsonPropertyName("mediaType")]
    public string MediaType { get; set; } = string.Empty;
    
    [JsonPropertyName("adult")]
    public bool Adult { get; set; }
    
    [JsonPropertyName("genreIds")]
    public List<int> GenreIds { get; set; } = new();
    
    [JsonPropertyName("originalLanguage")]
    public string OriginalLanguage { get; set; } = string.Empty;
    
    [JsonPropertyName("originalTitle")]
    public string OriginalTitle { get; set; } = string.Empty;
    
    [JsonPropertyName("overview")]
    public string Overview { get; set; } = string.Empty;
    
    [JsonPropertyName("popularity")]
    public double Popularity { get; set; }
    
    [JsonPropertyName("releaseDate")]
    public string ReleaseDate { get; set; } = string.Empty;
    
    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;
    
    [JsonPropertyName("video")]
    public bool Video { get; set; }
    
    [JsonPropertyName("voteAverage")]
    public double VoteAverage { get; set; }
    
    [JsonPropertyName("voteCount")]
    public int VoteCount { get; set; }
    
    [JsonPropertyName("backdropPath")]
    public string BackdropPath { get; set; } = string.Empty;
    
    [JsonPropertyName("posterPath")]
    public string PosterPath { get; set; } = string.Empty;
    
    [JsonPropertyName("mediaInfo")]
    public JellyseerrMediaInfo? MediaInfo { get; set; }
}

/// <summary>
/// Jellyseerr TV show model.
/// </summary>
public class JellyseerrTvShow
{
    [JsonPropertyName("id")]
    public int Id { get; set; }
    
    [JsonPropertyName("firstAirDate")]
    public string FirstAirDate { get; set; } = string.Empty;
    
    [JsonPropertyName("genreIds")]
    public List<int> GenreIds { get; set; } = new();
    
    [JsonPropertyName("mediaType")]
    public string MediaType { get; set; } = string.Empty;
    
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;
    
    [JsonPropertyName("originCountry")]
    public List<string> OriginCountry { get; set; } = new();
    
    [JsonPropertyName("originalLanguage")]
    public string OriginalLanguage { get; set; } = string.Empty;
    
    [JsonPropertyName("originalName")]
    public string OriginalName { get; set; } = string.Empty;
    
    [JsonPropertyName("overview")]
    public string Overview { get; set; } = string.Empty;
    
    [JsonPropertyName("popularity")]
    public double Popularity { get; set; }
    
    [JsonPropertyName("voteAverage")]
    public double VoteAverage { get; set; }
    
    [JsonPropertyName("voteCount")]
    public int VoteCount { get; set; }
    
    [JsonPropertyName("backdropPath")]
    public string BackdropPath { get; set; } = string.Empty;
    
    [JsonPropertyName("posterPath")]
    public string PosterPath { get; set; } = string.Empty;
    
    [JsonPropertyName("mediaInfo")]
    public JellyseerrMediaInfo? MediaInfo { get; set; }
}

/// <summary>
/// Jellyseerr media info model for additional metadata.
/// </summary>
public class JellyseerrMediaInfo
{
    public List<object> DownloadStatus { get; set; } = new();
    public List<object> DownloadStatus4k { get; set; } = new();
    public int Id { get; set; }
    public string MediaType { get; set; } = string.Empty;
    public int TmdbId { get; set; }
    public int? TvdbId { get; set; }
    public string? ImdbId { get; set; }
    public int Status { get; set; }
    public int Status4k { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime LastSeasonChange { get; set; }
    public DateTime MediaAddedAt { get; set; }
    public int? ServiceId { get; set; }
    public int? ServiceId4k { get; set; }
    public int? ExternalServiceId { get; set; }
    public int? ExternalServiceId4k { get; set; }
    public string? ExternalServiceSlug { get; set; }
    public string? ExternalServiceSlug4k { get; set; }
    public string? RatingKey { get; set; }
    public string? RatingKey4k { get; set; }
    public string? JellyfinMediaId { get; set; }
    public string? JellyfinMediaId4k { get; set; }
    public List<object> Watchlists { get; set; } = new();
    public string? MediaUrl { get; set; }
    public string? ServiceUrl { get; set; }
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
    public int Id { get; set; }
    
    public string Name { get; set; } = string.Empty;
    
    public string LogoPath { get; set; } = string.Empty;
    
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
