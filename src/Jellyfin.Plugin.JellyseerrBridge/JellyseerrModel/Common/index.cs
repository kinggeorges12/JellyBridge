using System;
using System.Text.Json.Serialization;
using System.Collections.Generic;
using Jellyfin.Plugin.JellyseerrBridge.JellyseerrModel.Server;

namespace Jellyfin.Plugin.JellyseerrBridge.JellyseerrModel;

public class Library
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; }

    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    [JsonPropertyName("lastScan")]
    public int? LastScan { get; set; } = null!;

}

public enum LibraryType
{
    Show = 0,
    Movie
}

public class Region
{
    [JsonPropertyName("iso_3166_1")]
    public string Iso31661 { get; set; } = string.Empty;

    [JsonPropertyName("english_name")]
    public string EnglishName { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string? Name { get; set; } = null!;

}

public class Language
{
    [JsonPropertyName("iso_639_1")]
    public string Iso6391 { get; set; } = string.Empty;

    [JsonPropertyName("english_name")]
    public string EnglishName { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

}

public class PlexSettings
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("machineId")]
    public string? MachineId { get; set; } = null!;

    [JsonPropertyName("ip")]
    public string Ip { get; set; } = string.Empty;

    [JsonPropertyName("port")]
    public int Port { get; set; }

    [JsonPropertyName("useSsl")]
    public bool? UseSsl { get; set; } = null!;

    [JsonPropertyName("libraries")]
    public List<Library> Libraries { get; set; } = new();

    [JsonPropertyName("webAppUrl")]
    public string? WebAppUrl { get; set; } = null!;

}

public class JellyfinSettings
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("ip")]
    public string Ip { get; set; } = string.Empty;

    [JsonPropertyName("port")]
    public int Port { get; set; }

    [JsonPropertyName("useSsl")]
    public bool? UseSsl { get; set; } = null!;

    [JsonPropertyName("urlBase")]
    public string? UrlBase { get; set; } = null!;

    [JsonPropertyName("externalHostname")]
    public string? ExternalHostname { get; set; } = null!;

    [JsonPropertyName("jellyfinForgotPasswordUrl")]
    public string? JellyfinForgotPasswordUrl { get; set; } = null!;

    [JsonPropertyName("libraries")]
    public List<Library> Libraries { get; set; } = new();

    [JsonPropertyName("serverId")]
    public string ServerId { get; set; } = string.Empty;

    [JsonPropertyName("apiKey")]
    public string ApiKey { get; set; } = string.Empty;

}

public class TautulliSettings
{
    [JsonPropertyName("hostname")]
    public string? Hostname { get; set; } = null!;

    [JsonPropertyName("port")]
    public int? Port { get; set; } = null!;

    [JsonPropertyName("useSsl")]
    public bool? UseSsl { get; set; } = null!;

    [JsonPropertyName("urlBase")]
    public string? UrlBase { get; set; } = null!;

    [JsonPropertyName("apiKey")]
    public string? ApiKey { get; set; } = null!;

    [JsonPropertyName("externalUrl")]
    public string? ExternalUrl { get; set; } = null!;

}

public class DVRSettings
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("hostname")]
    public string Hostname { get; set; } = string.Empty;

    [JsonPropertyName("port")]
    public int Port { get; set; }

    [JsonPropertyName("apiKey")]
    public string ApiKey { get; set; } = string.Empty;

    [JsonPropertyName("useSsl")]
    public bool UseSsl { get; set; }

    [JsonPropertyName("baseUrl")]
    public string? BaseUrl { get; set; } = null!;

    [JsonPropertyName("activeProfileId")]
    public int ActiveProfileId { get; set; }

    [JsonPropertyName("activeProfileName")]
    public string ActiveProfileName { get; set; } = string.Empty;

    [JsonPropertyName("activeDirectory")]
    public string ActiveDirectory { get; set; } = string.Empty;

    [JsonPropertyName("tags")]
    public List<int> Tags { get; set; } = new();

    [JsonPropertyName("is4k")]
    public bool Is4k { get; set; }

    [JsonPropertyName("isDefault")]
    public bool IsDefault { get; set; }

    [JsonPropertyName("externalUrl")]
    public string? ExternalUrl { get; set; } = null!;

    [JsonPropertyName("syncEnabled")]
    public bool SyncEnabled { get; set; }

    [JsonPropertyName("preventSearch")]
    public bool PreventSearch { get; set; }

    [JsonPropertyName("tagRequests")]
    public bool TagRequests { get; set; }

    [JsonPropertyName("overrideRule")]
    public List<int> OverrideRule { get; set; } = new();

}

