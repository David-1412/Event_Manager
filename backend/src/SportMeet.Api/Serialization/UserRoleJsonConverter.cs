using System.Text.Json;
using System.Text.Json.Serialization;
using SportMeet.Domain.Enums;

namespace SportMeet.Api.Serialization;

/// <summary>
/// Writes <see cref="UserRole"/> by its PascalCase name — "Member" / "Creator" / "Admin" — which
/// is the form the client's <c>UserRole</c> union and the role badges expect.
///
/// Registered explicitly rather than through <c>JsonStringEnumConverter</c> for the
/// same reason <see cref="SkillLevelJsonConverter"/> documents: without a converter
/// System.Text.Json emits the numeric ordinal, so the admin user table received
/// <c>"role": 1</c> and every badge would have rendered the wrong role while still
/// returning 200. Both the nullable and non-nullable forms are registered because
/// STJ matches a converter on the property's exact type.
///
/// Reading is deliberately strict: a raw ordinal is not part of the contract, and
/// accepting <c>1</c> would let a client grant itself Admin by guessing a number. An
/// unreadable value becomes null so the caller reports it rather than defaulting to
/// whichever role happens to be ordinal 0.
/// </summary>
public sealed class UserRoleJsonConverter : JsonConverter<UserRole>
{
    public override UserRole Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => ReadNullable(ref reader)
            ?? throw new JsonException("Pick a role");

    public override void Write(Utf8JsonWriter writer, UserRole value, JsonSerializerOptions options)
        => writer.WriteStringValue(value.ToString());

    /// <summary>Shared with the nullable converter. A non-nullable property cannot
    /// express "unreadable", so the caller decides whether that is an error.</summary>
    internal static UserRole? ReadNullable(ref Utf8JsonReader reader)
    {
        if (reader.TokenType != JsonTokenType.String)
        {
            return null;
        }

        var text = reader.GetString();
        if (string.IsNullOrEmpty(text))
        {
            return null;
        }

        // Complete-name match, case-insensitive, so "99" and "AdminX" are both
        // rejected rather than becoming a role that later serialises as a name
        // nobody recognises.
        return Enum.TryParse<UserRole>(text, ignoreCase: true, out var role)
               && string.Equals(text, role.ToString(), StringComparison.OrdinalIgnoreCase)
            ? role
            : null;
    }
}

/// <summary>Same wire form, but null flows through so the caller can report a bad
/// value instead of the converter throwing. This is the form
/// <c>ChangeRoleRequest.Role</c> binds through, so an unknown role reaches
/// UserAdminService as null and is refused with a sentence, never defaulted.</summary>
public sealed class NullableUserRoleJsonConverter : JsonConverter<UserRole?>
{
    public override UserRole? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => UserRoleJsonConverter.ReadNullable(ref reader);

    public override void Write(Utf8JsonWriter writer, UserRole? value, JsonSerializerOptions options)
    {
        if (value is { } role)
        {
            writer.WriteStringValue(role.ToString());
            return;
        }

        writer.WriteNullValue();
    }
}
