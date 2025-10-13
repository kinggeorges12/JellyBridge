using System;
using System.Text.Json.Serialization;
using System.Collections.Generic;
using Jellyfin.Plugin.JellyseerrBridge.JellyseerrModel;

namespace Jellyfin.Plugin.JellyseerrBridge.JellyseerrModel.Server;

public class OverrideRule
{
    [JsonPropertyName("id")]
    public int Id { get; set; }
    [JsonPropertyName("radarrServiceId")]
    public int RadarrServiceId { get; set; }
    [JsonPropertyName("sonarrServiceId")]
    public int SonarrServiceId { get; set; }
    [JsonPropertyName("users")]
    public string Users { get; set; } = string.Empty;
    [JsonPropertyName("genre")]
    public string Genre { get; set; } = string.Empty;
    [JsonPropertyName("language")]
    public string Language { get; set; } = string.Empty;
    [JsonPropertyName("keywords")]
    public string Keywords { get; set; } = string.Empty;
    [JsonPropertyName("profileId")]
    public int ProfileId { get; set; }
    [JsonPropertyName("rootFolder")]
    public string RootFolder { get; set; } = string.Empty;
    [JsonPropertyName("tags")]
    public string Tags { get; set; } = string.Empty;
    [JsonPropertyName("createdAt")]
    public DateTimeOffset CreatedAt { get; set; }
    [JsonPropertyName("updatedAt")]
    public DateTimeOffset UpdatedAt { get; set; }
}


