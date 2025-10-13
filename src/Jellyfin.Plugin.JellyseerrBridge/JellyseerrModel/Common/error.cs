using System;
using System.Text.Json.Serialization;
using System.Collections.Generic;

namespace Jellyfin.Plugin.JellyseerrBridge.JellyseerrModel;

public enum ApiErrorCode
{
    InvalidUrl = 0,
    InvalidCredentials = 1,
    InvalidAuthToken = 2,
    InvalidEmail = 3,
    NotAdmin = 4,
    NoAdminUser = 5,
    SyncErrorGroupedFolders = 6,
    SyncErrorNoLibraries = 7,
    Unauthorized = 8,
    Unknown = 9
}

