using System.Text.Json.Serialization;
using Jellyfin.Plugin.JellyseerrBridge.JellyseerrModel.Api;
using Jellyfin.Plugin.JellyseerrBridge.JellyseerrModel;

namespace Jellyfin.Plugin.JellyseerrBridge.BridgeModels;

/// <summary>
/// Bridge model for paginated response that matches the Jellyseerr API structure.
/// 
/// STRUCTURE MISMATCH EXPLANATION:
/// The generated PaginatedResponse uses nested PageInfo structure:
///   - PaginatedResponse.pageInfo.pages
///   - PaginatedResponse.pageInfo.page  
///   - PaginatedResponse.pageInfo.results
///   - PaginatedResponse.pageInfo.pageSize
/// 
/// But the actual API endpoints return a flat structure from TMDB data:
///   - From: server/routes/discover.ts, movie.ts, tv.ts, search.ts
///   - Example: return res.status(200).json({
///       page: data.page,                    // ← Direct from TMDB
///       totalPages: data.total_pages,       // ← TMDB snake_case → camelCase
///       totalResults: data.total_results,  // ← TMDB snake_case → camelCase
///       keywords: keywordData,              // ← Additional field
///       results: data.results.map(...)      // ← Transformed TMDB results
///     });
/// 
/// The TMDB interfaces (TmdbPaginatedResponse) have the correct structure:
///   - From: server/api/themoviedb/interfaces.ts
///   - interface TmdbPaginatedResponse { page, total_results, total_pages }
///   - But these weren't converted to C# models
/// </summary>
public class JellyseerrPaginatedResponse<T>
{
    // Use the generated PageInfo for the nested structure
    [JsonPropertyName("pageInfo")]
    public PageInfo PageInfo { get; set; } = new();
    
    // Additional fields that the API actually returns
    [JsonPropertyName("keywords")]
    public List<TmdbKeyword> Keywords { get; set; } = new();
    
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
