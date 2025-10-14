using System;
using System.Text.Json.Serialization;
using System.Collections.Generic;
using Jellyfin.Plugin.JellyseerrBridge.JellyseerrModel.Server;

namespace Jellyfin.Plugin.JellyseerrBridge.JellyseerrModel;



public class Session
{
    [JsonPropertyName("expiredAt")]
    public string ExpiredAt { get; set; } = string.Empty;

    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("json")]
    public string Json { get; set; } = string.Empty;

}


