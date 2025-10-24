using System;
using System.Text.Json.Serialization;
using System.Collections.Generic;
using Jellyfin.Plugin.JellyseerrBridge.JellyseerrModel;

namespace Jellyfin.Plugin.JellyseerrBridge.JellyseerrModel.Server;

public class SearchResult
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("mediaType")]
    public SearchMediaType MediaType { get; set; } = new();

    [JsonPropertyName("popularity")]
    public double Popularity { get; set; }

    [JsonPropertyName("posterPath")]
    public string? PosterPath { get; set; } = null!;

    [JsonPropertyName("backdropPath")]
    public string? BackdropPath { get; set; } = null!;

    [JsonPropertyName("voteCount")]
    public int VoteCount { get; set; }

    [JsonPropertyName("voteAverage")]
    public double VoteAverage { get; set; }

    [JsonPropertyName("genreIds")]
    public List<int> GenreIds { get; set; } = new();

    [JsonPropertyName("overview")]
    public string Overview { get; set; } = string.Empty;

    [JsonPropertyName("originalLanguage")]
    public string OriginalLanguage { get; set; } = string.Empty;

    [JsonPropertyName("mediaInfo")]
    public Media? MediaInfo { get; set; } = null!;

}

public class MovieResult : SearchResult
{
    [JsonPropertyName("mediaType")]
    public new string MediaType { get; set; } = string.Empty;

    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("originalTitle")]
    public string OriginalTitle { get; set; } = string.Empty;

    [JsonPropertyName("releaseDate")]
    public string ReleaseDate { get; set; } = string.Empty;

    [JsonPropertyName("adult")]
    public bool Adult { get; set; }

    [JsonPropertyName("video")]
    public bool Video { get; set; }

    [JsonPropertyName("mediaInfo")]
    public new Media? MediaInfo { get; set; } = null!;

}

public class TvResult : SearchResult
{
    [JsonPropertyName("mediaType")]
    public new string MediaType { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("originalName")]
    public string OriginalName { get; set; } = string.Empty;

    [JsonPropertyName("originCountry")]
    public List<string> OriginCountry { get; set; } = new();

    [JsonPropertyName("firstAirDate")]
    public string FirstAirDate { get; set; } = string.Empty;

}

public class CollectionResult
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("mediaType")]
    public string MediaType { get; set; } = string.Empty;

    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("originalTitle")]
    public string OriginalTitle { get; set; } = string.Empty;

    [JsonPropertyName("adult")]
    public bool Adult { get; set; }

    [JsonPropertyName("posterPath")]
    public string? PosterPath { get; set; } = null!;

    [JsonPropertyName("backdropPath")]
    public string? BackdropPath { get; set; } = null!;

    [JsonPropertyName("overview")]
    public string Overview { get; set; } = string.Empty;

    [JsonPropertyName("originalLanguage")]
    public string OriginalLanguage { get; set; } = string.Empty;

}

public class PersonResult
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("popularity")]
    public double Popularity { get; set; }

    [JsonPropertyName("profilePath")]
    public string? ProfilePath { get; set; } = null!;

    [JsonPropertyName("adult")]
    public bool Adult { get; set; }

    [JsonPropertyName("mediaType")]
    public string MediaType { get; set; } = string.Empty;

    [JsonPropertyName("knownFor")]
    // Union type array: (MovieResult | TvResult)[]
    public List<object> KnownFor { get; set; } = new();

}

public enum SearchMediaType
{
    [JsonPropertyName("tv")]
    Tv = 0,
    [JsonPropertyName("movie")]
    Movie,
    [JsonPropertyName("person")]
    Person,
    [JsonPropertyName("collection")]
    Collection
}

// TypeScript: Results = MovieResult | TvResult | PersonResult | CollectionResult
public class Results
{
    public SearchResult Value { get; set; } = new();

}


