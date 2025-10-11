using System.Text.Json.Serialization;
using Jellyfin.Plugin.JellyseerrBridge.Models;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Model.Entities;

namespace Jellyfin.Plugin.JellyseerrBridge.BridgeModels;

/// <summary>
/// Bridge Jellyseerr TV show model with bridge functionality.
/// </summary>
public class BridgeTv : TvResult, IEquatable<BridgeTv>, IEquatable<BaseItem>
{
    /// <summary>
    /// Gets the type of this Jellyseerr object.
    /// </summary>
    public string Type => "TV";

    /// <summary>
    /// Gets or sets the genres for this TV show.
    /// </summary>
    public List<Models.Genre>? Genres { get; set; }

    /// <summary>
    /// Gets or sets the genre IDs (computed property for compatibility).
    /// </summary>
    [JsonIgnore]
    public virtual List<int> GenreIds => Genres?.Select(g => g.Id).ToList() ?? new List<int>();

    /// <summary>
    /// Gets or sets the backdrop path.
    /// </summary>
    public string? BackdropPath { get; set; }

    /// <summary>
    /// Gets or sets the poster path.
    /// </summary>
    public string? PosterPath { get; set; }

    /// <summary>
    /// Gets or sets the vote count.
    /// </summary>
    public int VoteCount { get; set; }

    /// <summary>
    /// Gets or sets the vote average.
    /// </summary>
    public double VoteAverage { get; set; }

    /// <summary>
    /// Gets or sets the original language.
    /// </summary>
    public string? OriginalLanguage { get; set; }

    /// <summary>
    /// Gets or sets the overview.
    /// </summary>
    public string? Overview { get; set; }

    /// <summary>
    /// Gets or sets the popularity.
    /// </summary>
    public double Popularity { get; set; }

    /// <summary>
    /// Gets or sets the first air date.
    /// </summary>
    public string? FirstAirDate { get; set; }

    /// <summary>
    /// Gets or sets the origin country.
    /// </summary>
    public List<string>? OriginCountry { get; set; }

    /// <summary>
    /// Gets or sets the media information.
    /// </summary>
    public Media? MediaInfo { get; set; }

    /// <summary>
    /// Gets or sets the external IDs.
    /// </summary>
    public ExternalIds? ExternalIds { get; set; }

    /// <summary>
    /// Gets or sets the watch providers.
    /// </summary>
    public List<WatchProviders>? WatchProviders { get; set; }

    /// <summary>
    /// Gets or sets the keywords.
    /// </summary>
    public List<Models.Keyword>? Keywords { get; set; }

    /// <summary>
    /// Gets or sets whether this item is on the user's watchlist.
    /// </summary>
    public bool? OnUserWatchlist { get; set; }

    /// <summary>
    /// Gets or sets the media URL.
    /// </summary>
    public string? MediaUrl { get; set; }

    /// <summary>
    /// Gets or sets the service URL.
    /// </summary>
    public string? ServiceUrl { get; set; }

    /// <summary>
    /// Determines whether the specified object is equal to the current object.
    /// </summary>
    /// <param name="other">The object to compare with the current object.</param>
    /// <returns>true if the specified object is equal to the current object; otherwise, false.</returns>
    public bool Equals(BridgeTv? other)
    {
        if (other == null) return false;
        return Id == other.Id && MediaType == other.MediaType;
    }

    /// <summary>
    /// Determines whether the specified BaseItem is equal to the current object.
    /// </summary>
    /// <param name="other">The BaseItem to compare with the current object.</param>
    /// <returns>true if the specified BaseItem is equal to the current object; otherwise, false.</returns>
    public bool Equals(BaseItem? other)
    {
        if (other is not Series series) return false;
        return Id == series.GetProviderId(MetadataProvider.Tmdb);
    }

    /// <summary>
    /// Determines whether the specified object is equal to the current object.
    /// </summary>
    /// <param name="obj">The object to compare with the current object.</param>
    /// <returns>true if the specified object is equal to the current object; otherwise, false.</returns>
    public override bool Equals(object? obj)
    {
        return obj switch
        {
            BridgeTv tv => Equals(tv),
            BaseItem item => Equals(item),
            _ => false
        };
    }

    /// <summary>
    /// Returns a hash code for this instance.
    /// </summary>
    /// <returns>A hash code for this instance.</returns>
    public override int GetHashCode()
    {
        return HashCode.Combine(Id, MediaType);
    }
}
