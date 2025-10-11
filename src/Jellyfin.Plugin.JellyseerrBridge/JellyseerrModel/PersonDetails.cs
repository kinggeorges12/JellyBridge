using System.Text.Json.Serialization;
using System.Collections.Generic;

namespace Jellyfin.Plugin.JellyseerrBridge.Models;

/// <summary>
/// Generated from TypeScript interface: PersonDetails
/// </summary>
public class PersonDetails
{

    [JsonPropertyName("id")]
    public int Id { get; set; } = 0;

    [JsonPropertyName("name")]
    public string? Name { get; set; } = null;

    [JsonPropertyName("birthday")]
    public string? Birthday { get; set; } = null;

    [JsonPropertyName("deathday")]
    public string? Deathday { get; set; } = null;

    [JsonPropertyName("knownForDepartment")]
    public string? KnownForDepartment { get; set; } = null;

    [JsonPropertyName("alsoKnownAs")]
    public List<string>? AlsoKnownAs { get; set; } = null;

    [JsonPropertyName("gender")]
    public int Gender { get; set; } = 0;

    [JsonPropertyName("biography")]
    public string? Biography { get; set; } = null;

    [JsonPropertyName("popularity")]
    public double Popularity { get; set; } = 0.0;

    [JsonPropertyName("placeOfBirth")]
    public string? PlaceOfBirth { get; set; } = null;

    [JsonPropertyName("profilePath")]
    public string? ProfilePath { get; set; } = null;

    [JsonPropertyName("adult")]
    public bool Adult { get; set; } = false;

    [JsonPropertyName("imdbId")]
    public string? ImdbId { get; set; } = null;

    [JsonPropertyName("homepage")]
    public string? Homepage { get; set; } = null;

}
