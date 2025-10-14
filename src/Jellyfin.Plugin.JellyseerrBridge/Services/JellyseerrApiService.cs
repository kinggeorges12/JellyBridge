using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Linq;
using System.Collections;
using Microsoft.Extensions.Logging;
using Jellyfin.Plugin.JellyseerrBridge.Configuration;
using Jellyfin.Plugin.JellyseerrBridge.JellyseerrModel.Api;
using Jellyfin.Plugin.JellyseerrBridge.JellyseerrModel;
using Jellyfin.Plugin.JellyseerrBridge.BridgeModels;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Model.Entities;

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
    /// Response type enumeration for API calls.
    /// Maps to specific JellyseerrModel classes and bridge models.
    /// 
    /// This enum defines how different API responses should be deserialized,
    /// ensuring type safety and proper handling of different response structures.
    /// </summary>
    private enum JellyseerrResponseType
    {
        // Bridge model specific types that map to JellyseerrModel classes
        /// <summary>
        /// Uses JellyseerrPaginatedResponse&lt;T&gt; bridge model for discover endpoints.
        /// </summary>
        DiscoverResponse,
        
        /// <summary>
        /// Uses JellyseerrUser bridge model (maps to JellyseerrModel.Server.User).
        /// </summary>
        UserResponse,
        
        /// <summary>
        /// Uses JellyseerrPaginatedResponse&lt;MediaRequest&gt; bridge model (maps to JellyseerrModel.MediaRequest).
        /// </summary>
        RequestResponse,
        
        /// <summary>
        /// Uses TmdbWatchProviderDetails/TmdbWatchProviderRegion models (maps to JellyseerrModel.TmdbWatchProviderDetails/TmdbWatchProviderRegion).
        /// </summary>
        WatchProviderResponse,
        
        /// <summary>
        /// Uses SystemStatus from JellyseerrModel for status endpoint.
        /// </summary>
        StatusResponse
    }

    /// <summary>
    /// Configuration class for Jellyseerr API endpoints that defines endpoint metadata.
    /// 
    /// This class centralizes endpoint configuration including:
    /// - API path and HTTP method
    /// - Expected request model type (for POST/PUT requests)
    /// - Expected response model type
    /// - Pagination status
    /// - Template value requirements
    /// - Description for documentation
    /// 
    /// Used by JellyseerrEndpointRegistry to provide a data-driven approach
    /// to API endpoint management instead of hardcoded switch statements.
    /// </summary>
    private class JellyseerrEndpointConfig
    {
        /// <summary>
        /// The API endpoint path (e.g., "/api/v1/status").
        /// </summary>
        public string Path { get; set; } = string.Empty;
        
        /// <summary>
        /// The HTTP method for this endpoint (GET, POST, PUT, DELETE).
        /// </summary>
        public HttpMethod Method { get; set; } = HttpMethod.Get;
        
        
        /// <summary>
        /// The expected response model type for this endpoint.
        /// </summary>
        public Type ResponseModel { get; set; } = typeof(object);
        
        /// <summary>
        /// Whether this endpoint returns paginated results.
        /// </summary>
        public bool IsPaginated { get; set; }
        
        /// <summary>
        /// The maximum number of pages to fetch for paginated endpoints.
        /// </summary>
        public int? MaxPages { get; set; }
        
        /// <summary>
        /// Whether this endpoint requires template values (e.g., user ID in path).
        /// </summary>
        public bool RequiresTemplateValues { get; set; }
        
        /// <summary>
        /// Human-readable description of this endpoint.
        /// </summary>
        public string Description { get; set; } = string.Empty;
        
        /// <summary>
        /// Initializes a new instance of JellyseerrEndpointConfig.
        /// </summary>
        /// <param name="path">The API endpoint path</param>
        /// <param name="responseModel">The expected response model type</param>
        /// <param name="method">The HTTP method (defaults to GET)</param>
        /// <param name="isPaginated">Whether this endpoint returns paginated results</param>
        /// <param name="requiresTemplateValues">Whether this endpoint requires template values</param>
        /// <param name="description">Human-readable description</param>
        public JellyseerrEndpointConfig(
            string path, 
            Type responseModel, 
            HttpMethod? method = null,
            bool isPaginated = false, 
            int? maxPages = null,
            bool requiresTemplateValues = false, 
            string description = "")
        {
            Path = path;
            ResponseModel = responseModel;
            Method = method ?? HttpMethod.Get;
            IsPaginated = isPaginated;
            MaxPages = maxPages;
            RequiresTemplateValues = requiresTemplateValues;
            Description = description;
        }
    }

    /// <summary>
    /// Endpoint configuration registry.
    /// </summary>
    private static readonly Dictionary<JellyseerrEndpoint, JellyseerrEndpointConfig> _endpointConfigs = new()
    {
        // Status endpoint - use actual JellyseerrModel.SystemStatus
        [JellyseerrEndpoint.Status] = new JellyseerrEndpointConfig(
            "/api/v1/status",
            typeof(SystemStatus),
            description: "Get Jellyseerr status"
        ),
        
        // Request endpoints - use paginated response with MediaRequest base model
        [JellyseerrEndpoint.Requests] = new JellyseerrEndpointConfig(
            "/api/v1/request",
            typeof(JellyseerrPaginatedResponse<MediaRequest>),
            isPaginated: true,
            description: "Get all requests"
        ),
        [JellyseerrEndpoint.UserList] = new JellyseerrEndpointConfig(
            "/api/v1/user",
            typeof(JellyseerrPaginatedResponse<JellyseerrUser>),
            isPaginated: false,
            description: "Get user list"
        ),
        [JellyseerrEndpoint.UserRequests] = new JellyseerrEndpointConfig(
            "/api/v1/user/{id}/requests",
            typeof(JellyseerrPaginatedResponse<MediaRequest>),
            isPaginated: true,
            requiresTemplateValues: true,
            description: "Get user requests"
        ),
        
        // Discover endpoints - use paginated response with bridge models
        [JellyseerrEndpoint.DiscoverMovies] = new JellyseerrEndpointConfig(
            "/api/v1/discover/movies",
            typeof(JellyseerrPaginatedResponse<JellyseerrMovie>),
            isPaginated: true,
            description: "Discover movies"
        ),
        [JellyseerrEndpoint.DiscoverTv] = new JellyseerrEndpointConfig(
            "/api/v1/discover/tv",
            typeof(JellyseerrPaginatedResponse<JellyseerrShow>),
            isPaginated: true,
            description: "Discover TV shows"
        ),
        
        // Auth endpoints - use user response
        [JellyseerrEndpoint.AuthMe] = new JellyseerrEndpointConfig(
            "/api/v1/auth/me",
            typeof(JellyseerrUser),
            description: "Get current user"
        ),
        
        // Watch provider endpoints - use watch provider response
        [JellyseerrEndpoint.WatchProvidersRegions] = new JellyseerrEndpointConfig(
            "/api/v1/watchproviders/regions",
            typeof(List<TmdbWatchProviderRegion>),
            description: "Get watch provider regions"
        ),
            [JellyseerrEndpoint.WatchProvidersMovies] = new JellyseerrEndpointConfig(
                "/api/v1/watchproviders/movies",
                typeof(List<JellyseerrNetwork>),
                description: "Get movie watch providers"
            ),
            [JellyseerrEndpoint.WatchProvidersTv] = new JellyseerrEndpointConfig(
                "/api/v1/watchproviders/tv",
                typeof(List<JellyseerrNetwork>),
                description: "Get TV watch providers"
            ),
    };

