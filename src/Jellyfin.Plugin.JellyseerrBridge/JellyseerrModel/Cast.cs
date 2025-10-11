using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.JellyseerrBridge.Models;

/// <summary>
/// Generated from TypeScript interface: Cast
/// </summary>
public class Cast
{

    [JsonPropertyName("id")]
    public int Id { get; set; } = 0;

    [JsonPropertyName("castId")]
    public int CastId { get; set; } = 0;

    [JsonPropertyName("character")]
    public string? Character { get; set; } = null;

    [JsonPropertyName("creditId")]
    public string? CreditId { get; set; } = null;

    [JsonPropertyName("gender")]
    public int? Gender { get; set; } = null;

    [JsonPropertyName("name")]
    public string? Name { get; set; } = null;

    [JsonPropertyName("order")]
    public int Order { get; set; } = 0;

    [JsonPropertyName("profilePath")]
    public string? ProfilePath { get; set; } = null;

}
