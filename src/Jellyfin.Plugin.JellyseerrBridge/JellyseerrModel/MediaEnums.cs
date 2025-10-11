using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.JellyseerrBridge.Models;

/// <summary>
/// Enums imported from TypeScript constants files
/// </summary>

public enum MediaRequestStatus
{
    PENDING = 1,
    APPROVED = 2,
    DECLINED = 3,
    FAILED = 4,
    COMPLETED = 5,
}

public static class MediaRequestStatusExtensions
{
    public static string ToStringValue(this MediaRequestStatus value)
    {
        return value.ToString().ToLowerInvariant();
    }

    public static MediaRequestStatus FromString(string value)
    {
        if (Enum.TryParse<MediaRequestStatus>(value, true, out var result))
            return result;
        return MediaRequestStatus.PENDING;
    }
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum MediaType
{
    [JsonPropertyName("movie")]
    MOVIE = 1,
    [JsonPropertyName("tv")]
    TV = 2,
}

public static class MediaTypeExtensions
{
    public static string ToStringValue(this MediaType value)
    {
        return value.ToString().ToLowerInvariant();
    }

    public static MediaType FromString(string value)
    {
        if (Enum.TryParse<MediaType>(value, true, out var result))
            return result;
        return MediaType.MOVIE;
    }
}

public enum MediaStatus
{
    UNKNOWN = 1,
    PENDING = 2,
    PROCESSING = 3,
    PARTIALLY_AVAILABLE = 4,
    AVAILABLE = 5,
    BLACKLISTED = 6,
    DELETED = 7,
}

public static class MediaStatusExtensions
{
    public static string ToStringValue(this MediaStatus value)
    {
        return value.ToString().ToLowerInvariant();
    }

    public static MediaStatus FromString(string value)
    {
        if (Enum.TryParse<MediaStatus>(value, true, out var result))
            return result;
        return MediaStatus.UNKNOWN;
    }
}


