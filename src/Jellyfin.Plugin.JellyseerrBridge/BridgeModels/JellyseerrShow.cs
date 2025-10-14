using System.Text.Json.Serialization;
using Jellyfin.Plugin.JellyseerrBridge.JellyseerrModel.Server;
using Jellyfin.Plugin.JellyseerrBridge.JellyseerrModel.Api;
using Jellyfin.Plugin.JellyseerrBridge.JellyseerrModel;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Model.Entities;

namespace Jellyfin.Plugin.JellyseerrBridge.BridgeModels;

/// <summary>
/// Jellyseerr TV Show that wraps TmdbTvResult with Jellyseerr-specific functionality.
/// 
/// This class provides:
/// - All TMDB TV properties through inheritance
/// - Jellyseerr media info (download status, service IDs, etc.)
/// - Equality comparison with Jellyfin Series items
/// 
/// This bridge model provides the complete structure returned by the discover TV API.
/// </summary>
public class JellyseerrShow 
    : TmdbTvResult, 
      IJellyseerrMedia,
      IEquatable<JellyseerrShow>, 
      IEquatable<Series>
{
    /// <summary>
    /// The display name of the TV show (Name from TMDB).
    /// </summary>
    public new string Name => base.Name;

    /// <summary>
    /// Computed property that extracts the year from the first air date.
    /// </summary>
    public string Year => IJellyseerrMedia.ExtractYear(FirstAirDate);

    /// <summary>
    /// Equality comparison with another JellyseerrShow.
    /// </summary>
    public bool Equals(JellyseerrShow? other)
    {
        if (other is null) return false;
        return Id == other.Id;
    }

    /// <summary>
    /// Equality comparison with a Jellyfin Series item.
    /// </summary>
    public bool Equals(Series? other)
    {
        if (other is null) return false;
        
        // Use TMDB ID for comparison (Id from Jellyseerr discover endpoint is TMDB ID)
        if (!string.IsNullOrEmpty(other.GetProviderId("Tmdb")))
        {
            return Id.ToString() == other.GetProviderId("Tmdb");
        }
        
        return false;
    }

    /// <summary>
    /// Equality comparison with a Jellyfin BaseItem (Series).
    /// </summary>
    public bool Equals(BaseItem? other)
    {
        if (other is null) return false;
        
        // Only compare with Series items
        if (other is not Series series) return false;
        
        return Equals(series);
    }
}
