using System;
using System.Text.Json.Serialization;
using System.Collections.Generic;

namespace Jellyfin.Plugin.JellyseerrBridge.JellyseerrModel;

public enum MediaRequestStatus
{
    PENDING = 1,
    APPROVED,
    DECLINED,
    FAILED,
    COMPLETED
}

public enum MediaType
{
    MOVIE = 0,
    TV = 1
}

public enum MediaStatus
{
    UNKNOWN = 1,
    PENDING,
    PROCESSING,
    PARTIALLY_AVAILABLE,
    AVAILABLE,
    BLACKLISTED,
    DELETED
}

