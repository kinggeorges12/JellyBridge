using System.Text.Json.Serialization;
using Jellyfin.Plugin.JellyseerrBridge.JellyseerrModel;

namespace Jellyfin.Plugin.JellyseerrBridge.BridgeModels;

/// <summary>
/// Bridge model for Jellyseerr watch network that extends the generated TmdbNetwork model.
/// 
/// SOURCE: This extends the generated TmdbNetwork class from JellyseerrModel.TmdbNetwork
/// to include the display_priority field that's present in the actual API response.
/// 
/// ACTUAL API RESPONSE: The TMDB API returns watch provider details with additional fields.
/// 
/// LOCATION: server/routes/index.ts - /watchproviders/movies and /watchproviders/tv endpoints
///           server/api/themoviedb/index.ts - getMovieWatchProviders() and getTvWatchProviders()
///           server/api/themoviedb/interfaces.ts - TmdbWatchProviderDetails interface
/// 
/// The generated TmdbNetwork model has:
/// - id: int
/// - name: string
/// - headquarters?: string
/// - homepage?: string
/// - logo_path?: string
/// - origin_country?: string
/// 
/// But the actual watch provider API response includes:
/// - display_priority?: number (missing from generated model)
/// - provider_id: number (maps to id)
/// - provider_name: string (maps to name)
/// - logo_path?: string
/// 
/// This bridge model inherits from TmdbNetwork and adds the missing display_priority field.
/// </summary>
public class JellyseerrWatchNetwork : TmdbNetwork
{
    [JsonPropertyName("display_priority")]
    public int DisplayPriority { get; set; }
}

