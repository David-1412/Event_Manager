using System.Text.Json;
using System.Text.Json.Serialization;
using SportMeet.Domain.Enums;

namespace SportMeet.Application.Common;

/// <summary>
/// EventVisibility on the wire: its PascalCase name in and out.
///
/// This is why the converter exists at all. System.Text.Json handles an enum by
/// its numeric ordinal unless a converter says otherwise, so the create form's
/// `"visibility":"Public"` could not bind to `CreateEventDto.Visibility`
/// (`EventVisibility?`) and the request died with "The JSON value could not be
/// converted to Nullable`1[EventVisibility]" before validation ran. SkillLevel
/// escapes this only because it has its own converter; EventVisibility needs the
/// same, in every JsonSerializerOptions that reads a CreateEventDto - both the
/// MVC options (Api) and EventDraftService's stored-payload reader.
///
/// An unrecognised value - and any JSON number, which is not part of the contract
/// - reads as null rather than throwing, so it reaches CreateEventDtoValidator and
/// answers 422 + "Pick public or private" instead of a bare 500. Null is legal
/// (it means Public); only a present-but-bad value is the error.
///
/// Both the nullable and non-nullable converters are registered because STJ
/// matches a converter on the property's exact type: one declared only for
/// EventVisibility? is skipped for an EventVisibility property, which then falls
/// back to the ordinal.
/// </summary>
public static class EventVisibilityParser
{
    public static EventVisibility? Parse(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return null;
        }

        return Enum.TryParse<EventVisibility>(text, ignoreCase: true, out var visibility)
               && string.Equals(text, visibility.ToString(), StringComparison.OrdinalIgnoreCase)
            ? visibility
            : null;
    }
}

public sealed class EventVisibilityJsonConverter : JsonConverter<EventVisibility>
{
    public override EventVisibility Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => ReadNullable(ref reader)
            ?? throw new JsonException("Pick public or private");

    public override void Write(Utf8JsonWriter writer, EventVisibility value, JsonSerializerOptions options)
        => writer.WriteStringValue(value.ToString());

    /// <summary>Shared with the nullable converter. A non-nullable property cannot
    /// express "unreadable", so the caller decides whether that is an error.</summary>
    internal static EventVisibility? ReadNullable(ref Utf8JsonReader reader)
        => reader.TokenType == JsonTokenType.String
            ? EventVisibilityParser.Parse(reader.GetString())
            : null;
}

/// <summary>
/// Same wire form, null flowing through so a bad value reaches validation. This is
/// the converter the create request binds through, since CreateEventDto.Visibility
/// is declared `EventVisibility?`.
/// </summary>
public sealed class NullableEventVisibilityJsonConverter : JsonConverter<EventVisibility?>
{
    public override EventVisibility? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => EventVisibilityJsonConverter.ReadNullable(ref reader);

    public override void Write(Utf8JsonWriter writer, EventVisibility? value, JsonSerializerOptions options)
    {
        if (value is { } visibility)
        {
            writer.WriteStringValue(visibility.ToString());
            return;
        }

        writer.WriteNullValue();
    }
}
