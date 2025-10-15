using System;
using System.Text.Json.Serialization;
using System.Collections.Generic;
using Jellyfin.Plugin.JellyseerrBridge.JellyseerrModel;

namespace Jellyfin.Plugin.JellyseerrBridge.JellyseerrModel.Server;

public class Video
{
    [JsonPropertyName("url")]
    public string? Url { get; set; } = null!;

    [JsonPropertyName("site")]
    public string Site { get; set; } = string.Empty;

    [JsonPropertyName("key")]
    public string Key { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("size")]
    public int Size { get; set; }

    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

}

public enum VideoType
{
    [JsonPropertyName("Clip")]
    Clip = 0,
    [JsonPropertyName("Teaser")]
    Teaser,
    [JsonPropertyName("Trailer")]
    Trailer,
    [JsonPropertyName("Featurette")]
    Featurette,
    [JsonPropertyName("Opening Credits")]
    OpeningCredits,
    [JsonPropertyName("Behind the Scenes")]
    BehindTheScenes,
    [JsonPropertyName("Bloopers")]
    Bloopers
}

public class MovieDetails
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("imdbId")]
    public string? ImdbId { get; set; } = null!;

    [JsonPropertyName("adult")]
    public bool Adult { get; set; }

    [JsonPropertyName("backdropPath")]
    public string? BackdropPath { get; set; } = null!;

    [JsonPropertyName("budget")]
    public int Budget { get; set; }

    [JsonPropertyName("genres")]
    public List<Genre> Genres { get; set; } = new();

    [JsonPropertyName("homepage")]
    public string? Homepage { get; set; } = null!;

    [JsonPropertyName("originalLanguage")]
    public string OriginalLanguage { get; set; } = string.Empty;

    [JsonPropertyName("originalTitle")]
    public string OriginalTitle { get; set; } = string.Empty;

    [JsonPropertyName("overview")]
    public string? Overview { get; set; } = null!;

    [JsonPropertyName("popularity")]
    public double Popularity { get; set; }

    [JsonPropertyName("relatedVideos")]
    public List<Video>? RelatedVideos { get; set; } = new();

    [JsonPropertyName("posterPath")]
    public string? PosterPath { get; set; } = null!;

    [JsonPropertyName("productionCompanies")]
    public List<ProductionCompany> ProductionCompanies { get; set; } = new();

    [JsonPropertyName("productionCountries")]
    public List<MovieDetailsProductionCountries> ProductionCountries { get; set; } = new();

    [JsonPropertyName("releaseDate")]
    public string ReleaseDate { get; set; } = string.Empty;

    [JsonPropertyName("releases")]
    public TmdbMovieReleaseResult Releases { get; set; } = new();

    [JsonPropertyName("revenue")]
    public int Revenue { get; set; }

    [JsonPropertyName("runtime")]
    public int? Runtime { get; set; } = null!;

    [JsonPropertyName("spokenLanguages")]
    public List<MovieDetailsSpokenLanguages> SpokenLanguages { get; set; } = new();

    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    [JsonPropertyName("tagline")]
    public string? Tagline { get; set; } = null!;

    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("video")]
    public bool Video { get; set; }

    [JsonPropertyName("voteAverage")]
    public double VoteAverage { get; set; }

    [JsonPropertyName("voteCount")]
    public int VoteCount { get; set; }

    [JsonPropertyName("credits")]
    public MovieDetailsCredits Credits { get; set; } = new();

    [JsonPropertyName("collection")]
    public MovieDetailsCollection? Collection { get; set; } = null!;

    [JsonPropertyName("mediaInfo")]
    public Media? MediaInfo { get; set; } = null!;

    [JsonPropertyName("externalIds")]
    public ExternalIds ExternalIds { get; set; } = new();

    [JsonPropertyName("mediaUrl")]
    public string? MediaUrl { get; set; } = null!;

    [JsonPropertyName("watchProviders")]
    public List<WatchProviders>? WatchProviders { get; set; } = new();

    [JsonPropertyName("keywords")]
    public List<Keyword> Keywords { get; set; } = new();

    [JsonPropertyName("onUserWatchlist")]
    public bool? OnUserWatchlist { get; set; } = null!;

}



public class MovieDetailsProductionCountries
{
    [JsonPropertyName("iso_3166_1")]
    public string Iso31661 { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

}

public class MovieDetailsSpokenLanguages
{
    [JsonPropertyName("iso_639_1")]
    public string Iso6391 { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

}

public class MovieDetailsCredits
{
    [JsonPropertyName("cast")]
    public List<Cast> Cast { get; set; } = new();

    [JsonPropertyName("crew")]
    public List<Crew> Crew { get; set; } = new();

}

public class MovieDetailsCollection
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("posterPath")]
    public string? PosterPath { get; set; } = null!;

    [JsonPropertyName("backdropPath")]
    public string? BackdropPath { get; set; } = null!;

}