public class RadarrSettings : DVRSettings
{
    [JsonPropertyName("minimumAvailability")]
    public string MinimumAvailability { get; set; } = string.Empty;

}

public class SonarrSettings : DVRSettings
{
    [JsonPropertyName("seriesType")]
    public string SeriesType { get; set; } = string.Empty;

    [JsonPropertyName("animeSeriesType")]
    public string AnimeSeriesType { get; set; } = string.Empty;

    [JsonPropertyName("activeAnimeProfileId")]
    public int? ActiveAnimeProfileId { get; set; } = null!;

    [JsonPropertyName("activeAnimeProfileName")]
    public string? ActiveAnimeProfileName { get; set; } = null!;

    [JsonPropertyName("activeAnimeDirectory")]
    public string? ActiveAnimeDirectory { get; set; } = null!;

    [JsonPropertyName("activeAnimeLanguageProfileId")]
    public int? ActiveAnimeLanguageProfileId { get; set; } = null!;

    [JsonPropertyName("activeLanguageProfileId")]
    public int? ActiveLanguageProfileId { get; set; } = null!;

    [JsonPropertyName("animeTags")]
    public List<int>? AnimeTags { get; set; } = new();

    [JsonPropertyName("enableSeasonFolders")]
    public bool EnableSeasonFolders { get; set; }

}

public enum SeriesType
{
    Standard = 0,
    Daily,
    Anime
}

public enum AnimeSeriesType
{
    Standard = 0,
    Daily,
    Anime
}

public class Quota
{
    [JsonPropertyName("quotaLimit")]
    public int? QuotaLimit { get; set; } = null!;

    [JsonPropertyName("quotaDays")]
    public int? QuotaDays { get; set; } = null!;

}

public class ProxySettings
{
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; }

    [JsonPropertyName("hostname")]
    public string Hostname { get; set; } = string.Empty;

    [JsonPropertyName("port")]
    public int Port { get; set; }

    [JsonPropertyName("useSsl")]
    public bool UseSsl { get; set; }

    [JsonPropertyName("user")]
    public string User { get; set; } = string.Empty;

    [JsonPropertyName("password")]
    public string Password { get; set; } = string.Empty;

    [JsonPropertyName("bypassFilter")]
    public string BypassFilter { get; set; } = string.Empty;

    [JsonPropertyName("bypassLocalAddresses")]
    public bool BypassLocalAddresses { get; set; }

}

public class MainSettings
{
    [JsonPropertyName("apiKey")]
    public string ApiKey { get; set; } = string.Empty;

    [JsonPropertyName("applicationTitle")]
    public string ApplicationTitle { get; set; } = string.Empty;

    [JsonPropertyName("applicationUrl")]
    public string ApplicationUrl { get; set; } = string.Empty;

    [JsonPropertyName("cacheImages")]
    public bool CacheImages { get; set; }

    [JsonPropertyName("defaultPermissions")]
    public int DefaultPermissions { get; set; }

    [JsonPropertyName("defaultQuotas")]
    public MainSettingsDefaultQuotas DefaultQuotas { get; set; } = new();

    [JsonPropertyName("hideAvailable")]
    public bool HideAvailable { get; set; }

    [JsonPropertyName("hideBlacklisted")]
    public bool HideBlacklisted { get; set; }

    [JsonPropertyName("localLogin")]
    public bool LocalLogin { get; set; }

    [JsonPropertyName("mediaServerLogin")]
    public bool MediaServerLogin { get; set; }

    [JsonPropertyName("newPlexLogin")]
    public bool NewPlexLogin { get; set; }

    [JsonPropertyName("discoverRegion")]
    public string DiscoverRegion { get; set; } = string.Empty;

    [JsonPropertyName("streamingRegion")]
    public string StreamingRegion { get; set; } = string.Empty;

    [JsonPropertyName("originalLanguage")]
    public string OriginalLanguage { get; set; } = string.Empty;

    [JsonPropertyName("blacklistedTags")]
    public string BlacklistedTags { get; set; } = string.Empty;

    [JsonPropertyName("blacklistedTagsLimit")]
    public int BlacklistedTagsLimit { get; set; }

    [JsonPropertyName("mediaServerType")]
    public int MediaServerType { get; set; }

    [JsonPropertyName("partialRequestsEnabled")]
    public bool PartialRequestsEnabled { get; set; }

    [JsonPropertyName("enableSpecialEpisodes")]
    public bool EnableSpecialEpisodes { get; set; }

