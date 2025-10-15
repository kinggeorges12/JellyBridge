using System.Text.Json;
using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.JellyseerrBridge.Serialization;

/// <summary>
/// Custom JSON converter that skips writing null, empty, or whitespace string values.
/// This prevents unnecessary properties from being written to JSON output.
/// </summary>
public class JellyseerrStringConverter : JsonConverter<string>
{
    /// <summary>
    /// Writes a string value to JSON, skipping null, empty, or whitespace values.
    /// </summary>
    /// <param name="writer">The JSON writer.</param>
    /// <param name="value">The string value to write.</param>
    /// <param name="options">The JSON serializer options.</param>
    public override void Write(Utf8JsonWriter writer, string? value, JsonSerializerOptions options)
    {
        // Skip writing if string is null, empty, or whitespace
        if (string.IsNullOrWhiteSpace(value))
        {
            return; // This prevents writing the property entirely
        }

        writer.WriteStringValue(value);
    }

    /// <summary>
    /// Reads a string value from JSON.
    /// </summary>
    /// <param name="reader">The JSON reader.</param>
    /// <param name="typeToConvert">The type to convert to.</param>
    /// <param name="options">The JSON serializer options.</param>
    /// <returns>The string value or null.</returns>
    public override string? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        // When deserializing, just read the string normally
        return reader.GetString();
    }
}

/// <summary>
/// Static class providing Jellyseerr-specific JSON serialization methods with default options.
/// </summary>
public static class JellyseerrJsonSerializer
{
    /// <summary>
    /// Default JSON serializer options for Jellyseerr.
    /// </summary>
    public static readonly JsonSerializerOptions DefaultOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = null, // Use JsonPropertyName attributes instead of naming policy
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault, // ignores empty base properties
        Converters = { new JellyseerrStringConverter() }
    };

    /// <summary>
    /// Deserializes JSON to the specified type using default JsonSerializer.
    /// </summary>
    /// <typeparam name="T">The type to deserialize to.</typeparam>
    /// <param name="json">The JSON string to deserialize.</param>
    /// <returns>The deserialized object.</returns>
    public static T? Deserialize<T>(string json)
    {
        return JsonSerializer.Deserialize<T>(json);
    }

    /// <summary>
    /// Serializes the specified object to JSON using Jellyseerr default options.
    /// </summary>
    /// <typeparam name="T">The type of the object to serialize.</typeparam>
    /// <param name="value">The object to serialize.</param>
    /// <returns>The JSON string.</returns>
    public static string Serialize<T>(T value)
    {
        return JsonSerializer.Serialize(value, DefaultOptions);
    }

    /// <summary>
    /// Serializes the specified object to JSON using Jellyseerr default options.
    /// </summary>
    /// <typeparam name="T">The type of the object to serialize.</typeparam>
    /// <param name="value">The object to serialize.</param>
    /// <param name="options">Additional options to merge with default options.</param>
    /// <returns>The JSON string.</returns>
    public static string Serialize<T>(T value, JsonSerializerOptions options)
    {
        var mergedOptions = new JsonSerializerOptions(DefaultOptions);
        foreach (var converter in options.Converters)
        {
            mergedOptions.Converters.Add(converter);
        }
        return JsonSerializer.Serialize(value, mergedOptions);
    }
}
