using System.Text.Json.Serialization;
using System.Collections.Generic;

namespace Jellyfin.Plugin.JellyseerrBridge.Models;

/// <summary>
/// Generated from TypeScript interface: MovieDetails
/// </summary>
public class MovieDetails
{

    [JsonPropertyName("id")]
    public int Id { get; set; } = 0;

    [JsonPropertyName("imdbId")]
    public string? ImdbId { get; set; } = null;

    [JsonPropertyName("adult")]
    public bool Adult { get; set; } = false;

    [JsonPropertyName("backdropPath")]
    public string? BackdropPath { get; set; } = null;

    [JsonPropertyName("budget")]
    public int Budget { get; set; } = 0;

    [JsonPropertyName("genres")]
    public List<Genre> Genres { get; set; } = new();

    [JsonPropertyName("homepage")]
    public string? Homepage { get; set; } = null;

    [JsonPropertyName("originalLanguage")]
    public string? OriginalLanguage { get; set; } = null;

    [JsonPropertyName("originalTitle")]
    public string? OriginalTitle { get; set; } = null;

    [JsonPropertyName("overview")]
    public string? Overview { get; set; } = null;

    [JsonPropertyName("popularity")]
    public double Popularity { get; set; } = 0.0;

    [JsonPropertyName("relatedVideos")]
    public List<Video>? RelatedVideos { get; set; } = null;

    [JsonPropertyName("posterPath")]
    public string? PosterPath { get; set; } = null;

    [JsonPropertyName("productionCompanies")]
    public List<ProductionCompany> ProductionCompanies { get; set; } = new();

    [JsonPropertyName("releaseDate")]
    public string? ReleaseDate { get; set; } = null;

    [JsonPropertyName("releases")]
    public string? Releases { get; set; } = null;

    [JsonPropertyName("revenue")]
    public int Revenue { get; set; } = 0;

    [JsonPropertyName("runtime")]
    public int? Runtime { get; set; } = null;

    [JsonPropertyName("status")]
    public string? Status { get; set; } = null;

    [JsonPropertyName("tagline")]
    public string? Tagline { get; set; } = null;

    [JsonPropertyName("title")]
    public string? Title { get; set; } = null;

    [JsonPropertyName("video")]
    public bool Video { get; set; } = false;

    [JsonPropertyName("voteAverage")]
    public double VoteAverage { get; set; } = 0.0;

    [JsonPropertyName("voteCount")]
    public int VoteCount { get; set; } = 0;

    [JsonPropertyName("mediaInfo")]
    public Media? MediaInfo { get; set; } = null;

    [JsonPropertyName("externalIds")]
    public ExternalIds? ExternalIds { get; set; } = null;

    [JsonPropertyName("mediaUrl")]
    public string? MediaUrl { get; set; } = null;

    [JsonPropertyName("watchProviders")]
    public List<WatchProviders>? WatchProviders { get; set; } = null;

    [JsonPropertyName("keywords")]
    public List<Keyword> Keywords { get; set; } = new();

    [JsonPropertyName("onUserWatchlist")]
    public bool? OnUserWatchlist { get; set; } = null;

}
