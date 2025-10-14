using System;
using System.Text.Json.Serialization;
using System.Collections.Generic;
using Jellyfin.Plugin.JellyseerrBridge.JellyseerrModel.Server;

namespace Jellyfin.Plugin.JellyseerrBridge.JellyseerrModel;



public class IssueComment
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("user")]
    public User User { get; set; } = new();

    [JsonPropertyName("issue")]
    public Issue Issue { get; set; } = new();

    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;

    [JsonPropertyName("createdAt")]
    public DateTimeOffset CreatedAt { get; set; }

    [JsonPropertyName("updatedAt")]
    public DateTimeOffset UpdatedAt { get; set; }

}


