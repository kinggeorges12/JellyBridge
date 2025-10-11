using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.JellyseerrBridge.Models;

/// <summary>
/// Generated from TypeScript interface: Keyword
/// </summary>
public class Keyword
{

    [JsonPropertyName("id")]
    public int Id { get; set; } = 0;

    [JsonPropertyName("name")]
    public string? Name { get; set; } = null;

}