/// <summary>
    /// Private URL builder for Jellyseerr API endpoints.
/// </summary>
    private static class JellyseerrUrlBuilder
{
    /// <summary>
    /// Builds a complete URL for a Jellyseerr API endpoint.
    /// </summary>
    /// <param name="baseUrl">The base Jellyseerr URL</param>
    /// <param name="endpoint">The API endpoint</param>
    /// <param name="parameters">Optional query parameters</param>
    /// <param name="templateValues">Optional template values for URL placeholders</param>
    /// <returns>Complete URL string</returns>
    private static string BuildUrl(string baseUrl, JellyseerrEndpoint endpoint, Dictionary<string, string>? parameters = null, Dictionary<string, string>? templateValues = null)
    {
        var cleanBaseUrl = baseUrl.TrimEnd('/');
        var endpointPath = GetEndpoint(endpoint).Path;
        
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
        /// <param name="templateValues">Optional template values for URL placeholders</param>
    /// <returns>Configured HttpRequestMessage</returns>
    public static HttpRequestMessage CreateRequest(string baseUrl, JellyseerrEndpoint endpoint, string apiKey, HttpMethod? method = null, Dictionary<string, string>? parameters = null, Dictionary<string, string>? templateValues = null)
    {
        var url = BuildUrl(baseUrl, endpoint, parameters, templateValues);
        var requestMessage = new HttpRequestMessage(method ?? HttpMethod.Get, url);
        requestMessage.Headers.Add("X-Api-Key", apiKey);
        return requestMessage;
        }
    }
    
