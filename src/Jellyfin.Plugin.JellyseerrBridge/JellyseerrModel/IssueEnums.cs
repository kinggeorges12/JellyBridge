using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.JellyseerrBridge.Models;

/// <summary>
/// Enums imported from TypeScript constants files
/// </summary>

public enum IssueType
{
    VIDEO = 1,
    AUDIO = 2,
    SUBTITLES = 3,
    OTHER = 4,
}

public static class IssueTypeExtensions
{
    public static string ToStringValue(this IssueType value)
    {
        return value.ToString().ToLowerInvariant();
    }

    public static IssueType FromString(string value)
    {
        if (Enum.TryParse<IssueType>(value, true, out var result))
            return result;
        return IssueType.VIDEO;
    }
}

public enum IssueStatus
{
    OPEN = 1,
    RESOLVED = 2,
}

public static class IssueStatusExtensions
{
    public static string ToStringValue(this IssueStatus value)
    {
        return value.ToString().ToLowerInvariant();
    }

    public static IssueStatus FromString(string value)
    {
        if (Enum.TryParse<IssueStatus>(value, true, out var result))
            return result;
        return IssueStatus.OPEN;
    }
}


