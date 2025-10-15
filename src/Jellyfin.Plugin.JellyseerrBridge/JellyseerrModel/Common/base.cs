using System;
using System.Text.Json.Serialization;
using System.Collections.Generic;

namespace Jellyfin.Plugin.JellyseerrBridge.JellyseerrModel;

public class SystemStatus
{
    [JsonPropertyName("version")]
    public string Version { get; set; } = string.Empty;

    [JsonPropertyName("buildTime")]
    public DateTimeOffset BuildTime { get; set; }

    [JsonPropertyName("isDebug")]
    public bool IsDebug { get; set; }

    [JsonPropertyName("isProduction")]
    public bool IsProduction { get; set; }

    [JsonPropertyName("isAdmin")]
    public bool IsAdmin { get; set; }

    [JsonPropertyName("isUserInteractive")]
    public bool IsUserInteractive { get; set; }

    [JsonPropertyName("startupPath")]
    public string StartupPath { get; set; } = string.Empty;

    [JsonPropertyName("appData")]
    public string AppData { get; set; } = string.Empty;

    [JsonPropertyName("osName")]
    public string OsName { get; set; } = string.Empty;

    [JsonPropertyName("osVersion")]
    public string OsVersion { get; set; } = string.Empty;

    [JsonPropertyName("isNetCore")]
    public bool IsNetCore { get; set; }

    [JsonPropertyName("isMono")]
    public bool IsMono { get; set; }

    [JsonPropertyName("isLinux")]
    public bool IsLinux { get; set; }

    [JsonPropertyName("isOsx")]
    public bool IsOsx { get; set; }

    [JsonPropertyName("isWindows")]
    public bool IsWindows { get; set; }

    [JsonPropertyName("isDocker")]
    public bool IsDocker { get; set; }

    [JsonPropertyName("mode")]
    public string Mode { get; set; } = string.Empty;

    [JsonPropertyName("branch")]
    public string Branch { get; set; } = string.Empty;

    [JsonPropertyName("authentication")]
    public string Authentication { get; set; } = string.Empty;

    [JsonPropertyName("sqliteVersion")]
    public string SqliteVersion { get; set; } = string.Empty;

    [JsonPropertyName("migrationVersion")]
    public int MigrationVersion { get; set; }

    [JsonPropertyName("urlBase")]
    public string UrlBase { get; set; } = string.Empty;

    [JsonPropertyName("runtimeVersion")]
    public string RuntimeVersion { get; set; } = string.Empty;

    [JsonPropertyName("runtimeName")]
    public string RuntimeName { get; set; } = string.Empty;

    [JsonPropertyName("startTime")]
    public DateTimeOffset StartTime { get; set; }

    [JsonPropertyName("packageUpdateMechanism")]
    public string PackageUpdateMechanism { get; set; } = string.Empty;

}

public class RootFolder
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("path")]
    public string Path { get; set; } = string.Empty;

    [JsonPropertyName("freeSpace")]
    public int FreeSpace { get; set; }

    [JsonPropertyName("totalSpace")]
    public int TotalSpace { get; set; }

    [JsonPropertyName("unmappedFolders")]
    public List<RootFolderUnmappedFolders> UnmappedFolders { get; set; } = new();

}

public class QualityProfile
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

}

public class QueueItem
{
    [JsonPropertyName("size")]
    public int Size { get; set; }

    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("sizeleft")]
    public int Sizeleft { get; set; }

    [JsonPropertyName("timeleft")]
    public string Timeleft { get; set; } = string.Empty;

    [JsonPropertyName("estimatedCompletionTime")]
    public string EstimatedCompletionTime { get; set; } = string.Empty;

    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    [JsonPropertyName("trackedDownloadStatus")]
    public string TrackedDownloadStatus { get; set; } = string.Empty;

    [JsonPropertyName("trackedDownloadState")]
    public string TrackedDownloadState { get; set; } = string.Empty;

    [JsonPropertyName("downloadId")]
    public string DownloadId { get; set; } = string.Empty;

    [JsonPropertyName("protocol")]
    public string Protocol { get; set; } = string.Empty;

    [JsonPropertyName("downloadClient")]
    public string DownloadClient { get; set; } = string.Empty;

    [JsonPropertyName("indexer")]
    public string Indexer { get; set; } = string.Empty;

    [JsonPropertyName("id")]
    public int Id { get; set; }

}

public class Tag
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("label")]
    public string Label { get; set; } = string.Empty;

}

public class QueueResponse
{
    [JsonPropertyName("page")]
    public int Page { get; set; }

    [JsonPropertyName("pageSize")]
    public int PageSize { get; set; }

    [JsonPropertyName("sortKey")]
    public string SortKey { get; set; } = string.Empty;

    [JsonPropertyName("sortDirection")]
    public string SortDirection { get; set; } = string.Empty;

    [JsonPropertyName("totalRecords")]
    public int TotalRecords { get; set; }

    [JsonPropertyName("records")]
    public List<object> Records { get; set; } = new();

}



public class RootFolderUnmappedFolders
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("path")]
    public string Path { get; set; } = string.Empty;

}