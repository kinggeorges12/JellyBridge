using System.Text.Json.Serialization;
using Jellyfin.Plugin.JellyseerrBridge.JellyseerrModel;

namespace Jellyfin.Plugin.JellyseerrBridge.BridgeModels;

/// <summary>
/// Bridge model for Jellyseerr watch region that extends the generated Region model.
/// 
/// SOURCE: This extends the generated Region class from JellyseerrModel.Region
/// to include the native_name field that's present in the actual API response.
/// 
/// ACTUAL API RESPONSE: The TMDB API returns regions with iso_3166_1, english_name, and native_name.
/// 
/// LOCATION: server/routes/index.ts - /watchproviders/regions endpoint
///           server/api/themoviedb/index.ts - getAvailableWatchProviderRegions()
///           server/api/themoviedb/interfaces.ts - TmdbWatchProviderRegion interface
/// 
/// The generated Region model has:
/// - iso_3166_1: string
/// - english_name: string  
/// - name?: string (optional)
/// 
/// But the actual API response includes:
/// - iso_3166_1: string
/// - english_name: string
/// - native_name: string (missing from generated model)
/// 
/// This bridge model inherits from Region and adds the missing native_name field.
/// </summary>
public class JellyseerrWatchRegion : Region
{
    [JsonPropertyName("native_name")]
    public string NativeName { get; set; } = string.Empty;
}

