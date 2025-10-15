using System;
using System.Text.Json.Serialization;
using System.Collections.Generic;

namespace Jellyfin.Plugin.JellyseerrBridge.JellyseerrModel;

public class SonarrSeason
{
    [JsonPropertyName("seasonNumber")]
    public int SeasonNumber { get; set; }

    [JsonPropertyName("monitored")]
    public bool Monitored { get; set; }

    [JsonPropertyName("statistics")]
    public SonarrSeasonStatistics? Statistics { get; set; } = null!;

}

public class EpisodeResult
{
    [JsonPropertyName("seriesId")]
    public int SeriesId { get; set; }

    [JsonPropertyName("episodeFileId")]
    public int EpisodeFileId { get; set; }

    [JsonPropertyName("seasonNumber")]
    public int SeasonNumber { get; set; }

    [JsonPropertyName("episodeNumber")]
    public int EpisodeNumber { get; set; }

    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("airDate")]
    public string AirDate { get; set; } = string.Empty;

    [JsonPropertyName("airDateUtc")]
    public string AirDateUtc { get; set; } = string.Empty;

    [JsonPropertyName("overview")]
    public string Overview { get; set; } = string.Empty;

    [JsonPropertyName("hasFile")]
    public bool HasFile { get; set; }

    [JsonPropertyName("monitored")]
    public bool Monitored { get; set; }

    [JsonPropertyName("absoluteEpisodeNumber")]
    public int AbsoluteEpisodeNumber { get; set; }

    [JsonPropertyName("unverifiedSceneNumbering")]
    public bool UnverifiedSceneNumbering { get; set; }

    [JsonPropertyName("id")]
    public int Id { get; set; }

}

public class SonarrSeries
{
    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("sortTitle")]
    public string SortTitle { get; set; } = string.Empty;

    [JsonPropertyName("seasonCount")]
    public int SeasonCount { get; set; }

    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    [JsonPropertyName("overview")]
    public string Overview { get; set; } = string.Empty;

    [JsonPropertyName("network")]
    public string Network { get; set; } = string.Empty;

    [JsonPropertyName("airTime")]
    public string AirTime { get; set; } = string.Empty;

    [JsonPropertyName("images")]
    public List<SonarrSeriesImages> Images { get; set; } = new();

    [JsonPropertyName("remotePoster")]
    public string RemotePoster { get; set; } = string.Empty;

    [JsonPropertyName("seasons")]
    public List<SonarrSeason> Seasons { get; set; } = new();

    [JsonPropertyName("year")]
    public int Year { get; set; }

    [JsonPropertyName("path")]
    public string Path { get; set; } = string.Empty;

    [JsonPropertyName("profileId")]
    public int ProfileId { get; set; }

    [JsonPropertyName("languageProfileId")]
    public int LanguageProfileId { get; set; }

    [JsonPropertyName("seasonFolder")]
    public bool SeasonFolder { get; set; }

    [JsonPropertyName("monitored")]
    public bool Monitored { get; set; }

    [JsonPropertyName("useSceneNumbering")]
    public bool UseSceneNumbering { get; set; }

    [JsonPropertyName("runtime")]
    public int Runtime { get; set; }

    [JsonPropertyName("tvdbId")]
    public int TvdbId { get; set; }

    [JsonPropertyName("tvRageId")]
    public int TvRageId { get; set; }

    [JsonPropertyName("tvMazeId")]
    public int TvMazeId { get; set; }

    [JsonPropertyName("firstAired")]
    public string FirstAired { get; set; } = string.Empty;

    [JsonPropertyName("lastInfoSync")]
    public string? LastInfoSync { get; set; } = null!;

    [JsonPropertyName("seriesType")]
    public string SeriesType { get; set; } = string.Empty;

    [JsonPropertyName("cleanTitle")]
    public string CleanTitle { get; set; } = string.Empty;

    [JsonPropertyName("imdbId")]
    public string ImdbId { get; set; } = string.Empty;

    [JsonPropertyName("titleSlug")]
    public string TitleSlug { get; set; } = string.Empty;

    [JsonPropertyName("certification")]
    public string Certification { get; set; } = string.Empty;

    [JsonPropertyName("genres")]
    public List<string> Genres { get; set; } = new();

    [JsonPropertyName("tags")]
    public List<int> Tags { get; set; } = new();

    [JsonPropertyName("added")]
    public string Added { get; set; } = string.Empty;

    [JsonPropertyName("ratings")]
    public SonarrSeriesRatings Ratings { get; set; } = new();

    [JsonPropertyName("qualityProfileId")]
    public int QualityProfileId { get; set; }

    [JsonPropertyName("id")]
    public int? Id { get; set; } = null!;

    [JsonPropertyName("rootFolderPath")]
    public string? RootFolderPath { get; set; } = null!;

    [JsonPropertyName("addOptions")]
    public SonarrSeriesAddOptions? AddOptions { get; set; } = null!;

    [JsonPropertyName("statistics")]
    public SonarrSeriesStatistics Statistics { get; set; } = new();

}

