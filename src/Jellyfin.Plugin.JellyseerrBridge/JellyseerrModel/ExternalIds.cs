using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.JellyseerrBridge.Models;

/// <summary>
/// Generated from TypeScript interface: ExternalIds
/// </summary>
public class ExternalIds
{

    [JsonPropertyName("imdbId")]
    public string? ImdbId { get; set; } = null;

    [JsonPropertyName("freebaseMid")]
    public string? FreebaseMid { get; set; } = null;

    [JsonPropertyName("freebaseId")]
    public string? FreebaseId { get; set; } = null;

    [JsonPropertyName("tvdbId")]
    public int? TvdbId { get; set; } = null;

    [JsonPropertyName("tvrageId")]
    public string? TvrageId { get; set; } = null;

    [JsonPropertyName("facebookId")]
    public string? FacebookId { get; set; } = null;

    [JsonPropertyName("instagramId")]
    public string? InstagramId { get; set; } = null;

    [JsonPropertyName("twitterId")]
    public string? TwitterId { get; set; } = null;

}
