using System.Text.Json.Serialization;
using Jellyfin.Plugin.JellyBridge.JellyseerrModel.Api;
using Jellyfin.Plugin.JellyBridge.JellyseerrModel;

namespace Jellyfin.Plugin.JellyBridge.BridgeModels;

/// <summary>
/// Bridge model for paginated response that matches the Jellyseerr API structure.
/// 
/// ACTUAL API STRUCTURE (from server/routes/user/index.ts):
///   - pageInfo.pages: total number of pages
///   - pageInfo.pageSize: items per page  
///   - pageInfo.results: total number of results
///   - pageInfo.page: current page number
///   - results: array of actual items
/// 
/// This model correctly maps the nested structure returned by Jellyseerr API.
/// </summary>
public class JellyseerrPaginatedResponse<T>
{
    // The actual API structure uses nested pageInfo
    [JsonPropertyName("pageInfo")]
    public PageInfo PageInfo { get; set; } = new();
    
    // Additional fields that some endpoints return
    [JsonPropertyName("keywords")]
    public List<TmdbKeyword> Keywords { get; set; } = new();
    
    // The actual results array
    [JsonPropertyName("results")]
    public List<T> Results { get; set; } = new();
    
    // Convenience properties to maintain compatibility with existing code
    [JsonIgnore]
    public int Page => PageInfo.Page;
    
    [JsonIgnore]
    public int TotalPages => PageInfo.Pages;
    
    [JsonIgnore]
    public int TotalResults => PageInfo.Results;
}
