using System.Text.Json;
using System.Text.Json.Serialization;
using SportMeet.Domain.Enums;

namespace SportMeet.Api.Serialization;

/// <summary>
/// The single place SkillLevel text is parsed, shared by both converters below.
///
/// Returns null for anything that is not a real enum name, including a JSON
/// number: a raw ordinal is not part of the contract, and accepting one would let
/// a client guess its way to Advanced.
///
/// Parsing must not throw. An exception raised inside a converter surfaces as a 500
/// with no field errors, which the create form renders as a bare failure; null lets
/// the value reach CreateEventDtoValidator, which answers 422 + code "ValidationError"
/// + "Pick a skill level" - a shape the form handles.
///
/// Case-insensitive, but only on a complete name, so "99" and "BeginnerX" are both
/// rejected rather than becoming a level that later serialises as a name nobody
/// recognises.
/// </summary>
public static class SkillLevelParser
{
    public static SkillLevel? Parse(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return null;
        }

        return Enum.TryParse<SkillLevel>(text, ignoreCase: true, out var level)
               && string.Equals(text, level.ToString(), StringComparison.OrdinalIgnoreCase)
            ? level
            : null;
    }
}

/// <summary>
/// Writes SkillLevel by its PascalCase name - the exact form of the client's
/// SkillLevel union ("Beginner" | "Intermediate" | "Advanced") in
/// frontend/src/types/events.ts, which spec §12 says the client lower-cases only
/// for CSS class lookup.
///
/// Both the nullable and non-nullable converter are registered, because
/// System.Text.Json matches a converter on the property's exact type: one declared
/// only for SkillLevel? is skipped for a SkillLevel property, and the serializer
/// then silently falls back to the numeric ordinal. That is how the browse response
/// returned "skillLevel": 1 instead of "Beginner". Responses declare the
/// non-nullable type (an event always has a level); the create request declares the
/// nullable one so a bad value can be reported instead of thrown.
/// </summary>
public sealed class SkillLevelJsonConverter : JsonConverter<SkillLevel>
{
    public override SkillLevel Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => ReadNullable(ref reader)
            ?? throw new JsonException("Pick a skill level");

    public override void Write(Utf8JsonWriter writer, SkillLevel value, JsonSerializerOptions options)
        => writer.WriteStringValue(value.ToString());

    /// <summary>Shared with the nullable converter. A non-nullable property cannot
    /// express "unreadable", so the caller decides whether that is an error.</summary>
    internal static SkillLevel? ReadNullable(ref Utf8JsonReader reader)
    {
        if (reader.TokenType != JsonTokenType.String)
        {
            return null;
        }

        return SkillLevelParser.Parse(reader.GetString());
    }
}

/// <summary>
/// Same wire form, but null flows through so validation can reject it with a field
/// error. Only the request DTO needs this; see SkillLevelJsonConverter.
/// </summary>
public sealed class NullableSkillLevelJsonConverter : JsonConverter<SkillLevel?>
{
    public override SkillLevel? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => SkillLevelJsonConverter.ReadNullable(ref reader);

    public override void Write(Utf8JsonWriter writer, SkillLevel? value, JsonSerializerOptions options)
    {
        if (value is { } level)
        {
            writer.WriteStringValue(level.ToString());
            return;
        }

        writer.WriteNullValue();
    }
}

public static class JsonOptionsConfiguration
{
    /// <summary>Registered explicitly rather than via JsonStringEnumConverter so
    /// the unknown-value behaviour above is ours and not a framework default that
    /// a servicing release is free to change.</summary>
    public static void ConfigureEnumConverters(JsonSerializerOptions options)
    {
        options.Converters.Add(new SkillLevelJsonConverter());
        options.Converters.Add(new NullableSkillLevelJsonConverter());
    }
}
