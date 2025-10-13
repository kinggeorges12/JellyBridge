using System;
using System.Text.Json.Serialization;
using System.Collections.Generic;
using Jellyfin.Plugin.JellyseerrBridge.JellyseerrModel;

namespace Jellyfin.Plugin.JellyseerrBridge.JellyseerrModel.Server;

public class PersonDetails
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("birthday")]
    public string Birthday { get; set; } = string.Empty;

    [JsonPropertyName("deathday")]
    public string Deathday { get; set; } = string.Empty;

    [JsonPropertyName("knownForDepartment")]
    public string KnownForDepartment { get; set; } = string.Empty;

    [JsonPropertyName("alsoKnownAs")]
    public List<string>? AlsoKnownAs { get; set; } = new();

    [JsonPropertyName("gender")]
    public int Gender { get; set; }

    [JsonPropertyName("biography")]
    public string Biography { get; set; } = string.Empty;

    [JsonPropertyName("popularity")]
    public double Popularity { get; set; }

    [JsonPropertyName("placeOfBirth")]
    public string? PlaceOfBirth { get; set; } = string.Empty;

    [JsonPropertyName("profilePath")]
    public string? ProfilePath { get; set; } = string.Empty;

    [JsonPropertyName("adult")]
    public bool Adult { get; set; }

    [JsonPropertyName("imdbId")]
    public string? ImdbId { get; set; } = string.Empty;

    [JsonPropertyName("homepage")]
    public string? Homepage { get; set; } = string.Empty;

}

public class PersonCredit
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("originalLanguage")]
    public string OriginalLanguage { get; set; } = string.Empty;

    [JsonPropertyName("episodeCount")]
    public int EpisodeCount { get; set; }

    [JsonPropertyName("overview")]
    public string Overview { get; set; } = string.Empty;

    [JsonPropertyName("originCountry")]
    public List<string> OriginCountry { get; set; } = new();

    [JsonPropertyName("originalName")]
    public string OriginalName { get; set; } = string.Empty;

    [JsonPropertyName("voteCount")]
    public int VoteCount { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("mediaType")]
    public string? MediaType { get; set; } = string.Empty;

    [JsonPropertyName("popularity")]
    public double Popularity { get; set; }

    [JsonPropertyName("creditId")]
    public string CreditId { get; set; } = string.Empty;

    [JsonPropertyName("backdropPath")]
    public string? BackdropPath { get; set; } = string.Empty;

    [JsonPropertyName("firstAirDate")]
    public string FirstAirDate { get; set; } = string.Empty;

    [JsonPropertyName("voteAverage")]
    public double VoteAverage { get; set; }

    [JsonPropertyName("genreIds")]
    public List<int>? GenreIds { get; set; } = new();

    [JsonPropertyName("posterPath")]
    public string? PosterPath { get; set; } = string.Empty;

    [JsonPropertyName("originalTitle")]
    public string OriginalTitle { get; set; } = string.Empty;

    [JsonPropertyName("video")]
    public bool? Video { get; set; } = null!;

    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("adult")]
    public bool Adult { get; set; }

    [JsonPropertyName("releaseDate")]
    public string ReleaseDate { get; set; } = string.Empty;

    [JsonPropertyName("mediaInfo")]
    public Media? MediaInfo { get; set; } = null!;

}

public class PersonCreditCast : PersonCredit
{
    [JsonPropertyName("character")]
    public string Character { get; set; } = string.Empty;

}

public class PersonCreditCrew : PersonCredit
{
    [JsonPropertyName("department")]
    public string Department { get; set; } = string.Empty;

    [JsonPropertyName("job")]
    public string Job { get; set; } = string.Empty;

}

public class CombinedCredit
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("cast")]
    public List<PersonCreditCast> Cast { get; set; } = new();

    [JsonPropertyName("crew")]
    public List<PersonCreditCrew> Crew { get; set; } = new();

}

