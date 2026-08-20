using System.Text.Json;
using System.Text.Json.Serialization;

namespace CallAnalog.Softphone.Services;

/// <summary>
/// PBX API responses sometimes include null string fields; treat them as empty.
/// </summary>
public sealed class NullToEmptyStringJsonConverter : JsonConverter<string>
{
    public override string Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        return reader.TokenType switch
        {
            JsonTokenType.Null => string.Empty,
            JsonTokenType.String => reader.GetString() ?? string.Empty,
            JsonTokenType.Number => reader.TryGetInt64(out var n) ? n.ToString() : reader.GetDouble().ToString(),
            _ => throw new JsonException($"Cannot convert JSON token '{reader.TokenType}' to string.")
        };
    }

    public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value);
}

/// <summary>
/// PBX pagination/metadata fields may be null when a search returns no rows.
/// </summary>
public sealed class NullToDefaultJsonConverter<T> : JsonConverter<T> where T : struct
{
    public override T Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
        {
            return default;
        }

        if (typeof(T) == typeof(int) && reader.TryGetInt32(out var intValue))
        {
            return (T)(object)intValue;
        }

        if (typeof(T) == typeof(long) && reader.TryGetInt64(out var longValue))
        {
            return (T)(object)longValue;
        }

        throw new JsonException($"Cannot convert JSON token '{reader.TokenType}' to {typeof(T).Name}.");
    }

    public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options)
    {
        if (typeof(T) == typeof(int))
        {
            writer.WriteNumberValue(UnsafeAsInt(value));
            return;
        }

        if (typeof(T) == typeof(long))
        {
            writer.WriteNumberValue(UnsafeAsLong(value));
            return;
        }

        JsonSerializer.Serialize(writer, value, options);
    }

    private static int UnsafeAsInt(T value) => (int)(object)value!;

    private static long UnsafeAsLong(T value) => (long)(object)value!;
}
