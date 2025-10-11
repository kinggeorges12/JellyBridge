using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.JellyseerrBridge.Models;

/// <summary>
/// Generated from TypeScript interface: MovieResult
/// </summary>
public class MovieResult : SearchResult
{

    [JsonPropertyName("title")]
    public string? Title { get; set; } = null;

    [JsonPropertyName("originalTitle")]
    public string? OriginalTitle { get; set; } = null;

    [JsonPropertyName("releaseDate")]
    public string? ReleaseDate { get; set; } = null;

    [JsonPropertyName("adult")]
    public bool Adult { get; set; } = false;

    [JsonPropertyName("video")]
    public bool Video { get; set; } = false;

}
