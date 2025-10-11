using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.JellyseerrBridge.Models;

/// <summary>
/// Generated from TypeScript interface: WatchProviderDetails
/// </summary>
public class WatchProviderDetails
{

    [JsonPropertyName("displayPriority")]
    public int? DisplayPriority { get; set; } = null;

    [JsonPropertyName("logoPath")]
    public string? LogoPath { get; set; } = null;

    [JsonPropertyName("id")]
    public int Id { get; set; } = 0;

    [JsonPropertyName("name")]
    public string? Name { get; set; } = null;

}
