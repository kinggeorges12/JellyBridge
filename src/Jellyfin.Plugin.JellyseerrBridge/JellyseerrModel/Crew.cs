using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.JellyseerrBridge.Models;

/// <summary>
/// Generated from TypeScript interface: Crew
/// </summary>
public class Crew
{

    [JsonPropertyName("id")]
    public int Id { get; set; } = 0;

    [JsonPropertyName("creditId")]
    public string? CreditId { get; set; } = null;

    [JsonPropertyName("department")]
    public string? Department { get; set; } = null;

    [JsonPropertyName("gender")]
    public int? Gender { get; set; } = null;

    [JsonPropertyName("job")]
    public string? Job { get; set; } = null;

    [JsonPropertyName("name")]
    public string? Name { get; set; } = null;

    [JsonPropertyName("profilePath")]
    public string? ProfilePath { get; set; } = null;

}
