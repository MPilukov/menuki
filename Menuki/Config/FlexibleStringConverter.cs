using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Menuki.Config;

/// <summary>
/// Reads a JSON string, number or boolean into a string. Lets an input's
/// <c>default</c> be written naturally - <c>"default": 100</c> or
/// <c>"default": true</c> - while the engine keeps treating it as text.
/// </summary>
public sealed class FlexibleStringConverter : JsonConverter<string?>
{
    public override string? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.String:
                return reader.GetString();
            case JsonTokenType.True:
                return "true";
            case JsonTokenType.False:
                return "false";
            case JsonTokenType.Null:
                return null;
            case JsonTokenType.Number:
                var d = reader.GetDouble();
                return d == Math.Floor(d) && !double.IsInfinity(d)
                    ? ((long)d).ToString(CultureInfo.InvariantCulture)
                    : d.ToString(CultureInfo.InvariantCulture);
            default:
                throw new JsonException($"Cannot convert {reader.TokenType} to string.");
        }
    }

    public override void Write(Utf8JsonWriter writer, string? value, JsonSerializerOptions options)
    {
        if (value is null)
            writer.WriteNullValue();
        else
            writer.WriteStringValue(value);
    }
}
