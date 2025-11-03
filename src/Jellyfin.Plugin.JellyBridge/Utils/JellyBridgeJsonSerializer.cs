using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using System.Linq;

namespace Jellyfin.Plugin.JellyBridge.Utils;

/// <summary>
/// Custom JSON converter that skips writing null, empty, or whitespace string values.
/// This prevents unnecessary properties from being written to JSON output.
/// </summary>
public class JellyBridgeStringConverter : JsonConverter<string>
{
    /// <summary>
    /// Writes a string value to JSON, skipping null, empty, or whitespace values.
    /// </summary>
    /// <param name="writer">The JSON writer.</param>
    /// <param name="value">The string value to write.</param>
    /// <param name="options">The JSON serializer options.</param>
    public override void Write(Utf8JsonWriter writer, string? value, JsonSerializerOptions options)
    {
        // Write null if string is null, empty, or whitespace
        if (string.IsNullOrWhiteSpace(value))
        {
            writer.WriteNullValue();
            return;
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
/// Custom JSON converter that skips writing empty lists/arrays.
/// This prevents unnecessary empty array properties from being written to JSON output.
/// </summary>
public class JellyBridgeListConverter : JsonConverter<List<object>>
{
    /// <summary>
    /// Writes a list value to JSON, skipping empty lists.
    /// </summary>
    /// <param name="writer">The JSON writer.</param>
    /// <param name="value">The list value to write.</param>
    /// <param name="options">The JSON serializer options.</param>
    public override void Write(Utf8JsonWriter writer, List<object>? value, JsonSerializerOptions options)
    {
        // Write null if list is null or empty
        if (value == null || value.Count == 0)
        {
            writer.WriteNullValue();
            return;
        }

        writer.WriteStartArray();
        foreach (var item in value)
        {
            JsonSerializer.Serialize(writer, item, options);
        }
        writer.WriteEndArray();
    }

    /// <summary>
    /// Reads a list value from JSON.
    /// </summary>
    /// <param name="reader">The JSON reader.</param>
    /// <param name="typeToConvert">The type to convert to.</param>
    /// <param name="options">The JSON serializer options.</param>
    /// <returns>The list value or null.</returns>
    public override List<object>? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        // When deserializing, just read the list normally
        return JsonSerializer.Deserialize<List<object>>(ref reader, options);
    }
}

public class JellyBridgePropertiesConverter<T> : JsonConverter<T> where T : class
{
    public override T? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        // Use default deserialization
        return JsonSerializer.Deserialize<T>(ref reader, options);
    }

    public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options)
    {
        if (value == null)
        {
            writer.WriteNullValue();
            return;
        }

        writer.WriteStartObject();

        // Use the actual runtime type of the object, not the generic type parameter T
        // This ensures we get all properties from the concrete class and its base classes,
        // even when T is an interface type. GetProperties automatically includes inherited properties.
        var actualType = value.GetType();
        var properties = actualType.GetProperties(BindingFlags.Public | BindingFlags.Instance);
        
        foreach (PropertyInfo prop in properties)
        {
            // Skip properties marked with JsonIgnore only at the current level (not inherited)
            if (prop.GetCustomAttribute<JsonIgnoreAttribute>(false) != null) continue;
            
            // Get the JsonPropertyName attribute if present
            var jsonNameAttr = prop.GetCustomAttribute<JsonPropertyNameAttribute>();
            string jsonName = jsonNameAttr?.Name ?? prop.Name;

            // Skip if the name contains an underscore
            if (jsonName.Contains("_")) continue;

            object? propValue = prop.GetValue(value);

            // Write the property using the default serializer
            writer.WritePropertyName(jsonName);
            JsonSerializer.Serialize(writer, propValue, prop.PropertyType, options);
        }

        writer.WriteEndObject();
    }
}

/// <summary>
/// Static class providing Jellyseerr-specific JSON serialization methods with default options.
/// </summary>
public static class JellyBridgeJsonSerializer
{
    /// <summary>
    /// Default JSON serializer options for Jellyseerr.
    /// </summary>
    public static JsonSerializerOptions DefaultOptions<T>() where T : class
    {
        return new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = null, // Use JsonPropertyName attributes instead of naming policy
            PropertyNameCaseInsensitive = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault, // ignores empty base properties
            Converters = { new JellyBridgePropertiesConverter<T>() }
        };
    }


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
    public static string Serialize<T>(T value) where T : class
    {
        return JsonSerializer.Serialize(value, DefaultOptions<T>());
    }

    /// <summary>
    /// Serializes the specified object to JSON using Jellyseerr default options.
    /// </summary>
    /// <typeparam name="T">The type of the object to serialize.</typeparam>
    /// <param name="value">The object to serialize.</param>
    /// <param name="options">Additional options to merge with default options.</param>
    /// <returns>The JSON string.</returns>
    public static string Serialize<T>(T value, JsonSerializerOptions options) where T : class
    {
        var mergedOptions = new JsonSerializerOptions(DefaultOptions<T>());
        foreach (var converter in options.Converters)
        {
            mergedOptions.Converters.Add(converter);
        }
        return JsonSerializer.Serialize(value, mergedOptions);
    }
}
