using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace RingInfo.Core.Api;

/// <summary>
/// 整数の項目を寛容に読み取る（56.0 のような小数や "56" のような文字列も受け付ける）。
/// API の仕様では整数でも、小数で返ってくると種類ごとのデータ全体が読めなくなるため。
/// </summary>
internal sealed class LenientInt32Converter : JsonConverter<int>
{
    public override int Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => LenientNumber.Read(ref reader) ?? throw new JsonException("整数の項目に null が指定されています。");

    public override void Write(Utf8JsonWriter writer, int value, JsonSerializerOptions options) => writer.WriteNumberValue(value);
}

/// <summary>null を許す整数の項目を寛容に読み取る</summary>
internal sealed class LenientNullableInt32Converter : JsonConverter<int?>
{
    public override bool HandleNull => true;

    public override int? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => LenientNumber.Read(ref reader);

    public override void Write(Utf8JsonWriter writer, int? value, JsonSerializerOptions options)
    {
        if (value is { } v)
        {
            writer.WriteNumberValue(v);
        }
        else
        {
            writer.WriteNullValue();
        }
    }
}

internal static class LenientNumber
{
    public static int? Read(ref Utf8JsonReader reader)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.Null:
                return null;
            case JsonTokenType.Number:
                if (reader.TryGetInt32(out var integer))
                {
                    return integer;
                }

                return ToInt(reader.GetDouble());
            case JsonTokenType.String:
                var text = reader.GetString();
                if (string.IsNullOrWhiteSpace(text))
                {
                    return null;
                }

                if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
                {
                    return ToInt(parsed);
                }

                throw new JsonException($"整数として読み取れない値です: {text}");
            default:
                throw new JsonException($"整数として読み取れない値です（{reader.TokenType}）。");
        }
    }

    private static int ToInt(double value)
    {
        if (double.IsNaN(value) || value is > int.MaxValue or < int.MinValue)
        {
            throw new JsonException($"整数の範囲外の値です: {value}");
        }

        return (int)Math.Round(value, MidpointRounding.AwayFromZero);
    }
}
