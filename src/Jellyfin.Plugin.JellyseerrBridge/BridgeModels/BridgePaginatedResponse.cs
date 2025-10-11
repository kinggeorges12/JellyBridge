using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.JellyseerrBridge.BridgeModels;

/// <summary>
/// Paginated response wrapper for Jellyseerr API responses.
/// </summary>
public class BridgePaginatedResponse<T>
{
    /// <summary>
    /// Gets or sets the current page number.
    /// </summary>
    [JsonPropertyName("page")]
    public int Page { get; set; }
    
    /// <summary>
    /// Gets or sets the total number of pages.
    /// </summary>
    [JsonPropertyName("totalPages")]
    public int TotalPages { get; set; }
    
    /// <summary>
    /// Gets or sets the total number of results.
    /// </summary>
    [JsonPropertyName("totalResults")]
    public int TotalResults { get; set; }
    
    /// <summary>
    /// Gets or sets the results for the current page.
    /// </summary>
    [JsonPropertyName("results")]
    public List<T> Results { get; set; } = new();
}

/// <summary>
/// Non-generic paginated response for compatibility.
/// </summary>
public class BridgePaginatedResponse
{
    /// <summary>
    /// Gets or sets the current page number.
    /// </summary>
    [JsonPropertyName("page")]
    public int Page { get; set; }
    
    /// <summary>
    /// Gets or sets the total number of pages.
    /// </summary>
    [JsonPropertyName("totalPages")]
    public int TotalPages { get; set; }
    
    /// <summary>
    /// Gets or sets the total number of results.
    /// </summary>
    [JsonPropertyName("totalResults")]
    public int TotalResults { get; set; }
    
    /// <summary>
    /// Gets or sets the results for the current page.
    /// </summary>
    [JsonPropertyName("results")]
    public List<object> Results { get; set; } = new();
}
