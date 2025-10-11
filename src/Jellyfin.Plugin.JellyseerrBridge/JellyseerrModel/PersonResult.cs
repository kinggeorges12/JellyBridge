using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.JellyseerrBridge.Models;

/// <summary>
/// Generated from TypeScript interface: PersonResult
/// </summary>
public class PersonResult
{

    [JsonPropertyName("id")]
    public int Id { get; set; } = 0;

    [JsonPropertyName("name")]
    public string? Name { get; set; } = null;

    [JsonPropertyName("popularity")]
    public double Popularity { get; set; } = 0.0;

    [JsonPropertyName("profilePath")]
    public string? ProfilePath { get; set; } = null;

    [JsonPropertyName("adult")]
    public bool Adult { get; set; } = false;

    [JsonPropertyName("mediaType")]
    public string? MediaType { get; set; } = null;

    [JsonPropertyName("knownFor")]
    public string? KnownFor { get; set; } = null;

}
