using System.Text.Json.Serialization;
using System.Collections.Generic;

namespace Jellyfin.Plugin.JellyseerrBridge.Models;

/// <summary>
/// Generated from TypeScript interface: Collection
/// </summary>
public class Collection
{

    [JsonPropertyName("id")]
    public int Id { get; set; } = 0;

    [JsonPropertyName("name")]
    public string? Name { get; set; } = null;

    [JsonPropertyName("overview")]
    public string? Overview { get; set; } = null;

    [JsonPropertyName("posterPath")]
    public string? PosterPath { get; set; } = null;

    [JsonPropertyName("backdropPath")]
    public string? BackdropPath { get; set; } = null;

    [JsonPropertyName("parts")]
    public List<MovieResult> Parts { get; set; } = new();

}
