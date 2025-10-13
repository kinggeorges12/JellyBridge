using System.Net.Http;

namespace Jellyfin.Plugin.JellyseerrBridge.BridgeModels;

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
public class JellyseerrEndpointConfig
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
    /// The expected request model type for this endpoint (for POST/PUT requests).
    /// </summary>
    public Type? RequestModel { get; set; }
    
    /// <summary>
    /// The expected response model type for this endpoint.
    /// </summary>
    public Type ResponseModel { get; set; } = typeof(object);
    
    /// <summary>
    /// Whether this endpoint returns paginated results.
    /// </summary>
    public bool IsPaginated { get; set; }
    
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
    /// <param name="requestModel">The expected request model type (for POST/PUT requests)</param>
    /// <param name="isPaginated">Whether this endpoint returns paginated results</param>
    /// <param name="requiresTemplateValues">Whether this endpoint requires template values</param>
    /// <param name="description">Human-readable description</param>
    public JellyseerrEndpointConfig(
        string path, 
        Type responseModel, 
        HttpMethod? method = null, 
        Type? requestModel = null,
        bool isPaginated = false, 
        bool requiresTemplateValues = false, 
        string description = "")
    {
        Path = path;
        ResponseModel = responseModel;
        Method = method ?? HttpMethod.Get;
        RequestModel = requestModel;
        IsPaginated = isPaginated;
        RequiresTemplateValues = requiresTemplateValues;
        Description = description;
    }
}
