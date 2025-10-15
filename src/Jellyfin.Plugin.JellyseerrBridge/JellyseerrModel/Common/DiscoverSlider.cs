using System;
using System.Text.Json.Serialization;
using System.Collections.Generic;

namespace Jellyfin.Plugin.JellyseerrBridge.JellyseerrModel;



public class DiscoverSlider
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("type")]
    public DiscoverSliderType Type { get; set; } = new();

    [JsonPropertyName("order")]
    public int Order { get; set; }

    [JsonPropertyName("isBuiltIn")]
    public bool IsBuiltIn { get; set; }

    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; }

    [JsonPropertyName("title")]
    public string? Title { get; set; } = null!;

    [JsonPropertyName("data")]
    public string? Data { get; set; } = null!;

    [JsonPropertyName("createdAt")]
    public DateTimeOffset CreatedAt { get; set; }

    [JsonPropertyName("updatedAt")]
    public DateTimeOffset UpdatedAt { get; set; }

}