    [JsonPropertyName("locale")]
    public string Locale { get; set; } = string.Empty;

    [JsonPropertyName("youtubeUrl")]
    public string YoutubeUrl { get; set; } = string.Empty;

}

public class NetworkSettings
{
    [JsonPropertyName("csrfProtection")]
    public bool CsrfProtection { get; set; }

    [JsonPropertyName("forceIpv4First")]
    public bool ForceIpv4First { get; set; }

    [JsonPropertyName("trustProxy")]
    public bool TrustProxy { get; set; }

    [JsonPropertyName("proxy")]
    public ProxySettings Proxy { get; set; } = new();

}

public class PublicSettings
{
    [JsonPropertyName("initialized")]
    public bool Initialized { get; set; }

}

public class FullPublicSettings : PublicSettings
{
    [JsonPropertyName("applicationTitle")]
    public string ApplicationTitle { get; set; } = string.Empty;

    [JsonPropertyName("applicationUrl")]
    public string ApplicationUrl { get; set; } = string.Empty;

    [JsonPropertyName("hideAvailable")]
    public bool HideAvailable { get; set; }

    [JsonPropertyName("hideBlacklisted")]
    public bool HideBlacklisted { get; set; }

    [JsonPropertyName("localLogin")]
    public bool LocalLogin { get; set; }

    [JsonPropertyName("mediaServerLogin")]
    public bool MediaServerLogin { get; set; }

    [JsonPropertyName("movie4kEnabled")]
    public bool Movie4kEnabled { get; set; }

    [JsonPropertyName("series4kEnabled")]
    public bool Series4kEnabled { get; set; }

    [JsonPropertyName("discoverRegion")]
    public string DiscoverRegion { get; set; } = string.Empty;

    [JsonPropertyName("streamingRegion")]
    public string StreamingRegion { get; set; } = string.Empty;

    [JsonPropertyName("originalLanguage")]
    public string OriginalLanguage { get; set; } = string.Empty;

    [JsonPropertyName("mediaServerType")]
    public int MediaServerType { get; set; }

    [JsonPropertyName("jellyfinExternalHost")]
    public string? JellyfinExternalHost { get; set; } = null!;

    [JsonPropertyName("jellyfinForgotPasswordUrl")]
    public string? JellyfinForgotPasswordUrl { get; set; } = null!;

    [JsonPropertyName("jellyfinServerName")]
    public string? JellyfinServerName { get; set; } = null!;

    [JsonPropertyName("partialRequestsEnabled")]
    public bool PartialRequestsEnabled { get; set; }

    [JsonPropertyName("enableSpecialEpisodes")]
    public bool EnableSpecialEpisodes { get; set; }

    [JsonPropertyName("cacheImages")]
    public bool CacheImages { get; set; }

    [JsonPropertyName("vapidPublic")]
    public string VapidPublic { get; set; } = string.Empty;

    [JsonPropertyName("enablePushRegistration")]
    public bool EnablePushRegistration { get; set; }

    [JsonPropertyName("locale")]
    public string Locale { get; set; } = string.Empty;

    [JsonPropertyName("emailEnabled")]
    public bool EmailEnabled { get; set; }

    [JsonPropertyName("userEmailRequired")]
    public bool UserEmailRequired { get; set; }

    [JsonPropertyName("newPlexLogin")]
    public bool NewPlexLogin { get; set; }

    [JsonPropertyName("youtubeUrl")]
    public string YoutubeUrl { get; set; } = string.Empty;

}

public class NotificationAgentConfig
{
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; }

    [JsonPropertyName("types")]
    public int? Types { get; set; } = null!;

    [JsonPropertyName("options")]
    // TypeScript: Record<string, unknown>
    public Dictionary<string, object> Options { get; set; } = new();

}

public class NotificationAgentDiscord : NotificationAgentConfig
{
    [JsonPropertyName("options")]
    public new NotificationAgentDiscordOptions Options { get; set; } = new();

}

public class NotificationAgentSlack : NotificationAgentConfig
{
    [JsonPropertyName("options")]
    public new NotificationAgentSlackOptions Options { get; set; } = new();

}

public class NotificationAgentEmail : NotificationAgentConfig
{
    [JsonPropertyName("options")]
    public new NotificationAgentEmailOptions Options { get; set; } = new();

}

public class NotificationAgentTelegram : NotificationAgentConfig
{
    [JsonPropertyName("options")]
    public new NotificationAgentTelegramOptions Options { get; set; } = new();

}

public class NotificationAgentPushbullet : NotificationAgentConfig
{
    [JsonPropertyName("options")]
    public new NotificationAgentPushbulletOptions Options { get; set; } = new();

}

