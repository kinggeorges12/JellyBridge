using System;
using System.Text.Json.Serialization;
using System.Collections.Generic;
using Jellyfin.Plugin.JellyseerrBridge.JellyseerrModel.Server;

namespace Jellyfin.Plugin.JellyseerrBridge.JellyseerrModel;



public class OverrideRule
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("radarrServiceId")]
    public int? RadarrServiceId { get; set; } = null!;

    [JsonPropertyName("sonarrServiceId")]
    public int? SonarrServiceId { get; set; } = null!;

    [JsonPropertyName("users")]
    public string? Users { get; set; } = null!;

    [JsonPropertyName("genre")]
    public string? Genre { get; set; } = null!;

    [JsonPropertyName("language")]
    public string? Language { get; set; } = null!;

    [JsonPropertyName("keywords")]
    public string? Keywords { get; set; } = null!;

    [JsonPropertyName("profileId")]
    public int? ProfileId { get; set; } = null!;

    [JsonPropertyName("rootFolder")]
    public string? RootFolder { get; set; } = null!;

    [JsonPropertyName("tags")]
    public string? Tags { get; set; } = null!;

    [JsonPropertyName("createdAt")]
    public DateTimeOffset CreatedAt { get; set; }

    [JsonPropertyName("updatedAt")]
    public DateTimeOffset UpdatedAt { get; set; }

}


