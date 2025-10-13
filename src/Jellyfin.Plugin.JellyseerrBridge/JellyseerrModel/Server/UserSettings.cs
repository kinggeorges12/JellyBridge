using System;
using System.Text.Json.Serialization;
using System.Collections.Generic;
using Jellyfin.Plugin.JellyseerrBridge.JellyseerrModel;
using Jellyfin.Plugin.JellyseerrBridge.JellyseerrModel.Api;

namespace Jellyfin.Plugin.JellyseerrBridge.JellyseerrModel.Server;

public class UserSettings
{
    [JsonPropertyName("id")]
    public int Id { get; set; }
    [JsonPropertyName("user")]
    public User User { get; set; } = new();
    [JsonPropertyName("locale")]
    public string Locale { get; set; } = string.Empty;
    [JsonPropertyName("discoverRegion")]
    public string DiscoverRegion { get; set; } = string.Empty;
    [JsonPropertyName("streamingRegion")]
    public string StreamingRegion { get; set; } = string.Empty;
    [JsonPropertyName("originalLanguage")]
    public string OriginalLanguage { get; set; } = string.Empty;
    [JsonPropertyName("pgpKey")]
    public string PgpKey { get; set; } = string.Empty;
    [JsonPropertyName("discordId")]
    public string DiscordId { get; set; } = string.Empty;
    [JsonPropertyName("pushbulletAccessToken")]
    public string PushbulletAccessToken { get; set; } = string.Empty;
    [JsonPropertyName("pushoverApplicationToken")]
    public string PushoverApplicationToken { get; set; } = string.Empty;
    [JsonPropertyName("pushoverUserKey")]
    public string PushoverUserKey { get; set; } = string.Empty;
    [JsonPropertyName("pushoverSound")]
    public string PushoverSound { get; set; } = string.Empty;
    [JsonPropertyName("telegramChatId")]
    public string TelegramChatId { get; set; } = string.Empty;
    [JsonPropertyName("telegramMessageThreadId")]
    public string TelegramMessageThreadId { get; set; } = string.Empty;
    [JsonPropertyName("telegramSendSilently")]
    public bool TelegramSendSilently { get; set; }
    [JsonPropertyName("watchlistSyncMovies")]
    public bool WatchlistSyncMovies { get; set; }
    [JsonPropertyName("watchlistSyncTv")]
    public bool WatchlistSyncTv { get; set; }
    [JsonPropertyName("notificationTypes")]
    public NotificationAgentTypes NotificationTypes { get; set; } = new();
}


