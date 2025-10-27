using System;
using System.Text.Json.Serialization;
using System.Collections.Generic;

namespace Jellyfin.Plugin.JellyBridge.JellyseerrModel;

public class EpisodeNumberResult
{
    [JsonPropertyName("seasonNumber")]
    public int SeasonNumber { get; set; }

    [JsonPropertyName("episodeNumber")]
    public int EpisodeNumber { get; set; }

    [JsonPropertyName("absoluteEpisodeNumber")]
    public int AbsoluteEpisodeNumber { get; set; }

    [JsonPropertyName("id")]
    public int Id { get; set; }

}

public class DownloadingItem
{
    [JsonPropertyName("mediaType")]
    public MediaType MediaType { get; set; } = new();

    [JsonPropertyName("externalId")]
    public int ExternalId { get; set; }

    [JsonPropertyName("size")]
    public int Size { get; set; }

    [JsonPropertyName("sizeLeft")]
    public int SizeLeft { get; set; }

    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    [JsonPropertyName("timeLeft")]
    public string TimeLeft { get; set; } = string.Empty;

    [JsonPropertyName("estimatedCompletionTime")]
    public DateTimeOffset EstimatedCompletionTime { get; set; }

    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("downloadId")]
    public string DownloadId { get; set; } = string.Empty;

    [JsonPropertyName("episode")]
    public EpisodeNumberResult? Episode { get; set; } = null!;

}