public class NotificationAgentPushover : NotificationAgentConfig
{
    [JsonPropertyName("options")]
    public new NotificationAgentPushoverOptions Options { get; set; } = new();

}

public class NotificationAgentWebhook : NotificationAgentConfig
{
    [JsonPropertyName("options")]
    public new NotificationAgentWebhookOptions Options { get; set; } = new();

}

public class NotificationAgentGotify : NotificationAgentConfig
{
    [JsonPropertyName("options")]
    public new NotificationAgentGotifyOptions Options { get; set; } = new();

}

public class NotificationAgentNtfy : NotificationAgentConfig
{
    [JsonPropertyName("options")]
    public new NotificationAgentNtfyOptions Options { get; set; } = new();

}

public class NotificationAgents
{
    [JsonPropertyName("discord")]
    public NotificationAgentDiscord Discord { get; set; } = new();

    [JsonPropertyName("email")]
    public NotificationAgentEmail Email { get; set; } = new();

    [JsonPropertyName("gotify")]
    public NotificationAgentGotify Gotify { get; set; } = new();

    [JsonPropertyName("ntfy")]
    public NotificationAgentNtfy Ntfy { get; set; } = new();

    [JsonPropertyName("pushbullet")]
    public NotificationAgentPushbullet Pushbullet { get; set; } = new();

    [JsonPropertyName("pushover")]
    public NotificationAgentPushover Pushover { get; set; } = new();

    [JsonPropertyName("slack")]
    public NotificationAgentSlack Slack { get; set; } = new();

    [JsonPropertyName("telegram")]
    public NotificationAgentTelegram Telegram { get; set; } = new();

    [JsonPropertyName("webhook")]
    public NotificationAgentWebhook Webhook { get; set; } = new();

    [JsonPropertyName("webpush")]
    public NotificationAgentConfig Webpush { get; set; } = new();

}

public class NotificationSettings
{
    [JsonPropertyName("agents")]
    public NotificationAgents Agents { get; set; } = new();

}

public class JobSettings
{
    [JsonPropertyName("schedule")]
    public string Schedule { get; set; } = string.Empty;

}

public class AllSettings
{
    [JsonPropertyName("clientId")]
    public string ClientId { get; set; } = string.Empty;

    [JsonPropertyName("vapidPublic")]
    public string VapidPublic { get; set; } = string.Empty;

    [JsonPropertyName("vapidPrivate")]
    public string VapidPrivate { get; set; } = string.Empty;

    [JsonPropertyName("main")]
    public MainSettings Main { get; set; } = new();

    [JsonPropertyName("plex")]
    public PlexSettings Plex { get; set; } = new();

    [JsonPropertyName("jellyfin")]
    public JellyfinSettings Jellyfin { get; set; } = new();

    [JsonPropertyName("tautulli")]
    public TautulliSettings Tautulli { get; set; } = new();

    [JsonPropertyName("radarr")]
    public List<RadarrSettings> Radarr { get; set; } = new();

    [JsonPropertyName("sonarr")]
    public List<SonarrSettings> Sonarr { get; set; } = new();

    [JsonPropertyName("public")]
    public PublicSettings Public { get; set; } = new();

    [JsonPropertyName("notifications")]
    public NotificationSettings Notifications { get; set; } = new();

    [JsonPropertyName("jobs")]
    public Dictionary<JobId, JobSettings> Jobs { get; set; } = new();

    [JsonPropertyName("network")]
    public NetworkSettings Network { get; set; } = new();

}

public enum NotificationAgentKey
{
    DISCORD = 0,
    EMAIL = 1,
    GOTIFY = 2,
    NTFY = 3,
    PUSHBULLET = 4,
    PUSHOVER = 5,
    SLACK = 6,
    TELEGRAM = 7,
    WEBHOOK = 8,
    WEBPUSH = 9
}

public enum JobId
{
    PlexRecentlyAddedScan,
    PlexFullScan,
    PlexWatchlistSync,
    PlexRefreshToken,
    RadarrScan,
    SonarrScan,
    DownloadSync,
    DownloadSyncReset,
    JellyfinRecentlyAddedScan,
    JellyfinFullScan,
    ImageCacheCleanup,
    AvailabilitySync,
    ProcessBlacklistedTags
}



public class MainSettingsDefaultQuotas
{
    [JsonPropertyName("movie")]
    public Quota Movie { get; set; } = new();

    [JsonPropertyName("tv")]
    public Quota Tv { get; set; } = new();

}

