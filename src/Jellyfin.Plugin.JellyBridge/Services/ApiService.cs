using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Linq;
using System.Collections;
using Microsoft.Extensions.Logging;
using Jellyfin.Plugin.JellyBridge.Configuration;
using Jellyfin.Plugin.JellyBridge.JellyseerrModel.Api;
using Jellyfin.Plugin.JellyBridge.JellyseerrModel;
using Jellyfin.Plugin.JellyBridge.BridgeModels;
using Jellyfin.Plugin.JellyBridge.Utils;

namespace Jellyfin.Plugin.JellyBridge.Services;

/// <summary>
/// Service for interacting with the Jellyseerr API.
/// </summary>
public class ApiService
{
    /// <summary>
    /// Maximum safe integer value that works for both JavaScript/TypeScript and C# int.
    /// Uses the minimum between Number.MAX_SAFE_INTEGER (2^53 - 1) and int.MaxValue (2^31 - 1).
    /// This is int.MaxValue = 2,147,483,647
    /// </summary>
    public const int MAX_SAFE_INTEGER = int.MaxValue;
    
    /// <summary>
    /// Maximum integer value for pagination (int.MaxValue).
    /// </summary>
    public const int MAX_PAGES = int.MaxValue;

    private readonly HttpClient _httpClient;
    private readonly DebugLogger<ApiService> _logger;

    public ApiService(HttpClient httpClient, ILogger<ApiService> logger)
    {
        _httpClient = httpClient;
        _logger = new DebugLogger<ApiService>(logger);
    }

