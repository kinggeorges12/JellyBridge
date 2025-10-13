using System;
using System.Text.Json.Serialization;
using System.Collections.Generic;
using Jellyfin.Plugin.JellyseerrBridge.JellyseerrModel;

using Jellyfin.Plugin.JellyseerrBridge.JellyseerrModel.Server;
namespace Jellyfin.Plugin.JellyseerrBridge.JellyseerrModel.Server;

public class ProductionCompany
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("logoPath")]
    public string? LogoPath { get; set; } = string.Empty;

    [JsonPropertyName("originCountry")]
    public string OriginCountry { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string? Description { get; set; } = string.Empty;

    [JsonPropertyName("headquarters")]
    public string? Headquarters { get; set; } = string.Empty;

    [JsonPropertyName("homepage")]
    public string? Homepage { get; set; } = string.Empty;

}

public class TvNetwork
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("logoPath")]
    public string? LogoPath { get; set; } = string.Empty;

    [JsonPropertyName("originCountry")]
    public string? OriginCountry { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("headquarters")]
    public string? Headquarters { get; set; } = string.Empty;

    [JsonPropertyName("homepage")]
    public string? Homepage { get; set; } = string.Empty;

}

public class Keyword
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

}

public class Genre
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

}

public class Cast
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("castId")]
    public int CastId { get; set; }

    [JsonPropertyName("character")]
    public string Character { get; set; } = string.Empty;

    [JsonPropertyName("creditId")]
    public string CreditId { get; set; } = string.Empty;

    [JsonPropertyName("gender")]
    public int? Gender { get; set; } = null!;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("order")]
    public int Order { get; set; }

    [JsonPropertyName("profilePath")]
    public string? ProfilePath { get; set; } = string.Empty;

}

public class Crew
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("creditId")]
    public string CreditId { get; set; } = string.Empty;

    [JsonPropertyName("department")]
    public string Department { get; set; } = string.Empty;

    [JsonPropertyName("gender")]
    public int? Gender { get; set; } = null!;

    [JsonPropertyName("job")]
    public string Job { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("profilePath")]
    public string? ProfilePath { get; set; } = string.Empty;

}

public class ExternalIds
{
    [JsonPropertyName("imdbId")]
    public string? ImdbId { get; set; } = string.Empty;

    [JsonPropertyName("freebaseMid")]
    public string? FreebaseMid { get; set; } = string.Empty;

    [JsonPropertyName("freebaseId")]
    public string? FreebaseId { get; set; } = string.Empty;

    [JsonPropertyName("tvdbId")]
    public int? TvdbId { get; set; } = null!;

    [JsonPropertyName("tvrageId")]
    public string? TvrageId { get; set; } = string.Empty;

    [JsonPropertyName("facebookId")]
    public string? FacebookId { get; set; } = string.Empty;

    [JsonPropertyName("instagramId")]
    public string? InstagramId { get; set; } = string.Empty;

    [JsonPropertyName("twitterId")]
    public string? TwitterId { get; set; } = string.Empty;

}

public class WatchProviders
{
    [JsonPropertyName("iso_3166_1")]
    public string Iso31661 { get; set; } = string.Empty;

    [JsonPropertyName("link")]
    public string? Link { get; set; } = string.Empty;

    [JsonPropertyName("buy")]
    public List<WatchProviderDetails>? Buy { get; set; } = new();

    [JsonPropertyName("flatrate")]
    public List<WatchProviderDetails>? Flatrate { get; set; } = new();

}

public class WatchProviderDetails
{
    [JsonPropertyName("displayPriority")]
    public int? DisplayPriority { get; set; } = null!;

    [JsonPropertyName("logoPath")]
    public string? LogoPath { get; set; } = string.Empty;

    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

}


