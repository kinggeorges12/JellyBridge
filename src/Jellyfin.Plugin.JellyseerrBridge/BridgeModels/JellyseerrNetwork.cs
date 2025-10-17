using System.Text.Json;
using System.Text.Json.Serialization;
using Jellyfin.Plugin.JellyseerrBridge.JellyseerrModel;
using Jellyfin.Plugin.JellyseerrBridge.Utils;

namespace Jellyfin.Plugin.JellyseerrBridge.BridgeModels;

/// <summary>
/// Bridge model for Jellyseerr networks that maps the actual API response format.
/// Inherits from TmdbWatchProviderDetails but uses the correct JSON property names from Jellyseerr API.
/// </summary>
public class JellyseerrNetwork : TmdbWatchProviderDetails
{
    [JsonPropertyName("id")]
    public int Id
    { 
        get => ProviderId;
        set => ProviderId = value;
    }

    [JsonPropertyName("name")]
    public string Name
    { 
        get => ProviderName;
        set => ProviderName = value;
    }

    // override DisplayPriority to use the correct JSON property name
    [JsonPropertyName("displayPriority")]
    public int? _DisplayPriority
    { 
        get => DisplayPriority;
        set => DisplayPriority = value;
    }

    [JsonPropertyName("country")]
    public string Country { get; set; } = string.Empty;
}
