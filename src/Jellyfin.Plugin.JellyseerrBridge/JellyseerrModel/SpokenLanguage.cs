using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.JellyseerrBridge.Models;

/// <summary>
/// Generated from TypeScript interface: SpokenLanguage
/// </summary>
public class SpokenLanguage
{

    [JsonPropertyName("englishName")]
    public string? EnglishName { get; set; } = null;

    [JsonPropertyName("iso_639_1")]
    public string? Iso6391 { get; set; } = null;

    [JsonPropertyName("name")]
    public string? Name { get; set; } = null;

}
