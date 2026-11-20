using System.Text.Json;
using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.JellyBridge.BridgeModels;

/// <summary>
/// Seerr's GET /api/v1/search response. Results mix movies, shows and people, so they are kept raw and parsed per mediaType.
/// </summary>
public class JellyseerrSearchResponse
{
    [JsonPropertyName("page")]
    public int Page { get; set; }

    [JsonPropertyName("totalPages")]
    public int TotalPages { get; set; }

    [JsonPropertyName("totalResults")]
    public int TotalResults { get; set; }

    [JsonPropertyName("results")]
    public List<JsonElement> Results { get; set; } = new();
}
