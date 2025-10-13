using System;
using System.Text.Json.Serialization;
using System.Collections.Generic;
using Jellyfin.Plugin.JellyseerrBridge.JellyseerrModel;

namespace Jellyfin.Plugin.JellyseerrBridge.JellyseerrModel.Server;

public class SeasonRequest
{
    [JsonPropertyName("id")]
    public int Id { get; set; }
    [JsonPropertyName("seasonNumber")]
    public int SeasonNumber { get; set; }
    [JsonPropertyName("status")]
    public MediaRequestStatus Status { get; set; } = new();
    [JsonPropertyName("request")]
    public MediaRequest Request { get; set; } = new();
    [JsonPropertyName("createdAt")]
    public DateTimeOffset CreatedAt { get; set; }
    [JsonPropertyName("updatedAt")]
    public DateTimeOffset UpdatedAt { get; set; }
}


