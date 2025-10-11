using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.JellyseerrBridge.Models;

/// <summary>
/// Generated from TypeScript interface: TvNetwork
/// </summary>
public class TvNetwork
{

    [JsonPropertyName("id")]
    public int Id { get; set; } = 0;

    [JsonPropertyName("logoPath")]
    public string? LogoPath { get; set; } = null;

    [JsonPropertyName("originCountry")]
    public string? OriginCountry { get; set; } = null;

    [JsonPropertyName("name")]
    public string? Name { get; set; } = null;

    [JsonPropertyName("headquarters")]
    public string? Headquarters { get; set; } = null;

    [JsonPropertyName("homepage")]
    public string? Homepage { get; set; } = null;

}
