using System;
using System.Text.Json.Serialization;
using System.Collections.Generic;
using Jellyfin.Plugin.JellyseerrBridge.JellyseerrModel.Server;

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
    JELLYFIN = 0,
    EMBY = 1
}