    /// <summary>
    /// Gets the configuration for a given endpoint.
    /// </summary>
    private static JellyseerrEndpointConfig GetEndpoint(JellyseerrEndpoint endpoint)
    {
        return _endpointConfigs.TryGetValue(endpoint, out var config) 
            ? config 
            : new JellyseerrEndpointConfig("/", typeof(object), description: "Unknown endpoint");
    }
    
    /// <summary>
    /// Gets the HTTP method for a given endpoint.
    /// </summary>
    private static HttpMethod GetHttpMethod(JellyseerrEndpoint endpoint)
    {
        return GetEndpoint(endpoint).Method;
    }
    
    
    /// <summary>
    /// Validates that a response model matches the expected type for an endpoint.
    /// </summary>
    private static bool ValidateResponseType<TResponse>(JellyseerrEndpoint endpoint)
    {
        var expectedType = GetEndpoint(endpoint).ResponseModel;
        return typeof(TResponse).IsAssignableFrom(expectedType);
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
        var timeout = TimeSpan.FromSeconds(Plugin.GetConfigOrDefault<int?>(nameof(PluginConfiguration.RequestTimeout), config) ?? 30);
        var retryAttempts = Plugin.GetConfigOrDefault<int?>(nameof(PluginConfiguration.RetryAttempts), config) ?? 3;
        var enableDebugLogging = Plugin.GetConfigOrDefault<bool?>(nameof(PluginConfiguration.EnableDebugLogging), config) ?? false;
        
        Exception? lastException = null;
        string? content = null;
        
        for (int attempt = 1; attempt <= retryAttempts; attempt++)
        {
            try
            {
                if (enableDebugLogging)
                {
                    _logger.LogDebug("[JellyseerrBridge] API Request Attempt {Attempt}/{MaxAttempts}: {Method} {Url}", 
                        attempt, retryAttempts, request.Method, request.RequestUri);
                }
                
                // Use the injected HttpClient with timeout for this request
                using var cts = new CancellationTokenSource(timeout);
                
                var response = await _httpClient.SendAsync(request, cts.Token);
                
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
                content = await response.Content.ReadAsStringAsync();
                
                if (enableDebugLogging)
                {
                    _logger.LogDebug("[JellyseerrBridge] API Response Content: {Content}", content);
                }
                
                return content;
            }
            catch (TaskCanceledException ex) when (ex.CancellationToken.IsCancellationRequested)
            {
                lastException = ex;
                if (enableDebugLogging)
                {
                    _logger.LogWarning("[JellyseerrBridge] API Request Attempt {Attempt}/{MaxAttempts} timed out after {Timeout}s", 
                        attempt, retryAttempts, Plugin.GetConfigOrDefault<int?>(nameof(PluginConfiguration.RequestTimeout), config) ?? 30);
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
                var delay = TimeSpan.FromSeconds(Math.Min(60, Math.Pow(2, attempt - 1))); // 1s, 2s, 4s, etc. up to 60 seconds
                if (enableDebugLogging)
                {
                    _logger.LogDebug("[JellyseerrBridge] Waiting {Delay}s before retry attempt {NextAttempt}", delay.TotalSeconds, attempt + 1);
                }
                await Task.Delay(delay);
            }
        }
        
        throw lastException ?? new InvalidOperationException("All retry attempts failed");
    }

    #region Generic Factory Method - Handles All Endpoints

    /// <summary>
    /// Generic factory method that handles all endpoints uniformly.
    /// Takes endpoint type and optional config, then handles everything internally including endpoint-specific logic.
    /// Returns the correct type directly based on the endpoint - no casting needed by calling functions.
    /// </summary>
    /// <param name="endpoint">The endpoint to call</param>
    /// <param name="config">Optional plugin configuration (uses default if not provided)</param>
    /// <returns>The response in the correct type for the endpoint</returns>
    public async Task<object> CallEndpointAsync(
        JellyseerrEndpoint endpoint, 
        PluginConfiguration? config = null)
    {
        string? content = null;
        var operationName = endpoint.ToString();
        try
        {
            // Use default plugin config if none provided
            config ??= Plugin.GetConfiguration();
            
            // Get endpoint configuration
            var endpointConfig = GetEndpoint(endpoint);
            
            _logger.LogInformation("Making API call to endpoint: {Endpoint}", endpoint);
            
            // Handle endpoint-specific logic
            var (queryParameters, templateValues) = HandleEndpointSpecificLogic(endpoint, config);
            
            // Handle response based on pagination
            _logger.LogInformation("Endpoint {Endpoint} isPaginated: {IsPaginated}", endpoint, endpointConfig.IsPaginated);
            if (endpointConfig.IsPaginated)
            {
                // Paginated endpoints - fetch all pages starting from page 1
                var maxPages = endpointConfig.MaxPages ?? Plugin.GetConfigOrDefault<int?>(nameof(PluginConfiguration.MaxDiscoverPages), config);
                _logger.LogInformation("Initial maxPages value: {MaxPages}", maxPages);
                if (maxPages == 0)
                {
                    maxPages = int.MaxValue;
                    _logger.LogInformation("Converted maxPages from 0 to {MaxPages}", maxPages);
                }
                
                // Get the item type from the paginated response model
                var responseModelType = endpointConfig.ResponseModel;
                var itemType = responseModelType.GetGenericArguments()[0]; // Get T from JellyseerrPaginatedResponse<T>
                var listType = typeof(List<>).MakeGenericType(itemType);
                var allItems = (IList)Activator.CreateInstance(listType)!;
                
                // Use do-while loop to fetch all pages starting from page 1
                int page = 1;
                do
                {
                    _logger.LogInformation("Fetching {Operation} page {Page}", operationName, page);
                    
                    // Add page parameter to query parameters
                    var pageParameters = queryParameters != null 
                        ? new Dictionary<string, string>(queryParameters) { ["page"] = page.ToString() }
                        : new Dictionary<string, string> { ["page"] = page.ToString() };
                    
                    var pageRequestMessage = JellyseerrUrlBuilder.CreateRequest(config.JellyseerrUrl, endpoint, config.ApiKey, parameters: pageParameters, templateValues: templateValues);
                    content = await MakeApiRequestAsync(pageRequestMessage, config);
                    
                    if (content == null) break;
                    
                    // Deserialize paginated response using the response type from endpoint config
                    var responseType = endpointConfig.ResponseModel;
                    _logger.LogInformation("Deserializing {Operation} response as type: {ResponseType}", operationName, responseType.Name);
                    var pageResponse = JsonSerializer.Deserialize(content, responseType);
                    if (pageResponse == null) 
                    {
                        _logger.LogWarning("Failed to deserialize {Operation} response - got null", operationName);
                        break;
                    }
                    
                    _logger.LogInformation("Successfully deserialized {Operation} response, type: {ResponseType}", operationName, pageResponse.GetType().Name);
                    
                    // Extract the Results property directly from the paginated response
                    var resultsProperty = pageResponse.GetType().GetProperty("Results");
                    _logger.LogInformation("Results property found: {HasResults}", resultsProperty != null);
                    
                    if (resultsProperty != null)
                    {
                        var results = resultsProperty.GetValue(pageResponse);
                        _logger.LogInformation("Results value type: {ResultsType}, IsNull: {IsNull}", results?.GetType().Name ?? "null", results == null);
                        
                        if (results is System.Collections.IEnumerable resultsEnumerable)
                        {
                            var itemCount = 0;
                            foreach (var item in resultsEnumerable)
                            {
                                allItems.Add(item);
                                itemCount++;
                            }
                            _logger.LogInformation("Added {Count} items from page {Page}", itemCount, page);
                        }
                        else
                        {
                            _logger.LogWarning("Results is not enumerable. Type: {Type}", results?.GetType().Name ?? "null");
                        }
                    }
                    else
                    {
                        _logger.LogWarning("No Results property found on response type: {ResponseType}", pageResponse.GetType().Name);
                    }
                    
                    page++;
                } while (page <= maxPages);
                
                _logger.LogInformation("Retrieved {Count} total {Operation} across {Pages} pages", allItems.Count, operationName, page - 1);
                
                // Return the typed list directly
                return allItems;
            }
            else
            {
                // Non-paginated endpoints - single request
                var requestMessage = JellyseerrUrlBuilder.CreateRequest(config.JellyseerrUrl, endpoint, config.ApiKey, parameters: queryParameters, templateValues: templateValues);
                _logger.LogInformation("Request URL: {Url}", requestMessage.RequestUri);
                
                // Make the API request
                content = await MakeApiRequestAsync(requestMessage, config);
                
                if (content == null)
                {
                    _logger.LogWarning("No content received for {Operation}", operationName);
                    return GetDefaultValueForEndpoint(endpoint);
                }
                
                // Deserialize using the response type from endpoint config
                var responseType = endpointConfig.ResponseModel;
                var response = JsonSerializer.Deserialize(content, responseType);
                _logger.LogInformation("Successfully deserialized response for {Operation}", operationName);
                return response ?? GetDefaultValueForEndpoint(endpoint);
            }
        }
        catch (JsonException jsonEx)
        {
            _logger.LogWarning(jsonEx, "Failed to deserialize {Operation} response JSON. Content: {Content}", operationName, content);
            return GetDefaultValueForEndpoint(endpoint);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get {Operation} from Jellyseerr", endpoint);
            return GetDefaultValueForEndpoint(endpoint);
        }
    }

    #endregion

    #region Factory Helper Methods

    /// <summary>
    /// Handles endpoint-specific logic including query parameters and template values.
    /// </summary>
    private (Dictionary<string, string>? queryParameters, Dictionary<string, string>? templateValues) HandleEndpointSpecificLogic(JellyseerrEndpoint endpoint, PluginConfiguration config)
    {
        return endpoint switch
        {
            // Discover endpoints - no parameters needed for GET requests
            JellyseerrEndpoint.DiscoverMovies => (null, null),
            JellyseerrEndpoint.DiscoverTv => (null, null),
            
            // Watch providers endpoints - use region as query parameter
            JellyseerrEndpoint.WatchProvidersMovies => (
                new Dictionary<string, string> 
                { 
                    ["watchRegion"] = Plugin.GetConfigOrDefault<string>(nameof(PluginConfiguration.Region), config) 
                },
                null
            ),
            JellyseerrEndpoint.WatchProvidersTv => (
                new Dictionary<string, string> 
                { 
                    ["watchRegion"] = Plugin.GetConfigOrDefault<string>(nameof(PluginConfiguration.Region), config) 
                },
                null
            ),
            
            // User requests endpoint - use user ID as template value for URL path
            JellyseerrEndpoint.UserRequests => (
                null,
                new Dictionary<string, string> 
                { 
                    ["id"] = Plugin.GetConfigOrDefault<int>(nameof(PluginConfiguration.UserId), config).ToString() 
                }
            ),
            
            // All other endpoints don't need parameters
            _ => (null, null)
        };
    }





/// <summary>
    /// Gets default value for endpoint based on expected response type.
/// </summary>
    private object GetDefaultValueForEndpoint(JellyseerrEndpoint endpoint)
    {
        var endpointConfig = GetEndpoint(endpoint);
        var responseModelType = endpointConfig.ResponseModel;
        
        if (responseModelType.IsGenericType && responseModelType.GetGenericTypeDefinition() == typeof(List<>))
        {
            return Activator.CreateInstance(responseModelType) ?? new List<object>();
        }
        
        return Activator.CreateInstance(responseModelType) ?? new object();
}

/// <summary>
    /// Converts items to expected type for endpoint.
/// </summary>
    private object ConvertToExpectedTypeForEndpoint(JellyseerrEndpoint endpoint, List<object> allItems)
    {
        var endpointConfig = GetEndpoint(endpoint);
        var responseModelType = endpointConfig.ResponseModel;

        if (responseModelType.IsGenericType && responseModelType.GetGenericTypeDefinition() == typeof(List<>))
    {
            var itemType = responseModelType.GetGenericArguments()[0];
            var listType = typeof(List<>).MakeGenericType(itemType);
            var result = Activator.CreateInstance(listType);

            foreach (var item in allItems)
        {
                if (item.GetType() == itemType || item.GetType().IsSubclassOf(itemType))
                {
                    listType.GetMethod("Add")?.Invoke(result, new[] { item });
                }
        }

            return result ?? new List<object>();
    }

        return allItems.Count > 0 ? allItems[0] : GetDefaultValueForEndpoint(endpoint);
    }


    /// <summary>
    /// Converts search parameters to query parameters dictionary.
    /// </summary>
    #endregion
}