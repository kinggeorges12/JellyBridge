using System;
using System.Text.Json.Serialization;
using System.Collections.Generic;
using Jellyfin.Plugin.JellyseerrBridge.JellyseerrModel;

namespace Jellyfin.Plugin.JellyseerrBridge.JellyseerrModel.Server;

public class Collection
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("overview")]
    public string? Overview { get; set; } = null!;

    [JsonPropertyName("posterPath")]
    public string? PosterPath { get; set; } = null!;

    [JsonPropertyName("backdropPath")]
    public string? BackdropPath { get; set; } = null!;

    [JsonPropertyName("parts")]
    public List<MovieResult> Parts { get; set; } = new();

}

