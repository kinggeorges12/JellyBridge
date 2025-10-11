using System.Text.Json.Serialization;
using System.Collections.Generic;

namespace Jellyfin.Plugin.JellyseerrBridge.Models;

/// <summary>
/// Generated from TypeScript interface: TvResult
/// </summary>
public class TvResult : SearchResult
{

    [JsonPropertyName("name")]
    public string? Name { get; set; } = null;

    [JsonPropertyName("originalName")]
    public string? OriginalName { get; set; } = null;

    [JsonPropertyName("originCountry")]
    public List<string> OriginCountry { get; set; } = new();

    [JsonPropertyName("firstAirDate")]
    public string? FirstAirDate { get; set; } = null;

}
