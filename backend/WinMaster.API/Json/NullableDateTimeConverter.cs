using System.Text.Json;
using System.Text.Json.Serialization;

namespace WinMaster.API.Json;

/// <summary>
/// Tolerant converter for <see cref="DateTime"/>? — treats empty or whitespace strings as null.
/// Browsers send empty datetime-local inputs as "" which otherwise fails binding to a nullable DateTime.
/// </summary>
public sealed class NullableDateTimeConverter : JsonConverter<DateTime?>
{
    public override DateTime? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null) return null;
        if (reader.TokenType == JsonTokenType.String)
        {
            var s = reader.GetString();
            if (string.IsNullOrWhiteSpace(s)) return null;
            if (DateTime.TryParse(s, null, System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal, out var dt))
                return dt;
            throw new JsonException($"Could not parse '{s}' as a date/time.");
        }
        // Fall back to the default handling for any other token type.
        return reader.GetDateTime();
    }

    public override void Write(Utf8JsonWriter writer, DateTime? value, JsonSerializerOptions options)
    {
        if (value.HasValue) writer.WriteStringValue(value.Value);
        else writer.WriteNullValue();
    }
}