public class NotificationAgentDiscordOptions
{
    [JsonPropertyName("botUsername")]
    public string? BotUsername { get; set; } = null!;

    [JsonPropertyName("botAvatarUrl")]
    public string? BotAvatarUrl { get; set; } = null!;

    [JsonPropertyName("webhookUrl")]
    public string WebhookUrl { get; set; } = string.Empty;

    [JsonPropertyName("webhookRoleId")]
    public string? WebhookRoleId { get; set; } = null!;

    [JsonPropertyName("enableMentions")]
    public bool EnableMentions { get; set; }

}

public class NotificationAgentSlackOptions
{
    [JsonPropertyName("webhookUrl")]
    public string WebhookUrl { get; set; } = string.Empty;

}

public class NotificationAgentEmailOptions
{
    [JsonPropertyName("userEmailRequired")]
    public bool UserEmailRequired { get; set; }

    [JsonPropertyName("emailFrom")]
    public string EmailFrom { get; set; } = string.Empty;

    [JsonPropertyName("smtpHost")]
    public string SmtpHost { get; set; } = string.Empty;

    [JsonPropertyName("smtpPort")]
    public int SmtpPort { get; set; }

    [JsonPropertyName("secure")]
    public bool Secure { get; set; }

    [JsonPropertyName("ignoreTls")]
    public bool IgnoreTls { get; set; }

    [JsonPropertyName("requireTls")]
    public bool RequireTls { get; set; }

    [JsonPropertyName("authUser")]
    public string? AuthUser { get; set; } = null!;

    [JsonPropertyName("authPass")]
    public string? AuthPass { get; set; } = null!;

    [JsonPropertyName("allowSelfSigned")]
    public bool AllowSelfSigned { get; set; }

    [JsonPropertyName("senderName")]
    public string SenderName { get; set; } = string.Empty;

    [JsonPropertyName("pgpPrivateKey")]
    public string? PgpPrivateKey { get; set; } = null!;

    [JsonPropertyName("pgpPassword")]
    public string? PgpPassword { get; set; } = null!;

}

public class NotificationAgentTelegramOptions
{
    [JsonPropertyName("botUsername")]
    public string? BotUsername { get; set; } = null!;

    [JsonPropertyName("botAPI")]
    public string BotAPI { get; set; } = string.Empty;

    [JsonPropertyName("chatId")]
    public string ChatId { get; set; } = string.Empty;

    [JsonPropertyName("messageThreadId")]
    public string MessageThreadId { get; set; } = string.Empty;

    [JsonPropertyName("sendSilently")]
    public bool SendSilently { get; set; }

}

public class NotificationAgentPushbulletOptions
{
    [JsonPropertyName("accessToken")]
    public string AccessToken { get; set; } = string.Empty;

    [JsonPropertyName("channelTag")]
    public string? ChannelTag { get; set; } = null!;

}

public class NotificationAgentPushoverOptions
{
    [JsonPropertyName("accessToken")]
    public string AccessToken { get; set; } = string.Empty;

    [JsonPropertyName("userToken")]
    public string UserToken { get; set; } = string.Empty;

    [JsonPropertyName("sound")]
    public string Sound { get; set; } = string.Empty;

}

public class NotificationAgentWebhookOptions
{
    [JsonPropertyName("webhookUrl")]
    public string WebhookUrl { get; set; } = string.Empty;

    [JsonPropertyName("jsonPayload")]
    public string JsonPayload { get; set; } = string.Empty;

    [JsonPropertyName("authHeader")]
    public string? AuthHeader { get; set; } = null!;

}

public class NotificationAgentGotifyOptions
{
    [JsonPropertyName("url")]
    public string Url { get; set; } = string.Empty;

    [JsonPropertyName("token")]
    public string Token { get; set; } = string.Empty;

    [JsonPropertyName("priority")]
    public int Priority { get; set; }

}

public class NotificationAgentNtfyOptions
{
    [JsonPropertyName("url")]
    public string Url { get; set; } = string.Empty;

    [JsonPropertyName("topic")]
    public string Topic { get; set; } = string.Empty;

    [JsonPropertyName("authMethodUsernamePassword")]
    public bool? AuthMethodUsernamePassword { get; set; } = null!;

    [JsonPropertyName("username")]
    public string? Username { get; set; } = null!;

    [JsonPropertyName("password")]
    public string? Password { get; set; } = null!;

    [JsonPropertyName("authMethodToken")]
    public bool? AuthMethodToken { get; set; } = null!;

    [JsonPropertyName("token")]
    public string? Token { get; set; } = null!;

}