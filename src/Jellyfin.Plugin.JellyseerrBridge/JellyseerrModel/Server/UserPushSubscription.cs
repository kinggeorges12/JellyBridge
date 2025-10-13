using System;
using System.Text.Json.Serialization;
using System.Collections.Generic;
using Jellyfin.Plugin.JellyseerrBridge.JellyseerrModel;

namespace Jellyfin.Plugin.JellyseerrBridge.JellyseerrModel.Server;

public class UserPushSubscription
{
    [JsonPropertyName("id")]
    public int Id { get; set; }
    [JsonPropertyName("user")]
    public User User { get; set; } = new();
    [JsonPropertyName("endpoint")]
    public string Endpoint { get; set; } = string.Empty;
    [JsonPropertyName("p256dh")]
    public string P256dh { get; set; } = string.Empty;
    [JsonPropertyName("auth")]
    public string Auth { get; set; } = string.Empty;
    [JsonPropertyName("userAgent")]
    public string UserAgent { get; set; } = string.Empty;
    [JsonPropertyName("createdAt")]
    public DateTimeOffset CreatedAt { get; set; }
}


