using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.JellyseerrBridge.Models;

/// <summary>
/// Generated from TypeScript interface: Season
/// </summary>
public class Season
{

    [JsonPropertyName("airDate")]
    public string? AirDate { get; set; } = null;

    [JsonPropertyName("id")]
    public int Id { get; set; } = 0;

    [JsonPropertyName("episodeCount")]
    public int EpisodeCount { get; set; } = 0;

    [JsonPropertyName("name")]
    public string? Name { get; set; } = null;

    [JsonPropertyName("overview")]
    public string? Overview { get; set; } = null;

    [JsonPropertyName("posterPath")]
    public string? PosterPath { get; set; } = null;

    [JsonPropertyName("seasonNumber")]
    public int SeasonNumber { get; set; } = 0;

}
