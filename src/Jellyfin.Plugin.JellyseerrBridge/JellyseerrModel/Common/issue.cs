using System;
using System.Text.Json.Serialization;
using System.Collections.Generic;

namespace Jellyfin.Plugin.JellyseerrBridge.JellyseerrModel;

public enum IssueType
{
    VIDEO = 1,
    AUDIO = 2,
    SUBTITLES = 3,
    OTHER = 4
}

public enum IssueStatus
{
    OPEN = 1,
    RESOLVED = 2
}

