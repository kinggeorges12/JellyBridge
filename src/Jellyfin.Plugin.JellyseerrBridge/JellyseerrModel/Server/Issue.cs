using System;
using System.Text.Json.Serialization;
using System.Collections.Generic;
using Jellyfin.Plugin.JellyseerrBridge.JellyseerrModel;

namespace Jellyfin.Plugin.JellyseerrBridge.JellyseerrModel.Server;

public class Issue
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("issueType")]
    public IssueType IssueType { get; set; } = new();

    [JsonPropertyName("status")]
    public IssueStatus Status { get; set; } = new();

    [JsonPropertyName("problemSeason")]
    public int ProblemSeason { get; set; }

    [JsonPropertyName("problemEpisode")]
    public int ProblemEpisode { get; set; }

    [JsonPropertyName("media")]
    public Media Media { get; set; } = new();

    [JsonPropertyName("createdBy")]
    public User CreatedBy { get; set; } = new();

    [JsonPropertyName("modifiedBy")]
    public User ModifiedBy { get; set; } = new();

    [JsonPropertyName("comments")]
    public List<IssueComment> Comments { get; set; } = new();

    [JsonPropertyName("createdAt")]
    public DateTimeOffset CreatedAt { get; set; }

    [JsonPropertyName("updatedAt")]
    public DateTimeOffset UpdatedAt { get; set; }

}


