using System.Buffers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace NSG.Mobile.Application.Pis.DTOs;

/// <summary>
/// Reads a JSON string or number into a string, keeping the digits exactly as sent (<c>172</c> becomes "172", <c>81.50</c> becomes
/// "81.50"; nothing is converted to a number and back). Used for the numeric-looking personnel columns that are stored as text,
/// so a client that still posts numbers keeps working. Writes plain strings.
/// </summary>
public sealed class PisTextJsonConverter : JsonConverter<string?>
{
    public override string? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => reader.TokenType switch
        {
            JsonTokenType.String => reader.GetString(),
            JsonTokenType.Number => Encoding.UTF8.GetString(reader.HasValueSequence ? reader.ValueSequence.ToArray() : reader.ValueSpan.ToArray()),
            JsonTokenType.Null => null,
            _ => throw new JsonException("Expected text or a number.")
        };

    public override void Write(Utf8JsonWriter writer, string? value, JsonSerializerOptions options)
    {
        if (value == null)
            writer.WriteNullValue();
        else
            writer.WriteStringValue(value);
    }
}
