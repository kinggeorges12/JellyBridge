using System;
using System.Text.Json.Serialization;
using System.Collections.Generic;

namespace Jellyfin.Plugin.JellyseerrBridge.JellyseerrModel;

public enum ApiErrorCode
{
    [JsonPropertyName("INVALID_URL")]
    InvalidUrl = 0,
    [JsonPropertyName("INVALID_CREDENTIALS")]
    InvalidCredentials = 1,
    [JsonPropertyName("INVALID_AUTH_TOKEN")]
    InvalidAuthToken = 2,
    [JsonPropertyName("INVALID_EMAIL")]
    InvalidEmail = 3,
    [JsonPropertyName("NOT_ADMIN")]
    NotAdmin = 4,
    [JsonPropertyName("NO_ADMIN_USER")]
    NoAdminUser = 5,
    [JsonPropertyName("SYNC_ERROR_GROUPED_FOLDERS")]
    SyncErrorGroupedFolders = 6,
    [JsonPropertyName("SYNC_ERROR_NO_LIBRARIES")]
    SyncErrorNoLibraries = 7,
    [JsonPropertyName("UNAUTHORIZED")]
    Unauthorized = 8,
    [JsonPropertyName("UNKNOWN")]
    Unknown = 9
}

