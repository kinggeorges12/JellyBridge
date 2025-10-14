using System.Text.Json.Serialization;
using Jellyfin.Plugin.JellyseerrBridge.JellyseerrModel;

namespace Jellyfin.Plugin.JellyseerrBridge.BridgeModels;

/// <summary>
/// Bridge model for Jellyseerr networks that maps the actual API response format.
/// Inherits from TmdbWatchProviderDetails but uses the correct JSON property names from Jellyseerr API.
/// </summary>
public class JellyseerrNetwork : TmdbWatchProviderDetails
{
    [JsonPropertyName("id")]
    public int Id => base.ProviderId;

    [JsonPropertyName("name")]
    public string Name => base.ProviderName;
}
