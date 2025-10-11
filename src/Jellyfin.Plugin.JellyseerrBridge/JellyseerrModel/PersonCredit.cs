using System.Text.Json.Serialization;
using System.Collections.Generic;

namespace Jellyfin.Plugin.JellyseerrBridge.Models;

/// <summary>
/// Generated from TypeScript interface: PersonCredit
/// </summary>
public class PersonCredit
{

    [JsonPropertyName("id")]
    public int Id { get; set; } = 0;

    [JsonPropertyName("originalLanguage")]
    public string? OriginalLanguage { get; set; } = null;

    [JsonPropertyName("episodeCount")]
    public int EpisodeCount { get; set; } = 0;

    [JsonPropertyName("overview")]
    public string? Overview { get; set; } = null;

    [JsonPropertyName("originCountry")]
    public List<string> OriginCountry { get; set; } = new();

    [JsonPropertyName("originalName")]
    public string? OriginalName { get; set; } = null;

    [JsonPropertyName("voteCount")]
    public int VoteCount { get; set; } = 0;

    [JsonPropertyName("name")]
    public string? Name { get; set; } = null;

    [JsonPropertyName("mediaType")]
    public string? MediaType { get; set; } = null;

    [JsonPropertyName("popularity")]
    public double Popularity { get; set; } = 0.0;

    [JsonPropertyName("creditId")]
    public string? CreditId { get; set; } = null;

    [JsonPropertyName("backdropPath")]
    public string? BackdropPath { get; set; } = null;

    [JsonPropertyName("firstAirDate")]
    public string? FirstAirDate { get; set; } = null;

    [JsonPropertyName("voteAverage")]
    public double VoteAverage { get; set; } = 0.0;

    [JsonPropertyName("genreIds")]
    public List<int>? GenreIds { get; set; } = null;

    [JsonPropertyName("posterPath")]
    public string? PosterPath { get; set; } = null;

    [JsonPropertyName("originalTitle")]
    public string? OriginalTitle { get; set; } = null;

    [JsonPropertyName("video")]
    public bool? Video { get; set; } = null;

    [JsonPropertyName("title")]
    public string? Title { get; set; } = null;

    [JsonPropertyName("adult")]
    public bool Adult { get; set; } = false;

    [JsonPropertyName("releaseDate")]
    public string? ReleaseDate { get; set; } = null;

    [JsonPropertyName("mediaInfo")]
    public Media? MediaInfo { get; set; } = null;

}