public class AddSeriesOptions
{
    [JsonPropertyName("tvdbid")]
    public int Tvdbid { get; set; }

    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("profileId")]
    public int ProfileId { get; set; }

    [JsonPropertyName("languageProfileId")]
    public int? LanguageProfileId { get; set; } = null!;

    [JsonPropertyName("seasons")]
    public List<int> Seasons { get; set; } = new();

    [JsonPropertyName("seasonFolder")]
    public bool SeasonFolder { get; set; }

    [JsonPropertyName("rootFolderPath")]
    public string RootFolderPath { get; set; } = string.Empty;

    [JsonPropertyName("tags")]
    public List<int>? Tags { get; set; } = new();

    [JsonPropertyName("seriesType")]
    public SeriesType SeriesType { get; set; } = new();

    [JsonPropertyName("monitored")]
    public bool? Monitored { get; set; } = null!;

    [JsonPropertyName("searchNow")]
    public bool? SearchNow { get; set; } = null!;

}

public class LanguageProfile
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

}

public class SonarrAPI : object
{
}



public class SonarrSeasonStatistics
{
    [JsonPropertyName("previousAiring")]
    public string? PreviousAiring { get; set; } = null!;

    [JsonPropertyName("episodeFileCount")]
    public int EpisodeFileCount { get; set; }

    [JsonPropertyName("episodeCount")]
    public int EpisodeCount { get; set; }

    [JsonPropertyName("totalEpisodeCount")]
    public int TotalEpisodeCount { get; set; }

    [JsonPropertyName("sizeOnDisk")]
    public int SizeOnDisk { get; set; }

    [JsonPropertyName("percentOfEpisodes")]
    public int PercentOfEpisodes { get; set; }

}

public class SonarrSeriesImages
{
    [JsonPropertyName("coverType")]
    public string CoverType { get; set; } = string.Empty;

    [JsonPropertyName("url")]
    public string Url { get; set; } = string.Empty;

}

public class SonarrSeriesRatings
{
    [JsonPropertyName("votes")]
    public int Votes { get; set; }

    [JsonPropertyName("value")]
    public int Value { get; set; }

}

public class SonarrSeriesAddOptions
{
    [JsonPropertyName("ignoreEpisodesWithFiles")]
    public bool? IgnoreEpisodesWithFiles { get; set; } = null!;

    [JsonPropertyName("ignoreEpisodesWithoutFiles")]
    public bool? IgnoreEpisodesWithoutFiles { get; set; } = null!;

    [JsonPropertyName("searchForMissingEpisodes")]
    public bool? SearchForMissingEpisodes { get; set; } = null!;

}

public class SonarrSeriesStatistics
{
    [JsonPropertyName("seasonCount")]
    public int SeasonCount { get; set; }

    [JsonPropertyName("episodeFileCount")]
    public int EpisodeFileCount { get; set; }

    [JsonPropertyName("episodeCount")]
    public int EpisodeCount { get; set; }

    [JsonPropertyName("totalEpisodeCount")]
    public int TotalEpisodeCount { get; set; }

    [JsonPropertyName("sizeOnDisk")]
    public int SizeOnDisk { get; set; }

    [JsonPropertyName("releaseGroups")]
    public List<string> ReleaseGroups { get; set; } = new();

    [JsonPropertyName("percentOfEpisodes")]
    public int PercentOfEpisodes { get; set; }

}