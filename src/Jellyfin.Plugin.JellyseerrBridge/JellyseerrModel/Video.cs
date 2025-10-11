using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.JellyseerrBridge.Models;

/// <summary>
/// Generated from TypeScript interface: Video
/// </summary>
public class Video
{

    [JsonPropertyName("url")]
    public string? Url { get; set; } = null;

    [JsonPropertyName("site")]
    public string? Site { get; set; } = null;

    [JsonPropertyName("key")]
    public string? Key { get; set; } = null;

    [JsonPropertyName("name")]
    public string? Name { get; set; } = null;

    [JsonPropertyName("size")]
    public int Size { get; set; } = 0;

}
