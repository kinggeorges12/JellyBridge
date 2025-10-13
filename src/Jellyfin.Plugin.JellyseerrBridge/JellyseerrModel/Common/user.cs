using System;
using System.Text.Json.Serialization;
using System.Collections.Generic;

namespace Jellyfin.Plugin.JellyseerrBridge.JellyseerrModel;

public enum UserType
{
    PLEX = 1,
    LOCAL = 2,
    JELLYFIN = 3,
    EMBY = 4
}

