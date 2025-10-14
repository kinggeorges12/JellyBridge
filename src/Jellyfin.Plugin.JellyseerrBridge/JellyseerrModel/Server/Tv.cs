using System;
using System.Text.Json.Serialization;
using System.Collections.Generic;
using Jellyfin.Plugin.JellyseerrBridge.JellyseerrModel;

namespace Jellyfin.Plugin.JellyseerrBridge.JellyseerrModel.Server;

public class Episode
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("airDate")]
    public object AirDate { get; set; } = null!;

    [JsonPropertyName("episodeNumber")]
    public int EpisodeNumber { get; set; }

    [JsonPropertyName("overview")]
    public string Overview { get; set; } = string.Empty;

    [JsonPropertyName("productionCode")]
    public string ProductionCode { get; set; } = string.Empty;

    [JsonPropertyName("seasonNumber")]
    public int SeasonNumber { get; set; }

    [JsonPropertyName("showId")]
    public int ShowId { get; set; }

    [JsonPropertyName("stillPath")]
    public string? StillPath { get; set; } = null!;

    [JsonPropertyName("voteAverage")]
    public double VoteAverage { get; set; }

    [JsonPropertyName("voteCount")]
    public int VoteCount { get; set; }

}

public class Season
{
    [JsonPropertyName("airDate")]
    public string AirDate { get; set; } = string.Empty;

    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("episodeCount")]
    public int EpisodeCount { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("overview")]
    public string Overview { get; set; } = string.Empty;

    [JsonPropertyName("posterPath")]
    public string? PosterPath { get; set; } = null!;

    [JsonPropertyName("seasonNumber")]
    public int SeasonNumber { get; set; }

}

public class SeasonWithEpisodes : Season
{
    [JsonPropertyName("episodes")]
    public List<Episode> Episodes { get; set; } = new();

    [JsonPropertyName("externalIds")]
    public ExternalIds ExternalIds { get; set; } = new();


    // TypeScript: Omit<Season, 'episodeCount'>
}

public class SpokenLanguage
{
    [JsonPropertyName("englishName")]
    public string EnglishName { get; set; } = string.Empty;

    [JsonPropertyName("iso_639_1")]
    public string Iso6391 { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

}

public class TvDetails
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("backdropPath")]
    public string? BackdropPath { get; set; } = null!;

    [JsonPropertyName("posterPath")]
    public string? PosterPath { get; set; } = null!;

    [JsonPropertyName("contentRatings")]
    public TmdbTvRatingResult ContentRatings { get; set; } = new();

    [JsonPropertyName("createdBy")]
    public List<TvDetailsCreatedBy> CreatedBy { get; set; } = new();

    [JsonPropertyName("episodeRunTime")]
    public List<int> EpisodeRunTime { get; set; } = new();

    [JsonPropertyName("firstAirDate")]
    public string? FirstAirDate { get; set; } = null!;

    [JsonPropertyName("genres")]
    public List<Genre> Genres { get; set; } = new();

    [JsonPropertyName("homepage")]
    public string Homepage { get; set; } = string.Empty;

    [JsonPropertyName("inProduction")]
    public bool InProduction { get; set; }

    [JsonPropertyName("relatedVideos")]
    public List<Video>? RelatedVideos { get; set; } = new();

    [JsonPropertyName("languages")]
    public List<string> Languages { get; set; } = new();

    [JsonPropertyName("lastAirDate")]
    public string LastAirDate { get; set; } = string.Empty;

    [JsonPropertyName("lastEpisodeToAir")]
    public Episode? LastEpisodeToAir { get; set; } = null!;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("nextEpisodeToAir")]
    public Episode? NextEpisodeToAir { get; set; } = null!;

    [JsonPropertyName("networks")]
    public List<TvNetwork> Networks { get; set; } = new();

    [JsonPropertyName("numberOfEpisodes")]
    public int NumberOfEpisodes { get; set; }

    [JsonPropertyName("numberOfSeasons")]
    public int NumberOfSeasons { get; set; }

    [JsonPropertyName("originCountry")]
    public List<string> OriginCountry { get; set; } = new();

    [JsonPropertyName("originalLanguage")]
    public string OriginalLanguage { get; set; } = string.Empty;

    [JsonPropertyName("originalName")]
    public string OriginalName { get; set; } = string.Empty;

    [JsonPropertyName("overview")]
    public string Overview { get; set; } = string.Empty;

    [JsonPropertyName("popularity")]
    public double Popularity { get; set; }

    [JsonPropertyName("productionCompanies")]
    public List<ProductionCompany> ProductionCompanies { get; set; } = new();

    [JsonPropertyName("productionCountries")]
    public List<TvDetailsProductionCountries> ProductionCountries { get; set; } = new();

    [JsonPropertyName("spokenLanguages")]
    public List<SpokenLanguage> SpokenLanguages { get; set; } = new();

    [JsonPropertyName("seasons")]
    public List<Season> Seasons { get; set; } = new();

    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    [JsonPropertyName("tagline")]
    public string? Tagline { get; set; } = null!;

    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    [JsonPropertyName("voteAverage")]
    public double VoteAverage { get; set; }

    [JsonPropertyName("voteCount")]
    public int VoteCount { get; set; }

    [JsonPropertyName("credits")]
    public TvDetailsCredits Credits { get; set; } = new();

    [JsonPropertyName("externalIds")]
    public ExternalIds ExternalIds { get; set; } = new();

    [JsonPropertyName("keywords")]
    public List<Keyword> Keywords { get; set; } = new();

    [JsonPropertyName("mediaInfo")]
    public Media? MediaInfo { get; set; } = null!;

    [JsonPropertyName("watchProviders")]
    public List<WatchProviders>? WatchProviders { get; set; } = new();

    [JsonPropertyName("onUserWatchlist")]
    public bool? OnUserWatchlist { get; set; } = null!;

}



public class TvDetailsCreatedBy
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("gender")]
    public int Gender { get; set; }

    [JsonPropertyName("profilePath")]
    public string? ProfilePath { get; set; } = null!;

}

public class TvDetailsProductionCountries
{
    [JsonPropertyName("iso_3166_1")]
    public string Iso31661 { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

}

public class TvDetailsCredits
{
    [JsonPropertyName("cast")]
    public List<Cast> Cast { get; set; } = new();

    [JsonPropertyName("crew")]
    public List<Crew> Crew { get; set; } = new();

}