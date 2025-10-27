using Jellyfin.Plugin.JellyBridge.JellyseerrModel;

namespace Jellyfin.Plugin.JellyBridge.BridgeModels;

/// <summary>
/// Bridge model for TMDB watch provider regions that inherits from the base model.
/// This provides a clean separation between generated models and bridge models
/// without duplicating any properties or JsonPropertyName attributes.
/// </summary>
public class JellyseerrWatchProviderRegion : TmdbWatchProviderRegion
{
    // Inherits all properties and JsonPropertyName attributes from TmdbWatchProviderRegion:
    // - Iso31661 with [JsonPropertyName("iso_3166_1")]
    // - EnglishName with [JsonPropertyName("english_name")]  
    // - NativeName with [JsonPropertyName("native_name")]
    
    // No additional properties needed - just inherits everything from base class
}
