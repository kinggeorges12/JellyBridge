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
        
        // Request endpoints - use paginated response with JellyseerrRequest bridge model
        [JellyseerrEndpoint.Requests] = new JellyseerrEndpointConfig(
            "/api/v1/request",
            typeof(JellyseerrPaginatedResponse<JellyseerrRequest>),
            isPaginated: true,
            description: "Get all requests"
        ),
        [JellyseerrEndpoint.UserList] = new JellyseerrEndpointConfig(
            "/api/v1/user",
            typeof(JellyseerrPaginatedResponse<JellyseerrUser>),
            isPaginated: true,
            description: "Get user list"
        ),
        [JellyseerrEndpoint.UserRequests] = new JellyseerrEndpointConfig(
            "/api/v1/user/{id}/requests",
            typeof(JellyseerrPaginatedResponse<JellyseerrRequest>),
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
            typeof(List<JellyseerrWatchRegion>),
            description: "Get watch provider regions"
        ),
        [JellyseerrEndpoint.WatchProvidersMovies] = new JellyseerrEndpointConfig(
            "/api/v1/watchproviders/movies",
            typeof(List<JellyseerrWatchNetwork>),
            description: "Get movie watch providers"
        ),
        [JellyseerrEndpoint.WatchProvidersTv] = new JellyseerrEndpointConfig(
            "/api/v1/watchproviders/tv",
            typeof(List<JellyseerrWatchNetwork>),
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
    private static JellyseerrEndpointConfig GetConfig(JellyseerrEndpoint endpoint)
    {
        return _endpointConfigs.TryGetValue(endpoint, out var config) 
            ? config 
            : new JellyseerrEndpointConfig("/", typeof(object), description: "Unknown endpoint");
    }
    
    /// <summary>
    /// Gets the API path for a given endpoint.
    /// </summary>
    private static string GetEndpointPath(JellyseerrEndpoint endpoint)
    {
        return GetConfig(endpoint).Path;
}

/// <summary>
    /// Gets the response type for a given endpoint.
/// </summary>
    private static JellyseerrResponseType GetResponseType(JellyseerrEndpoint endpoint)
    {
        return endpoint switch
        {
            // Status endpoint
            JellyseerrEndpoint.Status => JellyseerrResponseType.StatusResponse,
            
            // Request endpoints
            JellyseerrEndpoint.Requests => JellyseerrResponseType.RequestResponse,
            JellyseerrEndpoint.UserRequests => JellyseerrResponseType.RequestResponse,
            
            // User endpoints
            JellyseerrEndpoint.UserList => JellyseerrResponseType.UserResponse,
            JellyseerrEndpoint.AuthMe => JellyseerrResponseType.UserResponse,
            
            // Discover endpoints
            JellyseerrEndpoint.DiscoverMovies => JellyseerrResponseType.DiscoverResponse,
            JellyseerrEndpoint.DiscoverTv => JellyseerrResponseType.DiscoverResponse,
            
            // Watch provider endpoints
            JellyseerrEndpoint.WatchProvidersRegions => JellyseerrResponseType.WatchProviderResponse,
            JellyseerrEndpoint.WatchProvidersMovies => JellyseerrResponseType.WatchProviderResponse,
            JellyseerrEndpoint.WatchProvidersTv => JellyseerrResponseType.WatchProviderResponse,
            
            _ => throw new ArgumentException($"Unknown endpoint: {endpoint}")
        };
    }
    
    /// <summary>
    /// Gets the HTTP method for a given endpoint.
    /// </summary>
    private static HttpMethod GetHttpMethod(JellyseerrEndpoint endpoint)
    {
        return GetConfig(endpoint).Method;
    }
    
    /// <summary>
    /// Gets the request model type for a given endpoint.
    /// </summary>
    private static Type? GetRequestModel(JellyseerrEndpoint endpoint)
    {
        return GetConfig(endpoint).RequestModel;
    }
    
    /// <summary>
    /// Validates that a request model matches the expected type for an endpoint.
    /// </summary>
    private static bool ValidateRequestType<TRequest>(JellyseerrEndpoint endpoint)
    {
        var expectedType = GetRequestModel(endpoint);
        return expectedType == null || typeof(TRequest).IsAssignableFrom(expectedType);
    }
    
    /// <summary>
    /// Validates that a response model matches the expected type for an endpoint.
    /// </summary>
    private static bool ValidateResponseType<TResponse>(JellyseerrEndpoint endpoint)
    {
        var expectedType = GetConfig(endpoint).ResponseModel;
        return typeof(TResponse).IsAssignableFrom(expectedType);
    }


    
    /// <summary>
    /// Extracts items from a response based on the endpoint configuration.
    /// Now handles unified JellyseerrPaginatedResponse structure for all endpoints.
    /// </summary>
    private List<object> ExtractItemsFromResponse(object response, JellyseerrEndpointConfig endpointConfig)
    {
        var items = new List<object>();
        
        // Use reflection to extract items from the response
        var responseType = response.GetType();
        
        // Check for paginated response pattern (Results property)
        var resultsProperty = responseType.GetProperty("Results");
        if (resultsProperty != null)
        {
            var results = resultsProperty.GetValue(response);
            if (results is IEnumerable<object> enumerable)
            {
                items.AddRange(enumerable);
            }
        }
        
        // If no Results property, check if the response itself is a list
        if (items.Count == 0 && response is IEnumerable<object> directEnumerable)
        {
            items.AddRange(directEnumerable);
        }
        
        return items;
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
                var content = await response.Content.ReadAsStringAsync();
                
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
        try
        {
            // Use default plugin config if none provided
            config ??= Plugin.GetConfiguration();
            
            // Get endpoint configuration
            var endpointConfig = GetConfig(endpoint);
            var operationName = endpoint.ToString();
            
            _logger.LogInformation("Making API call to endpoint: {Endpoint}", endpoint);
            
            // Handle endpoint-specific logic
            var (requestModel, parameters) = HandleEndpointSpecificLogic(endpoint, config);
            
            // Extract template values from request model
            var templateValues = requestModel?.GetTemplateValues();
            
            // Build the request URL
            var requestMessage = JellyseerrUrlBuilder.CreateRequest(config.JellyseerrUrl, endpoint, config.ApiKey, parameters: parameters, templateValues: templateValues);
            _logger.LogInformation("Request URL: {Url}", requestMessage.RequestUri);
            
            // Make the API request
            var content = await MakeApiRequestAsync(requestMessage, config);
            
            if (content == null)
            {
                _logger.LogWarning("No content received for {Operation}", operationName);
                return GetDefaultValueForEndpoint(endpoint);
            }
            
            // Determine response type and deserialize
            var responseType = GetResponseType(endpoint);
            var response = DeserializeResponseByType(content, responseType, operationName, endpoint);
            
            // Handle pagination if needed
            if (endpointConfig.IsPaginated && response != null)
            {
                var maxPages = Plugin.GetConfigOrDefault<int?>(nameof(PluginConfiguration.MaxDiscoverPages), config) ?? int.MaxValue;
                
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
                    
                    // Add page parameter to request model
                    var pageRequestModel = AddPageToRequestModel(requestModel, page);
                    var pageParameters = ConvertRequestModelToParameters(pageRequestModel);
                    
                    var pageRequestMessage = JellyseerrUrlBuilder.CreateRequest(config.JellyseerrUrl, endpoint, config.ApiKey, parameters: pageParameters, templateValues: templateValues);
                    var pageContent = await MakeApiRequestAsync(pageRequestMessage, config);
                    
                    if (pageContent == null) break;
                    
                    var pageResponse = DeserializeResponseByType(pageContent, responseType, operationName, endpoint);
                    if (pageResponse == null) break;
                    
                    var pageItems = ExtractItemsFromResponse(pageResponse, endpointConfig);
                    if (pageItems.Count == 0) break;
                    
                    // Add items to the typed list
                    foreach (var item in pageItems)
                    {
                        allItems.Add(item);
                    }
                    page++;
                } while (page <= maxPages);
                
                _logger.LogInformation("Retrieved {Count} total {Operation} across {Pages} pages", allItems.Count, operationName, page - 1);
                
                // Return the typed list directly
                return allItems;
            }
            
            return response ?? GetDefaultValueForEndpoint(endpoint);
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
    /// Handles endpoint-specific logic including request model creation and parameters.
    /// Template values are extracted from the request model itself.
    /// </summary>
    private (IJellyseerrRequest? requestModel, Dictionary<string, string>? parameters) HandleEndpointSpecificLogic(JellyseerrEndpoint endpoint, PluginConfiguration config)
    {
        return endpoint switch
        {
            // Discover endpoints - no request model needed for GET requests
            JellyseerrEndpoint.DiscoverMovies => (null, null),
            JellyseerrEndpoint.DiscoverTv => (null, null),
            
            // Watch providers endpoints - use region parameter
            JellyseerrEndpoint.WatchProvidersMovies => (
                null,
                new Dictionary<string, string> { ["watchRegion"] = Plugin.GetConfigOrDefault<string>(nameof(PluginConfiguration.Region), config) ?? "US" }
            ),
            JellyseerrEndpoint.WatchProvidersTv => (
                null,
                new Dictionary<string, string> { ["watchRegion"] = Plugin.GetConfigOrDefault<string>(nameof(PluginConfiguration.Region), config) ?? "US" }
            ),
            
            // User requests endpoint - create request model with user ID
            JellyseerrEndpoint.UserRequests => (
                null,
                new Dictionary<string, string> { ["id"] = (config.UserId ?? 1).ToString() }
            ),
            
            // All other endpoints don't need request models or parameters
            _ => (null, null)
        };
    }


    /// <summary>
    /// Converts request model to parameters dictionary.
    /// </summary>
    private Dictionary<string, string>? ConvertRequestModelToParameters(IJellyseerrRequest? requestModel)
    {
        // No request models currently supported for parameter conversion
        return null;
    }

    /// <summary>
    /// Deserializes response based on response type.
    /// </summary>
    private object? DeserializeResponseByType(string content, JellyseerrResponseType responseType, string operationName, JellyseerrEndpoint endpoint)
    {
        return responseType switch
        {
            // Status endpoint returns a simple SystemStatus object, not paginated
            JellyseerrResponseType.StatusResponse => DeserializeSimpleResponse<SystemStatus>(content, operationName),
        
            // User endpoints return simple JellyseerrUser objects, not paginated
            JellyseerrResponseType.UserResponse => DeserializeSimpleResponse<JellyseerrUser>(content, operationName),
        
            // Watch provider endpoints return simple arrays, not paginated
            JellyseerrResponseType.WatchProviderResponse => DeserializeWatchProviderResponse(content, operationName, endpoint),

            // All other responses are paginated
            _ => DeserializePaginatedResponse<object>(content, operationName)
        };
    }

/// <summary>
    /// Deserializes watch provider responses (simple arrays).
/// </summary>
    private object DeserializeWatchProviderResponse(string content, string operationName, JellyseerrEndpoint endpoint)
    {
        try
        {
            return endpoint switch
            {
                // Regions endpoint returns List<JellyseerrWatchRegion>
                JellyseerrEndpoint.WatchProvidersRegions => DeserializeSimpleResponse<List<JellyseerrWatchRegion>>(content, operationName),
                
                // Movies and TV endpoints return List<JellyseerrWatchNetwork>
                JellyseerrEndpoint.WatchProvidersMovies or JellyseerrEndpoint.WatchProvidersTv => DeserializeSimpleResponse<List<JellyseerrWatchNetwork>>(content, operationName),
                
                // Fallback to List<object> for unknown watch provider endpoints
                _ => DeserializeSimpleResponse<List<object>>(content, operationName)
            };
        }
        catch (JsonException jsonEx)
        {
            _logger.LogWarning(jsonEx, "Failed to deserialize {Operation} watch provider response JSON. Content: {Content}", operationName, content);
            return endpoint switch
            {
                JellyseerrEndpoint.WatchProvidersRegions => new List<JellyseerrWatchRegion>(),
                JellyseerrEndpoint.WatchProvidersMovies or JellyseerrEndpoint.WatchProvidersTv => new List<JellyseerrWatchNetwork>(),
                _ => new List<object>()
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error deserializing {Operation} watch provider response JSON", operationName);
            return endpoint switch
            {
                JellyseerrEndpoint.WatchProvidersRegions => new List<JellyseerrWatchRegion>(),
                JellyseerrEndpoint.WatchProvidersMovies or JellyseerrEndpoint.WatchProvidersTv => new List<JellyseerrWatchNetwork>(),
                _ => new List<object>()
            };
        }
    }

    /// <summary>
    /// Deserializes simple (non-paginated) responses.
    /// </summary>
    private T DeserializeSimpleResponse<T>(string content, string operationName)
    {
        try
        {
            var response = JsonSerializer.Deserialize<T>(content);
            _logger.LogInformation("Successfully deserialized simple response for {Operation}", operationName);
            return response != null ? response : GetDefaultValue<T>();
        }
        catch (JsonException jsonEx)
        {
            _logger.LogWarning(jsonEx, "Failed to deserialize {Operation} simple response JSON. Content: {Content}", operationName, content);
            return GetDefaultValue<T>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error deserializing {Operation} simple response JSON", operationName);
            return GetDefaultValue<T>();
        }
    }

    /// <summary>
    /// Deserializes all responses uniformly using paginated response.
    /// </summary>
    private T DeserializePaginatedResponse<T>(string content, string operationName)
    {
        try
        {
            // All responses should be deserialized as paginated responses
            var paginatedResponse = JsonSerializer.Deserialize<JellyseerrPaginatedResponse<T>>(content);
            
            _logger.LogInformation("Successfully deserialized paginated response for {Operation}", operationName);
            return paginatedResponse != null ? (T)(object)paginatedResponse : GetDefaultValue<T>();
        }
        catch (JsonException jsonEx)
        {
            _logger.LogWarning(jsonEx, "Failed to deserialize {Operation} paginated response JSON. Content: {Content}", operationName, content);
            return GetDefaultValue<T>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error deserializing {Operation} paginated response JSON", operationName);
            return GetDefaultValue<T>();
        }
}

/// <summary>
    /// Gets default value for endpoint based on expected response type.
/// </summary>
    private object GetDefaultValueForEndpoint(JellyseerrEndpoint endpoint)
    {
        var endpointConfig = GetConfig(endpoint);
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
        var endpointConfig = GetConfig(endpoint);
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
    /// Adds page parameter to request model for paginated requests.
    /// </summary>
    private IJellyseerrRequest? AddPageToRequestModel(IJellyseerrRequest? requestModel, int page)
    {
        // No request models currently support pagination
        return requestModel;
    }

    /// <summary>
    /// Converts search parameters to query parameters dictionary.
    /// </summary>
    #endregion
}