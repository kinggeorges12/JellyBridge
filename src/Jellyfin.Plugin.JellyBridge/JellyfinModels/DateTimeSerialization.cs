using System;
using System.Globalization;

namespace Jellyfin.Plugin.JellyBridge.JellyfinModels
{
    /// <summary>
    /// Utilities for producing version-tolerant, consistent DateTime serialization
    /// across Jellyfin 10.10/10.11 differences.
    /// </summary>
    public static class DateTimeSerialization
    {
        /// <summary>
        /// Converts a DateTime? to a UTC ISO-8601 string with a trailing 'Z'.
        /// Handles Unspecified and Local kinds safely so the frontend receives a
        /// consistent representation regardless of server serializer behavior.
        /// </summary>
        public static string? ToIso8601UtcString(DateTime? dateTime)
        {
            if (!dateTime.HasValue)
            {
                return null;
            }

            var value = dateTime.Value;

            DateTime utc = value.Kind switch
            {
                DateTimeKind.Utc => value,
                DateTimeKind.Local => value.ToUniversalTime(),
                DateTimeKind.Unspecified => DateTime.SpecifyKind(value, DateTimeKind.Utc),
                _ => value
            };

            // Format with an explicit trailing 'Z' to indicate UTC, ensuring consistent parsing in browsers
            return utc.ToString("yyyy'-'MM'-'dd'T'HH':'mm':'ss'.'fffffff'Z'", CultureInfo.InvariantCulture);
        }
    }
}









