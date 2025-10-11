using System.Text.Json.Serialization;
using System.Collections.Generic;

namespace Jellyfin.Plugin.JellyseerrBridge.Models;

/// <summary>
/// Generated from TypeScript interface: SearchResult
/// </summary>
public class SearchResult
{

    [JsonPropertyName("id")]
    public int Id { get; set; } = 0;

    [JsonPropertyName("mediaType")]
    public string? MediaType { get; set; } = null;

    [JsonPropertyName("popularity")]
    public double Popularity { get; set; } = 0.0;

    [JsonPropertyName("posterPath")]
    public string? PosterPath { get; set; } = null;

    [JsonPropertyName("backdropPath")]
    public string? BackdropPath { get; set; } = null;

    [JsonPropertyName("voteCount")]
    public int VoteCount { get; set; } = 0;

    [JsonPropertyName("voteAverage")]
    public double VoteAverage { get; set; } = 0.0;

    [JsonPropertyName("genreIds")]
    public List<int> GenreIds { get; set; } = new();

    [JsonPropertyName("overview")]
    public string? Overview { get; set; } = null;

    [JsonPropertyName("originalLanguage")]
    public string? OriginalLanguage { get; set; } = null;

    [JsonPropertyName("mediaInfo")]
    public Media? MediaInfo { get; set; } = null;

}
