using System.Text.Json;
using System.Text.Json.Serialization;
using SportMeet.Domain.Enums;

namespace SportMeet.Api.Serialization;

/// <summary>
/// Writes <see cref="JoinRequestStatus"/> by its PascalCase name — "Pending" /
/// "Approved" / "Rejected" — which is the form the client's
/// <c>JoinRequestStatus</c> union in frontend/src/types/events.ts compares against.
///
/// Without this converter System.Text.Json emitted the numeric ordinal, so
/// <c>GET /api/events/{id}/join-requests</c> returned <c>"status": 0</c> while
/// still answering 200. Every frontend comparison is against the name
/// (<c>request.status === "Pending"</c>), so the equality was always false: the
/// request row rendered (its name, avatar and timestamp are ordinary strings) and
/// the pending badge rendered (<c>pendingCount</c> is an int on both sides), but
/// the Approve/Reject buttons — gated on exactly that comparison in
/// join-requests-panel.tsx — fell through to the decided-state badge and vanished.
/// The same ordinal made <c>myJoinRequestStatus</c> unreadable, so the
/// "Request submitted" state never appeared on the detail page or a browse card.
///
/// Registered explicitly rather than through <c>JsonStringEnumConverter</c> for the
/// reason <see cref="UserRoleJsonConverter"/> documents: the unknown-value
/// behaviour stays ours rather than a framework default a servicing release may
/// change. Both the nullable and non-nullable forms are registered because STJ
/// matches a converter on the property's exact type — <c>JoinRequestDto.Status</c>
/// is bare and <c>EventDetailDto.MyJoinRequestStatus</c> is nullable, and a
/// converter declared for only one of the two leaves the other on the ordinal.
///
/// This is the wire form only. Persistence is unaffected: the column keeps its
/// own <c>HasConversion&lt;string&gt;()</c> and CHECK constraint, both of which
/// already store these same PascalCase names.
///
/// Reading accepts a name and rejects a bare ordinal, matching the other
/// converters; no client sends a status today (approve/reject are verbs), but a
/// numeric read would let a caller write a status the client cannot render.
/// </summary>
public sealed class JoinRequestStatusJsonConverter : JsonConverter<JoinRequestStatus>
{
    public override JoinRequestStatus Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => ReadNullable(ref reader)
            ?? throw new JsonException("Pick pending, approved or rejected");

    public override void Write(Utf8JsonWriter writer, JoinRequestStatus value, JsonSerializerOptions options)
        => writer.WriteStringValue(value.ToString());

    /// <summary>Shared with the nullable converter. A non-nullable property cannot
    /// express "unreadable", so the caller decides whether that is an error.</summary>
    internal static JoinRequestStatus? ReadNullable(ref Utf8JsonReader reader)
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

        // Complete-name match, case-insensitive, so "0" and "PendingX" are both
        // rejected rather than becoming a status that later serialises as a name
        // nobody recognises.
        return Enum.TryParse<JoinRequestStatus>(text, ignoreCase: true, out var status)
               && string.Equals(text, status.ToString(), StringComparison.OrdinalIgnoreCase)
            ? status
            : null;
    }
}

/// <summary>Same wire form, null flowing through. This is the converter
/// <c>EventDetailDto.MyJoinRequestStatus</c> serialises through, and null is a real
/// state there — it means the viewer never asked, which is what renders the Join
/// button.</summary>
public sealed class NullableJoinRequestStatusJsonConverter : JsonConverter<JoinRequestStatus?>
{
    public override JoinRequestStatus? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => JoinRequestStatusJsonConverter.ReadNullable(ref reader);

    public override void Write(Utf8JsonWriter writer, JoinRequestStatus? value, JsonSerializerOptions options)
    {
        if (value is { } status)
        {
            writer.WriteStringValue(status.ToString());
            return;
        }

        writer.WriteNullValue();
    }
}