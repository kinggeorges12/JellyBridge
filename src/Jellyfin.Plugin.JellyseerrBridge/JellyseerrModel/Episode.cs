using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.JellyseerrBridge.Models;

/// <summary>
/// Generated from TypeScript interface: Episode
/// </summary>
public class Episode
{

    [JsonPropertyName("id")]
    public int Id { get; set; } = 0;

    [JsonPropertyName("name")]
    public string? Name { get; set; } = null;

    [JsonPropertyName("airDate")]
    public string? AirDate { get; set; } = null;

    [JsonPropertyName("episodeNumber")]
    public int EpisodeNumber { get; set; } = 0;

    [JsonPropertyName("overview")]
    public string? Overview { get; set; } = null;

    [JsonPropertyName("productionCode")]
    public string? ProductionCode { get; set; } = null;

    [JsonPropertyName("seasonNumber")]
    public int SeasonNumber { get; set; } = 0;

    [JsonPropertyName("showId")]
    public int ShowId { get; set; } = 0;

    [JsonPropertyName("stillPath")]
    public string? StillPath { get; set; } = null;

    [JsonPropertyName("voteAverage")]
    public double VoteAverage { get; set; } = 0.0;

    [JsonPropertyName("voteCount")]
    public int VoteCount { get; set; } = 0;

}
