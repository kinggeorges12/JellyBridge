using System.Text.Json.Serialization;
using System.Collections.Generic;

namespace Jellyfin.Plugin.JellyseerrBridge.Models;

/// <summary>
/// Generated from TypeScript interface: TvDetails
/// </summary>
public class TvDetails
{

    [JsonPropertyName("id")]
    public int Id { get; set; } = 0;

    [JsonPropertyName("backdropPath")]
    public string? BackdropPath { get; set; } = null;

    [JsonPropertyName("posterPath")]
    public string? PosterPath { get; set; } = null;

    [JsonPropertyName("contentRatings")]
    public string? ContentRatings { get; set; } = null;

    [JsonPropertyName("episodeRunTime")]
    public List<int> EpisodeRunTime { get; set; } = new();

    [JsonPropertyName("firstAirDate")]
    public string? FirstAirDate { get; set; } = null;

    [JsonPropertyName("genres")]
    public List<Genre> Genres { get; set; } = new();

    [JsonPropertyName("homepage")]
    public string? Homepage { get; set; } = null;

    [JsonPropertyName("inProduction")]
    public bool InProduction { get; set; } = false;

    [JsonPropertyName("relatedVideos")]
    public List<Video>? RelatedVideos { get; set; } = null;

    [JsonPropertyName("languages")]
    public List<string> Languages { get; set; } = new();

    [JsonPropertyName("lastAirDate")]
    public string? LastAirDate { get; set; } = null;

    [JsonPropertyName("lastEpisodeToAir")]
    public Episode? LastEpisodeToAir { get; set; } = null;

    [JsonPropertyName("name")]
    public string? Name { get; set; } = null;

    [JsonPropertyName("nextEpisodeToAir")]
    public Episode? NextEpisodeToAir { get; set; } = null;

    [JsonPropertyName("networks")]
    public List<TvNetwork> Networks { get; set; } = new();

    [JsonPropertyName("numberOfEpisodes")]
    public int NumberOfEpisodes { get; set; } = 0;

    [JsonPropertyName("numberOfSeasons")]
    public int NumberOfSeasons { get; set; } = 0;

    [JsonPropertyName("originCountry")]
    public List<string> OriginCountry { get; set; } = new();

    [JsonPropertyName("originalLanguage")]
    public string? OriginalLanguage { get; set; } = null;

    [JsonPropertyName("originalName")]
    public string? OriginalName { get; set; } = null;

    [JsonPropertyName("overview")]
    public string? Overview { get; set; } = null;

    [JsonPropertyName("popularity")]
    public double Popularity { get; set; } = 0.0;

    [JsonPropertyName("productionCompanies")]
    public List<ProductionCompany> ProductionCompanies { get; set; } = new();

    [JsonPropertyName("spokenLanguages")]
    public List<SpokenLanguage> SpokenLanguages { get; set; } = new();

    [JsonPropertyName("seasons")]
    public List<Season> Seasons { get; set; } = new();

    [JsonPropertyName("status")]
    public string? Status { get; set; } = null;

    [JsonPropertyName("tagline")]
    public string? Tagline { get; set; } = null;

    [JsonPropertyName("type")]
    public string? Type { get; set; } = null;

    [JsonPropertyName("voteAverage")]
    public double VoteAverage { get; set; } = 0.0;

    [JsonPropertyName("voteCount")]
    public int VoteCount { get; set; } = 0;

    [JsonPropertyName("externalIds")]
    public ExternalIds? ExternalIds { get; set; } = null;

    [JsonPropertyName("keywords")]
    public List<Keyword> Keywords { get; set; } = new();

    [JsonPropertyName("mediaInfo")]
    public Media? MediaInfo { get; set; } = null;

    [JsonPropertyName("watchProviders")]
    public List<WatchProviders>? WatchProviders { get; set; } = null;

    [JsonPropertyName("onUserWatchlist")]
    public bool? OnUserWatchlist { get; set; } = null;

}
