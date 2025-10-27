using System;
using System.Text.Json.Serialization;
using System.Collections.Generic;
using Jellyfin.Plugin.JellyBridge.JellyseerrModel;

using Jellyfin.Plugin.JellyBridge.JellyseerrModel.Server;
namespace Jellyfin.Plugin.JellyBridge.JellyseerrModel.Server;

public class ProductionCompany
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("logoPath")]
    public string? LogoPath { get; set; } = null!;

    [JsonPropertyName("originCountry")]
    public string OriginCountry { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string? Description { get; set; } = null!;

    [JsonPropertyName("headquarters")]
    public string? Headquarters { get; set; } = null!;

    [JsonPropertyName("homepage")]
    public string? Homepage { get; set; } = null!;

}

public class TvNetwork
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("logoPath")]
    public string? LogoPath { get; set; } = null!;

    [JsonPropertyName("originCountry")]
    public string? OriginCountry { get; set; } = null!;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("headquarters")]
    public string? Headquarters { get; set; } = null!;

    [JsonPropertyName("homepage")]
    public string? Homepage { get; set; } = null!;

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
    public string? ProfilePath { get; set; } = null!;

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
    public string? ProfilePath { get; set; } = null!;

}

public class ExternalIds
{
    [JsonPropertyName("imdbId")]
    public string? ImdbId { get; set; } = null!;

    [JsonPropertyName("freebaseMid")]
    public string? FreebaseMid { get; set; } = null!;

    [JsonPropertyName("freebaseId")]
    public string? FreebaseId { get; set; } = null!;

    [JsonPropertyName("tvdbId")]
    public int? TvdbId { get; set; } = null!;

    [JsonPropertyName("tvrageId")]
    public string? TvrageId { get; set; } = null!;

    [JsonPropertyName("facebookId")]
    public string? FacebookId { get; set; } = null!;

    [JsonPropertyName("instagramId")]
    public string? InstagramId { get; set; } = null!;

    [JsonPropertyName("twitterId")]
    public string? TwitterId { get; set; } = null!;

}

public class WatchProviders
{
    [JsonPropertyName("iso_3166_1")]
    public string Iso31661 { get; set; } = string.Empty;

    [JsonPropertyName("link")]
    public string? Link { get; set; } = null!;

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
    public string? LogoPath { get; set; } = null!;

    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

}


