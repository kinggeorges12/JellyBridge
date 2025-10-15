using System;
using System.Text.Json.Serialization;
using System.Collections.Generic;

namespace Jellyfin.Plugin.JellyseerrBridge.JellyseerrModel;

public enum MediaServerType
{
    PLEX = 1,
    JELLYFIN,
    EMBY,
    NOT_CONFIGURED
}

public enum ServerType
{
    [JsonPropertyName("Jellyfin")]
    JELLYFIN = 0,
    [JsonPropertyName("Emby")]
    EMBY = 1
}

