using System;
using System.Text.Json.Serialization;
using System.Collections.Generic;

namespace Jellyfin.Plugin.JellyBridge.JellyseerrModel;





public enum IssueType
{
    VIDEO = 1,
    AUDIO = 2,
    SUBTITLES = 3,
    OTHER = 4
}

public enum IssueStatus
{
    OPEN = 1,
    RESOLVED = 2
}

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
    public User? ModifiedBy { get; set; } = null!;

    [JsonPropertyName("comments")]
    public List<IssueComment> Comments { get; set; } = new();

    [JsonPropertyName("createdAt")]
    public DateTimeOffset CreatedAt { get; set; }

    [JsonPropertyName("updatedAt")]
    public DateTimeOffset UpdatedAt { get; set; }

}


