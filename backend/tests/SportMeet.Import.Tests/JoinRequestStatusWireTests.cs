using System.Text.Json;
using SportMeet.Api.Serialization;
using SportMeet.Application.Events;
using SportMeet.Domain.Enums;

namespace SportMeet.Import.Tests;

/// <summary>
/// The wire form of <see cref="JoinRequestStatus"/>, asserted against real
/// serialized JSON rather than the C# value.
///
/// These exist because the ordinal form passed every other test in the suite.
/// <c>JoinRequestApprovalTests</c> asserts <c>JoinRequestStatus.Pending</c> and the
/// frontend declares <c>"Pending" | "Approved" | "Rejected"</c>; both are correct
/// and mutually consistent, and neither one looks at what leaves the API. The
/// numeric default satisfied them both while making
/// <c>request.status === "Pending"</c> permanently false in
/// join-requests-panel.tsx, so the Approve/Reject buttons never rendered and the
/// host had no way to answer a request. Only comparing the emitted token catches
/// that, which is what this file does.
///
/// Serialization goes through <see cref="JsonOptionsConfiguration"/> rather than a
/// locally built options instance, so a converter that was written but never
/// registered fails here instead of shipping.
/// </summary>
public static class JoinRequestStatusWireTests
{
    /// <summary>The MVC serializer's options: the enum converters plus the camelCase
    /// naming Program.cs applies. Built the same way Program.cs builds it, so a
    /// converter registered for only one of the two nullable forms is caught.</summary>
    private static JsonSerializerOptions MvcOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        };
        JsonOptionsConfiguration.ConfigureEnumConverters(options);
        return options;
    }

    private static JsonElement Serialize(JoinRequestStatus status)
        => JsonSerializer.SerializeToElement(
            new JoinRequestDto(
                new ParticipantDto(Guid.NewGuid(), "D P", null),
                status,
                new DateTimeOffset(2030, 5, 1, 9, 0, 0, TimeSpan.Zero),
                null),
            MvcOptions());

    /// <summary>An EventDetailDto shaped the way EventService builds one, so the
    /// nullable property is exercised in the company it actually travels in.</summary>
    private static JsonElement DetailJson(JoinRequestStatus? status)
        => JsonSerializer.SerializeToElement(
            new EventDetailDto
            {
                Id = Guid.NewGuid(),
                Title = "Tennis Saturday",
                Tags = Array.Empty<string>(),
                StartAt = new DateTimeOffset(2030, 5, 1, 10, 0, 0, TimeSpan.Zero),
                EndAt = new DateTimeOffset(2030, 5, 1, 12, 0, 0, TimeSpan.Zero),
                Timezone = "Australia/Melbourne",
                VenueName = "Court 3",
                Latitude = -37.8,
                Longitude = 144.9,
                MaxParticipants = 4,
                JoinedCount = 0,
                InterestedCount = 0,
                Status = EventStatus.Scheduled.ToString(),
                IsCancelled = false,
                IsPublished = true,
                Visibility = EventVisibility.Public.ToString(),
                RequiresJoinApproval = true,
                Host = new ParticipantDto(Guid.NewGuid(), "Demo Host", null),
                IsHost = true,
                IsJoined = false,
                IsInterested = false,
                Participants = Array.Empty<ParticipantDto>(),
                MyJoinRequestStatus = status,
            },
            MvcOptions());

    // ---- The three names on the wire ----------------------------------------

    [Theory]
    [InlineData(JoinRequestStatus.Pending, "Pending")]
    [InlineData(JoinRequestStatus.Approved, "Approved")]
    [InlineData(JoinRequestStatus.Rejected, "Rejected")]
    public static void JoinRequestDto_status_serializes_as_its_name(
        JoinRequestStatus status, string expected)
    {
        var json = Serialize(status);

        Assert.Equal(JsonValueKind.String, json.GetProperty("status").ValueKind);
        Assert.Equal(expected, json.GetProperty("status").GetString());
    }

    /// <summary>The regression in its shortest form. Before the converter this
    /// property was <c>0</c>, which the client reads as "not pending" and answers
    /// by hiding the host's Approve button.</summary>
    [Fact]
    public static void Pending_is_the_string_and_never_the_ordinal()
    {
        var json = Serialize(JoinRequestStatus.Pending);

        Assert.Equal("\"Pending\"", json.GetProperty("status").GetRawText());
    }

    /// <summary>The list envelope, since the panel reads <c>items</c> and
    /// <c>pendingCount</c> off it and the badge was the one thing that kept working
    /// — the int side of the mismatch is pinned so the fix cannot shift it.</summary>
    [Fact]
    public static void JoinRequestListDto_keeps_pending_count_a_number()
    {
        var json = JsonSerializer.SerializeToElement(
            new JoinRequestListDto(
                new[]
                {
                    new JoinRequestDto(
                        new ParticipantDto(Guid.NewGuid(), "D P", null),
                        JoinRequestStatus.Pending,
                        new DateTimeOffset(2030, 5, 1, 9, 0, 0, TimeSpan.Zero),
                        null),
                },
                1),
            MvcOptions());

        Assert.Equal(1, json.GetProperty("pendingCount").GetInt32());
        var item = json.GetProperty("items")[0];
        Assert.Equal("\"Pending\"", item.GetProperty("status").GetRawText());
    }

    // ---- Nothing else on the row may change shape --------------------------

    /// <summary>The row kept rendering while its buttons vanished precisely because
    /// every other field was already a type both sides agree on. Those fields are
    /// pinned so a fix here cannot quietly move the breakage next door.</summary>
    [Fact]
    public static void The_rest_of_the_row_keeps_its_wire_form()
    {
        var decided = new DateTimeOffset(2030, 5, 1, 9, 30, 0, TimeSpan.Zero);
        var json = JsonSerializer.SerializeToElement(
            new JoinRequestDto(
                new ParticipantDto(Guid.Empty, "D P", "https://img/dp.png"),
                JoinRequestStatus.Approved,
                new DateTimeOffset(2030, 5, 1, 9, 0, 0, TimeSpan.Zero),
                decided),
            MvcOptions());

        Assert.Equal("D P", json.GetProperty("requester").GetProperty("displayName").GetString());
        Assert.Equal("https://img/dp.png", json.GetProperty("requester").GetProperty("avatarUrl").GetString());
        Assert.Equal(JsonValueKind.String, json.GetProperty("requestedAt").ValueKind);
        Assert.Equal(decided, json.GetProperty("decidedAt").GetDateTimeOffset());
    }

    [Fact]
    public static void DecidedAt_stays_nullable_on_the_wire()
    {
        var json = Serialize(JoinRequestStatus.Pending);

        Assert.Equal(JsonValueKind.Null, json.GetProperty("decidedAt").ValueKind);
    }

    // ---- EventDetailDto.MyJoinRequestStatus --------------------------------

    /// <summary>The nullable property, which drives the "Request submitted" state on
    /// the detail page and the browse card. STJ matches a converter on the property's
    /// exact type, so this fails independently of the non-nullable test above if only
    /// one converter is registered.</summary>
    [Theory]
    [InlineData(JoinRequestStatus.Pending, "Pending")]
    [InlineData(JoinRequestStatus.Approved, "Approved")]
    [InlineData(JoinRequestStatus.Rejected, "Rejected")]
    public static void MyJoinRequestStatus_serializes_as_its_name(
        JoinRequestStatus status, string expected)
    {
        var json = DetailJson(status);

        Assert.Equal(JsonValueKind.String, json.GetProperty("myJoinRequestStatus").ValueKind);
        Assert.Equal(expected, json.GetProperty("myJoinRequestStatus").GetString());
    }

    /// <summary>Null is a real state and must stay null rather than becoming the
    /// ordinal 0 or the name "Pending" — that is the difference between offering the
    /// Join button and telling a member they already asked.</summary>
    [Fact]
    public static void MyJoinRequestStatus_absent_stays_null()
    {
        var json = DetailJson(null);

        Assert.Equal(JsonValueKind.Null, json.GetProperty("myJoinRequestStatus").ValueKind);
    }

    // ---- Reading ------------------------------------------------------------

    /// <summary>Approve and reject are verbs on the route, so no client sends a
    /// status today. Reading stays strict anyway: a bare ordinal is not the contract,
    /// and accepting one would let a caller store a status no client can render.</summary>
    [Theory]
    [InlineData("\"Pending\"", JoinRequestStatus.Pending)]
    [InlineData("\"Approved\"", JoinRequestStatus.Approved)]
    [InlineData("\"Rejected\"", JoinRequestStatus.Rejected)]
    public static void A_name_reads_back(string json, JoinRequestStatus expected)
    {
        Assert.Equal(expected, JsonSerializer.Deserialize<JoinRequestStatus>(json, MvcOptions()));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("1")]
    [InlineData("\"pending-ish\"")]
    [InlineData("\"\"")]
    public static void Anything_that_is_not_a_name_is_unreadable(string json)
    {
        // The nullable form reports it; the bare form refuses outright.
        Assert.Null(JsonSerializer.Deserialize<JoinRequestStatus?>(json, MvcOptions()));
        Assert.Throws<JsonException>(
            () => JsonSerializer.Deserialize<JoinRequestStatus>(json, MvcOptions()));
    }
}