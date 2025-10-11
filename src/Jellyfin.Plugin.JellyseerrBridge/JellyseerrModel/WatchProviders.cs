using System.Text.Json.Serialization;
using System.Collections.Generic;

namespace Jellyfin.Plugin.JellyseerrBridge.Models;

/// <summary>
/// Generated from TypeScript interface: WatchProviders
/// </summary>
public class WatchProviders
{

    [JsonPropertyName("iso_3166_1")]
    public string? Iso31661 { get; set; } = null;

    [JsonPropertyName("link")]
    public string? Link { get; set; } = null;

    [JsonPropertyName("buy")]
    public List<WatchProviderDetails>? Buy { get; set; } = null;

    [JsonPropertyName("flatrate")]
    public List<WatchProviderDetails>? Flatrate { get; set; } = null;

}
