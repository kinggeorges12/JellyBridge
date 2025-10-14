using System;
using System.Text.Json.Serialization;
using System.Collections.Generic;
using Jellyfin.Plugin.JellyseerrBridge.JellyseerrModel;

namespace Jellyfin.Plugin.JellyseerrBridge.JellyseerrModel.Api;

public class UserSettingsGeneralResponse
{
    [JsonPropertyName("username")]
    public string? Username { get; set; } = null!;

    [JsonPropertyName("email")]
    public string? Email { get; set; } = null!;

    [JsonPropertyName("discordId")]
    public string? DiscordId { get; set; } = null!;

    [JsonPropertyName("locale")]
    public string? Locale { get; set; } = null!;

    [JsonPropertyName("discoverRegion")]
    public string? DiscoverRegion { get; set; } = null!;

    [JsonPropertyName("streamingRegion")]
    public string? StreamingRegion { get; set; } = null!;

    [JsonPropertyName("originalLanguage")]
    public string? OriginalLanguage { get; set; } = null!;

    [JsonPropertyName("movieQuotaLimit")]
    public int? MovieQuotaLimit { get; set; } = null!;

    [JsonPropertyName("movieQuotaDays")]
    public int? MovieQuotaDays { get; set; } = null!;

    [JsonPropertyName("tvQuotaLimit")]
    public int? TvQuotaLimit { get; set; } = null!;

    [JsonPropertyName("tvQuotaDays")]
    public int? TvQuotaDays { get; set; } = null!;

    [JsonPropertyName("globalMovieQuotaDays")]
    public int? GlobalMovieQuotaDays { get; set; } = null!;

    [JsonPropertyName("globalMovieQuotaLimit")]
    public int? GlobalMovieQuotaLimit { get; set; } = null!;

    [JsonPropertyName("globalTvQuotaLimit")]
    public int? GlobalTvQuotaLimit { get; set; } = null!;

    [JsonPropertyName("globalTvQuotaDays")]
    public int? GlobalTvQuotaDays { get; set; } = null!;

    [JsonPropertyName("watchlistSyncMovies")]
    public bool? WatchlistSyncMovies { get; set; } = null!;

    [JsonPropertyName("watchlistSyncTv")]
    public bool? WatchlistSyncTv { get; set; } = null!;

}

public class UserSettingsNotificationsResponse
{
    [JsonPropertyName("emailEnabled")]
    public bool? EmailEnabled { get; set; } = null!;

    [JsonPropertyName("pgpKey")]
    public string? PgpKey { get; set; } = null!;

    [JsonPropertyName("discordEnabled")]
    public bool? DiscordEnabled { get; set; } = null!;

    [JsonPropertyName("discordEnabledTypes")]
    public int? DiscordEnabledTypes { get; set; } = null!;

    [JsonPropertyName("discordId")]
    public string? DiscordId { get; set; } = null!;

    [JsonPropertyName("pushbulletAccessToken")]
    public string? PushbulletAccessToken { get; set; } = null!;

    [JsonPropertyName("pushoverApplicationToken")]
    public string? PushoverApplicationToken { get; set; } = null!;

    [JsonPropertyName("pushoverUserKey")]
    public string? PushoverUserKey { get; set; } = null!;

    [JsonPropertyName("pushoverSound")]
    public string? PushoverSound { get; set; } = null!;

    [JsonPropertyName("telegramEnabled")]
    public bool? TelegramEnabled { get; set; } = null!;

    [JsonPropertyName("telegramBotUsername")]
    public string? TelegramBotUsername { get; set; } = null!;

    [JsonPropertyName("telegramChatId")]
    public string? TelegramChatId { get; set; } = null!;

    [JsonPropertyName("telegramMessageThreadId")]
    public string? TelegramMessageThreadId { get; set; } = null!;

    [JsonPropertyName("telegramSendSilently")]
    public bool? TelegramSendSilently { get; set; } = null!;

    [JsonPropertyName("webPushEnabled")]
    public bool? WebPushEnabled { get; set; } = null!;

    [JsonPropertyName("notificationTypes")]
    // TypeScript: Partial<NotificationAgentTypes>
    public NotificationAgentTypes NotificationTypes { get; set; } = new();

}

public class NotificationAgentTypes
{
    public Dictionary<NotificationAgentKey, int> Value { get; set; } = new();

}