    /// <summary>
    /// Gets the type for a given endpoint.
    /// </summary>
    /// <param name="endpoint">The endpoint to get the type for</param>
    /// <returns>The type for the endpoint</returns>
    public Type GetReturnTypeForEndpoint(JellyseerrEndpoint endpoint)
    {
        return GetEndpoint(endpoint).ReturnModel;
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
        /// Uses JellyseerrPaginatedResponse&lt;JellyseerrMediaRequest&gt; bridge model (maps to JellyseerrModel.MediaRequest).
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
        /// The expected response model type for this endpoint.
        /// </summary>
        public Type ReturnModel { get; set; } = typeof(object);
        
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
        /// <param name="returnModel">The expected return model type from the CallEndpointAsync method</param>
        /// <param name="method">The HTTP method (defaults to GET)</param>
        /// <param name="isPaginated">Whether this endpoint returns paginated results</param>
        /// <param name="requiresTemplateValues">Whether this endpoint requires template values</param>
        /// <param name="description">Human-readable description</param>
        public JellyseerrEndpointConfig(
            string path,
            Type responseModel,
            Type? returnModel = null,
            HttpMethod? method = null,
            bool isPaginated = false,
            int? maxPages = null,
            bool requiresTemplateValues = false,
            string description = "")
        {
            Path = path;
            ResponseModel = responseModel;
            ReturnModel = returnModel ?? responseModel;
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
        
        // Request endpoints - use paginated response with JellyseerrMediaRequest model
        [JellyseerrEndpoint.ReadRequests] = new JellyseerrEndpointConfig(
            "/api/v1/request",
            typeof(JellyseerrPaginatedResponse<JellyseerrMediaRequest>),
            returnModel: typeof(List<JellyseerrMediaRequest>),
            isPaginated: true,
            description: "Get all requests"
        ),
        [JellyseerrEndpoint.CreateRequest] = new JellyseerrEndpointConfig(
            "/api/v1/request",
            typeof(JellyseerrMediaRequest),
            method: HttpMethod.Post,
            description: "Create a new request"
        ),

        // User endpoints - use paginated response with JellyseerrUser model
        [JellyseerrEndpoint.UserList] = new JellyseerrEndpointConfig(
            "/api/v1/user",
            typeof(JellyseerrPaginatedResponse<JellyseerrUser>),
            returnModel: typeof(List<JellyseerrUser>),
            isPaginated: true,
            description: "Get user list"
        ),
        [JellyseerrEndpoint.UserQuota] = new JellyseerrEndpointConfig(
            "/api/v1/user/{userId}/quota",
            typeof(QuotaResponse),
            returnModel: typeof(QuotaResponse),
            description: "Get quotas for a specific user"
        ),
        
        // Discover endpoints - use paginated response with bridge models
        [JellyseerrEndpoint.DiscoverMovies] = new JellyseerrEndpointConfig(
            "/api/v1/discover/movies",
            typeof(JellyseerrPaginatedResponse<JellyseerrMovie>),
            returnModel: typeof(List<JellyseerrMovie>),
            isPaginated: true,
            description: "Discover movies"
        ),
        [JellyseerrEndpoint.DiscoverTv] = new JellyseerrEndpointConfig(
            "/api/v1/discover/tv",
            typeof(JellyseerrPaginatedResponse<JellyseerrShow>),
            returnModel: typeof(List<JellyseerrShow>),
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
            typeof(List<JellyseerrWatchProviderRegion>),
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
        private static string BuildUrl(string baseUrl, JellyseerrEndpoint endpoint, Dictionary<string, object>? parameters = null, Dictionary<string, string>? templateValues = null)
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
                var queryString = string.Join("&", parameters.Select(kvp => $"{kvp.Key}={Uri.EscapeDataString(kvp.Value?.ToString() ?? "")}"));
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
        public static HttpRequestMessage BuildEndpointRequest(string baseUrl, JellyseerrEndpoint endpoint, string apiKey, HttpMethod? method = null, Dictionary<string, object>? parameters = null, Dictionary<string, string>? templateValues = null)
        {
            var httpMethod = method ?? GetEndpoint(endpoint).Method;
            var isBodyMethod = httpMethod == HttpMethod.Post || httpMethod == HttpMethod.Put;

            // For POST/PUT, use parameters as JSON body and omit from query string
            var url = BuildUrl(baseUrl, endpoint, !isBodyMethod ? parameters : null, templateValues);

            var requestMessage = new HttpRequestMessage(httpMethod, url);
            requestMessage.Headers.Add("X-Api-Key", apiKey);
            requestMessage.Headers.Add("Accept", "application/json");
            
            if (isBodyMethod && parameters != null)
            {
                var json = JsonSerializer.Serialize(parameters);
                requestMessage.Content = new StringContent(json, Encoding.UTF8, "application/json");
            }
            
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
        var timeout = TimeSpan.FromSeconds((double)Plugin.GetConfigOrDefault<int>(nameof(PluginConfiguration.RequestTimeout), config));
        var retryAttempts = Plugin.GetConfigOrDefault<int>(nameof(PluginConfiguration.RetryAttempts), config);
        
        Exception? lastException = null;
        string? content = null;
        int? responseStatusCode = null;
        
        for (int attempt = 1; attempt <= retryAttempts; attempt++)
        {
            try
            {
                // Create a new HttpRequestMessage for each retry attempt since HttpRequestMessage can only be sent once
                using var requestMessage = new HttpRequestMessage(request.Method, request.RequestUri);
                foreach (var header in request.Headers)
                {
                    requestMessage.Headers.TryAddWithoutValidation(header.Key, header.Value);
                }
                if (request.Content != null)
                {
                    requestMessage.Content = request.Content;
                }
                
                _logger.LogTrace("API Request Attempt {Attempt}/{MaxAttempts}: {Method} {Url}", 
                    attempt, retryAttempts, requestMessage.Method, requestMessage.RequestUri);
                
                // Log request body for POST/PUT requests
                if (request.Content != null)
                {
                    var bodyContent = await request.Content.ReadAsStringAsync();
                    _logger.LogTrace("Request Body: {Body}", bodyContent);
                    
                    // Recreate the content since ReadAsStringAsync consumes the stream
                    requestMessage.Content = new StringContent(bodyContent, Encoding.UTF8, "application/json");
                }
                
                // Use the injected HttpClient with timeout for this request
                using var cts = new CancellationTokenSource(timeout);
                
                var response = await _httpClient.SendAsync(requestMessage, cts.Token);
                
                _logger.LogTrace("API Response Attempt {Attempt}: {StatusCode} {ReasonPhrase}", 
                    attempt, response.StatusCode, response.ReasonPhrase);
                
                // Store status code before EnsureSuccessStatusCode throws (for retry catch block)
                responseStatusCode = (int)response.StatusCode;
                
                // EnsureSuccessStatusCode throws HttpRequestException for non-success status codes - cascade it
                response.EnsureSuccessStatusCode();
                
                // Log successful response
                _logger.LogTrace("API Request successful with status: {StatusCode}", response.StatusCode);
                
                // Read the response content
                content = await response.Content.ReadAsStringAsync();
                
                _logger.LogTrace("API Response Content: {Content}", content);
                
                return content;
            }
            catch (TaskCanceledException ex) when (ex.CancellationToken.IsCancellationRequested)
            {
                lastException = ex;
                _logger.LogWarning("API Request Attempt {Attempt}/{MaxAttempts} timed out after {Timeout}s", 
                    attempt, retryAttempts, Plugin.GetConfigOrDefault<int>(nameof(PluginConfiguration.RequestTimeout), config));
                
                if (attempt == retryAttempts)
                {
                    _logger.LogError("All {MaxAttempts} API request attempts timed out", retryAttempts);
                    throw new TimeoutException($"API request timed out after {retryAttempts} attempts", ex);
                }
            }
            catch (HttpRequestException ex)
            {
                lastException = ex;
                // EnsureSuccessStatusCode doesn't include status code in exception, so add it for controller
                // Only set status code from response if we have one (preserve actual HTTP status)
                if (responseStatusCode.HasValue)
                {
                    ex.Data["StatusCode"] = responseStatusCode.Value;
                }
                _logger.LogWarning("API Request Attempt {Attempt}/{MaxAttempts} failed: {Error}", 
                    attempt, retryAttempts, ex.Message);
                
                if (attempt == retryAttempts)
                {
                    _logger.LogError("All {MaxAttempts} API request attempts failed", retryAttempts);
                    throw;
                }
            }
            catch (Exception ex)
            {
                lastException = ex;
                _logger.LogWarning("API Request Attempt {Attempt}/{MaxAttempts} failed with unexpected error: {Error}", 
                    attempt, retryAttempts, ex.Message);
                
                if (attempt == retryAttempts)
                {
                    _logger.LogError("All {MaxAttempts} API request attempts failed with unexpected error", retryAttempts);
                    throw;
                }
            }
            
            // Wait before retry (exponential backoff)
            if (attempt < retryAttempts)
            {
                var delay = TimeSpan.FromSeconds(Math.Min(60, Math.Pow(2, attempt - 1))); // 1s, 2s, 4s, etc. up to 60 seconds
                _logger.LogDebug("Waiting {Delay}s before retry attempt {NextAttempt}", delay.TotalSeconds, attempt + 1);
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
        PluginConfiguration? config = null,
        Dictionary<string, object>? parameters = null,
        Dictionary<string, string>? templates = null
    ) {
        // Use default plugin config if none provided
        config ??= Plugin.GetConfiguration();
        
        string? content = null;
        var operationName = endpoint.ToString();
        try
        {
            // Get endpoint configuration
            var endpointConfig = GetEndpoint(endpoint);
            
            _logger.LogDebug("Making API call to endpoint: {Endpoint}", endpoint);
            
            // Handle endpoint-specific logic
            var (queryParameters, templateValues) = HandleEndpointSpecificLogic(endpoint, config);

            // Merge parameters if they exist, allowing passed parameters to overwrite defaults
            queryParameters = queryParameters?.Concat(parameters ?? Enumerable.Empty<KeyValuePair<string, object>>()).GroupBy(kvp => kvp.Key).ToDictionary(g => g.Key, g => g.Last().Value) ?? parameters;
            templateValues = templateValues?.Concat(templates ?? Enumerable.Empty<KeyValuePair<string, string>>()).GroupBy(kvp => kvp.Key).ToDictionary(g => g.Key, g => g.Last().Value) ?? templates;

            // Handle response based on pagination
            _logger.LogTrace("Endpoint {Endpoint} isPaginated: {IsPaginated}", endpoint, endpointConfig.IsPaginated);
            if (endpointConfig.IsPaginated)
            {
                // Paginated endpoints - fetch all pages starting from page 1
                var maxPages = endpointConfig.MaxPages ?? Plugin.GetConfigOrDefault<int>(nameof(PluginConfiguration.MaxDiscoverPages), config);
                _logger.LogTrace("Initial maxPages value: {MaxPages}", maxPages);
                if (maxPages == 0)
                {
                    maxPages = MAX_PAGES;
                    _logger.LogTrace("Converted maxPages from 0 to {MaxPages}", maxPages);
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
                    _logger.LogDebug("Fetching {Operation} page {Page}", operationName, page);
                    
                    // Add page parameter to query parameters
                    var pageParameters = queryParameters != null 
                        ? new Dictionary<string, object>(queryParameters) { ["page"] = page }
                        : new Dictionary<string, object> { ["page"] = page };
                    // If "take" parameter is present, use query parameters as-is (no page parameter)
                    if (queryParameters != null && queryParameters.ContainsKey("take"))
                    {
                        pageParameters = queryParameters;
                    }
                    
                    var pageRequestMessage = JellyseerrUrlBuilder.BuildEndpointRequest(config.JellyseerrUrl, endpoint, config.ApiKey, parameters: pageParameters, templateValues: templateValues);
                    
                    // Debug: Log the complete URL being used
                    _logger.LogDebug("Making API request to URL: {Url}", pageRequestMessage.RequestUri);
                    
                    content = await MakeApiRequestAsync(pageRequestMessage, config);
                    
                    if (content == null) break;
                    
                    // Log the actual response content for debugging
                    _logger.LogTrace("Raw response content for {Operation} page {Page}: {ResponseContent}", operationName, page, content);
                    
                    // Deserialize paginated response using the response type from endpoint config
                    var responseType = endpointConfig.ResponseModel;
                    _logger.LogTrace("Deserializing {Operation} response as type: {ResponseType}", operationName, responseType.Name);
                    var pageResponse = JsonSerializer.Deserialize(content, responseType);
                    if (pageResponse == null) 
                    {
                        _logger.LogWarning("Failed to deserialize {Operation} response - got null", operationName);
                        break;
                    }
                    
                    _logger.LogTrace("Successfully deserialized {Operation} response, type: {ResponseType}", operationName, pageResponse.GetType().Name);
                    
                    // Extract the Results property directly from the paginated response
                    var resultsProperty = pageResponse.GetType().GetProperty("Results");
                    _logger.LogDebug("Results property found: {HasResults}", resultsProperty != null);
                    
                    if (resultsProperty != null)
                    {
                        var results = resultsProperty.GetValue(pageResponse);
                        _logger.LogTrace("Results value type: {ResultsType}, IsNull: {IsNull}", results?.GetType().Name ?? "null", results == null);
                        
                        if (results is IEnumerable resultsEnumerable)
                        {
                            var itemCount = 0;
                            foreach (var item in resultsEnumerable)
                            {
                                allItems.Add(item);
                                itemCount++;
                            }
                            _logger.LogDebug("Added {Count} items from page {Page}", itemCount, page);
                            
                            // Stop pagination if no items were returned from this page
                            if (itemCount == 0)
                            {
                                _logger.LogDebug("No items returned from page {Page}, stopping pagination", page);
                                break;
                            }
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
                
                _logger.LogDebug("Retrieved {Count} total {Operation} across {Pages} pages", allItems.Count, operationName, page - 1);
                
                // Return the typed list directly
                return allItems ?? GetDefaultReturnValueForEndpoint(endpoint);
            }
            else
            {
                // Non-paginated endpoints - single request
                var requestMessage = JellyseerrUrlBuilder.BuildEndpointRequest(config.JellyseerrUrl, endpoint, config.ApiKey, parameters: queryParameters, templateValues: templateValues);
                _logger.LogDebug("Request URL: {Url}", requestMessage.RequestUri);
                
                // Make the API request
                content = await MakeApiRequestAsync(requestMessage, config);
                
                if (content == null)
                {
                    _logger.LogWarning("No content received for {Operation}", operationName);
                    return GetDefaultReturnValueForEndpoint(endpoint);
                }
                
                // Log the actual response content for debugging
                _logger.LogTrace("Raw response content for {Operation}: {ResponseContent}", operationName, content);
                
                // Deserialize using the response type from endpoint config
                var responseType = endpointConfig.ResponseModel;
                var response = JsonSerializer.Deserialize(content, responseType);
                _logger.LogDebug("Successfully deserialized response for {Operation}", operationName);
                return response ?? GetDefaultReturnValueForEndpoint(endpoint);
            }
        }
        catch (JsonException jsonEx)
        {
            _logger.LogWarning(jsonEx, "Failed to deserialize {Operation} response JSON. Content: {Content}", operationName, content);
            return GetDefaultReturnValueForEndpoint(endpoint);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get {Operation} from Jellyseerr", endpoint);
            return GetDefaultReturnValueForEndpoint(endpoint);
        }
    }

    #endregion

    #region Factory Helper Methods

    /// <summary>
    /// Handles endpoint-specific logic including query parameters and template values.
    /// </summary>
    private (Dictionary<string, object>? queryParameters, Dictionary<string, string>? templateValues) HandleEndpointSpecificLogic(JellyseerrEndpoint endpoint, PluginConfiguration config)
    {
        return endpoint switch
        {
            // CreateRequest endpoint - default parameters for POST body
            JellyseerrEndpoint.CreateRequest => (
                new Dictionary<string, object> 
                {
                    ["mediaType"] = "", //REQUIRED
                    ["mediaId"] = -1, //REQUIRED
                    ["seasons"] = "all", // Accepts an array of season numbers or "all"
                    ["userId"] = 0 //REQUIRED
                }, null
            ),
            // ReadRequests endpoint - no parameters needed for GET requests
            JellyseerrEndpoint.ReadRequests => (
                new Dictionary<string, object> 
                {
                    ["take"] = MAX_SAFE_INTEGER,
                    ["mediaType"] = "all" // Optional: all, movie, tv
                },
                null
            ),

            // Discover endpoints - no parameters needed for GET requests
            JellyseerrEndpoint.DiscoverMovies => (
                new Dictionary<string, object> 
                { 
                    ["sortBy"] = "popularity.desc"
                }, null
            ),
            JellyseerrEndpoint.DiscoverTv => (
                new Dictionary<string, object> 
                { 
                    ["sortBy"] = "popularity.desc"
                }, null
            ),
            // Watch providers endpoints - use region as query parameter
            JellyseerrEndpoint.WatchProvidersMovies => (
                new Dictionary<string, object> 
                { 
                    ["watchRegion"] = Plugin.GetConfigOrDefault<string>(nameof(PluginConfiguration.Region), config) 
                },
                null
            ),
            JellyseerrEndpoint.WatchProvidersTv => (
                new Dictionary<string, object> 
                { 
                    ["watchRegion"] = Plugin.GetConfigOrDefault<string>(nameof(PluginConfiguration.Region), config) 
                },
                null
            ),
            
            // UserList endpoint - use take parameter to get all users
            JellyseerrEndpoint.UserList => (
                new Dictionary<string, object> 
                {
                    ["take"] = MAX_SAFE_INTEGER
                },
                null
            ),
            // User quota endpoint - provide default template parameter userId = 0
            JellyseerrEndpoint.UserQuota => (
                null,
                new Dictionary<string, string>
                {
                    ["userId"] = "0"
                }
            ),
            
            // All other endpoints don't need parameters
            _ => (null, null)
        };
    }

    /// <summary>
    /// Gets default value for endpoint based on expected response type.
    /// </summary>
    private object GetDefaultReturnValueForEndpoint(JellyseerrEndpoint endpoint)
    {
        var endpointConfig = GetEndpoint(endpoint);
        var returnModelType = endpointConfig.ReturnModel;
        
        if (returnModelType.IsGenericType && returnModelType.GetGenericTypeDefinition() == typeof(List<>))
        {
            return Activator.CreateInstance(returnModelType) ?? new List<object>();
        }
        
        return Activator.CreateInstance(returnModelType) ?? new object();
    }

    #endregion

    #region Test Connection

    /// <summary>
    /// Tests basic connectivity to Jellyseerr using the Status endpoint and validates API key with AuthMe endpoint.
    /// Throws exceptions exactly as they come from the backend.
    /// </summary>
    public async Task<SystemStatus> TestConnectionAsync(string? jellyseerUrl = null, string? apiKey = null)
    {
        // Fall back to plugin configuration if not provided
        jellyseerUrl ??= Plugin.GetConfigOrDefault<string>(nameof(PluginConfiguration.JellyseerrUrl));
        apiKey ??= Plugin.GetConfigOrDefault<string>(nameof(PluginConfiguration.ApiKey));
        
        var testConfig = new PluginConfiguration
        {
            JellyseerrUrl = jellyseerUrl,
            ApiKey = apiKey
        };

        // Test 1: Basic connectivity (Status endpoint doesn't require auth)
        _logger.LogInformation("Testing Jellyseerr connectivity");
        var statusRequest = JellyseerrUrlBuilder.BuildEndpointRequest(jellyseerUrl, JellyseerrEndpoint.Status, apiKey);
        // HttpRequestException from MakeApiRequestAsync will cascade with status code preserved
        var statusContent = await MakeApiRequestAsync(statusRequest, testConfig);
        
        var status = JsonSerializer.Deserialize<SystemStatus>(statusContent);
        if (status == null || string.IsNullOrEmpty(status.Version))
        {
            var exception = new HttpRequestException("Jellyseerr service returned invalid response");
            exception.Data["StatusCode"] = 502; // Bad Gateway - invalid response format
            throw exception;
        }
        
        _logger.LogTrace("Connected to Jellyseerr v{Version}", status.Version);

        // Test 2: Validate API key (AuthMe endpoint requires auth)
        _logger.LogTrace("Validating API key");
        var authRequest = JellyseerrUrlBuilder.BuildEndpointRequest(jellyseerUrl, JellyseerrEndpoint.AuthMe, apiKey);
        // HttpRequestException from MakeApiRequestAsync will cascade with status code preserved (e.g., 401)
        var authContent = await MakeApiRequestAsync(authRequest, testConfig);
        
        var userInfo = JsonSerializer.Deserialize<JellyseerrUser>(authContent);
        if (userInfo == null || userInfo.Id == 0)
        {
            var exception = new HttpRequestException("Invalid API key: Authentication failed");
            exception.Data["StatusCode"] = 401; // Unauthorized
            throw exception;
        }
        
        _logger.LogTrace("API key validated successfully");
        
        return status;
    }

    #endregion
}