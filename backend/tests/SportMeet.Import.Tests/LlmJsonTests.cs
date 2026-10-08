using System.Text.Json;
using SportMeet.Infrastructure.Ingestion;

namespace SportMeet.Import.Tests;

/// <summary>
/// Regression tests for the failure where Claude returned HTTP 200 with a body the strict
/// parser rejected: "Expected end of string, but instead reached end of data" at
/// <c>$.address</c>. Every leading field (title, dates, venue, address) had been extracted;
/// the reader died on a raw control byte inside the address string, and the whole good
/// response was discarded in favour of the heuristic.
/// </summary>
public class LlmJsonTests
{
    // The real shape: a literal newline the model put inside "address". Raw control bytes are
    // illegal inside a JSON string, so System.Text.Json aborts at that byte.
    private const string AddressWithRawNewline =
        "{\"isEvent\":true,\"title\":\"Social badminton\",\"startAt\":\"2026-11-14T19:30:00+13:00\"," +
        "\"endAt\":\"2026-11-14T21:30:00+13:00\",\"venueName\":\"Seddon Park\"," +
        "\"address\":\"42 Railway Ave,\nSeddon, Wellington\",\"maxParticipants\":12," +
        "\"tags\":[\"badminton\"],\"confidence\":0.9,\"missingFields\":[]}";

    // The same object cut off mid-address - a token limit or a dropped stream. Everything
    // before the cut is real; the tail is simply absent.
    private const string TruncatedMidAddress =
        "{\"isEvent\":true,\"title\":\"Social badminton\",\"startAt\":\"2026-11-14T19:30:00+13:00\"," +
        "\"endAt\":\"2026-11-14T21:30:00+13:00\",\"venueName\":\"Seddon Park\"," +
        "\"address\":\"42 Railway Ave, Seddon, We";

    [Fact]
    public void The_failing_payload_reproduces_the_json_exception_before_repair()
    {
        // Pin the exact symptom the logs reported, so a future change to the payload shape is
        // caught here rather than silently passing the repair for the wrong reason.
        var ex = Assert.Throws<JsonException>(
            () => JsonSerializer.Deserialize<LlmExtractionResponse>(AddressWithRawNewline));
        Assert.Equal("$.address", ex.Path);
    }

    [Fact]
    public void Repair_escapes_a_raw_newline_inside_a_string_without_losing_the_value()
    {
        var repaired = LlmJson.Repair(AddressWithRawNewline);

        Assert.NotNull(repaired);
        Assert.True(LlmJson.IsValid(repaired!));

        var parsed = LlmExtractionResponse.Parse(AddressWithRawNewline);
        Assert.NotNull(parsed);
        Assert.Equal("Social badminton", parsed!.Title);
        Assert.Equal("Seddon Park", parsed.VenueName);
        // The newline survives as a real newline in the value, not as a dropped byte.
        Assert.Equal("42 Railway Ave,\nSeddon, Wellington", parsed.Address);
    }

    [Fact]
    public void Parse_recovers_the_full_object_from_the_failing_payload()
    {
        var parsed = LlmExtractionResponse.Parse(AddressWithRawNewline);

        Assert.NotNull(parsed);
        Assert.Equal("Social badminton", parsed!.Title);
        Assert.Equal("2026-11-14T19:30:00+13:00", parsed.StartAt);
        Assert.Equal("2026-11-14T21:30:00+13:00", parsed.EndAt);
        Assert.Equal("Seddon Park", parsed.VenueName);
        Assert.Equal(12, parsed.MaxParticipants);
        Assert.Equal(0.9, parsed.Confidence);
    }

    [Fact]
    public void Repair_salvages_a_response_truncated_mid_string()
    {
        var parsed = LlmExtractionResponse.Parse(TruncatedMidAddress);

        Assert.NotNull(parsed);
        Assert.Equal("Social badminton", parsed!.Title);
        Assert.Equal("Seddon Park", parsed.VenueName);
        // The cut value is preserved up to the truncation point; nothing after it is invented.
        Assert.Equal("42 Railway Ave, Seddon, We", parsed.Address);
        Assert.Null(parsed.MaxParticipants);
        Assert.Null(parsed.Tags);
        // The salvage keeps every field that arrived intact and fabricates none of the tail
        // that never came through.
        var proposal = parsed.ToExtractedEvent("v1", "claude-sonnet-5-5", TruncatedMidAddress);
        Assert.Equal("Social badminton", proposal.Title);
        Assert.Equal("Seddon Park", proposal.VenueName);
        Assert.Null(proposal.MaxParticipants);
    }

    [Fact]
    public void Repair_drops_a_dangling_key_with_no_value_after_truncation()
    {
        // Cut right after "confidence": with no value - the trailing member is a fragment with
        // nothing to assign, so it is dropped rather than left to break the parse.
        const string dangling =
            "{\"isEvent\":true,\"title\":\"Social badminton\",\"venueName\":\"Seddon Park\",\"confidence\":";

        var parsed = LlmExtractionResponse.Parse(dangling);

        Assert.NotNull(parsed);
        Assert.Equal("Social badminton", parsed!.Title);
        Assert.Equal("Seddon Park", parsed.VenueName);
        Assert.Null(parsed.Confidence);
    }

    [Fact]
    public void Repair_recovers_a_response_truncated_mid_property_name()
    {
        // The exact shape the log reported: every real field emitted, then cut inside the final
        // "confidence" property *name* (no colon, no value). The incomplete member is dropped
        // and the complete object kept - a good extraction is not discarded for a tight
        // max_tokens.
        const string truncatedAtKey =
            "{\"isEvent\":true,\"title\":\"Friday Badminton Night\"," +
            "\"venueName\":\"Seddon Park Badminton Centre\",\"address\":\"42 Railway Ave, Seddon VIC 3011\"," +
            "\"maxParticipants\":12,\"cost\":5,\"tags\":[\"badminton\"],\"skillLevel\":\"Beginner\",\"confidence";

        var repaired = LlmJson.Repair(truncatedAtKey);

        Assert.NotNull(repaired);
        Assert.True(LlmJson.IsValid(repaired!));
        // The dangling key fragment is gone, the completed fields survive.
        Assert.DoesNotContain("confidence", repaired!);

        var parsed = LlmExtractionResponse.Parse(truncatedAtKey);
        Assert.NotNull(parsed);
        Assert.Equal("Friday Badminton Night", parsed!.Title);
        Assert.Equal("Seddon Park Badminton Centre", parsed.VenueName);
        Assert.Equal("42 Railway Ave, Seddon VIC 3011", parsed.Address);
        Assert.Equal(12, parsed.MaxParticipants);
        Assert.Equal(5m, parsed.Cost);
        Assert.Equal("Beginner", parsed.SkillLevel);
        Assert.Null(parsed.Confidence);
    }

    [Fact]
    public void Repair_recovers_a_response_truncated_mid_number_value()
    {
        // Cut inside a number (no delimiter of its own). The partial value is not trusted -
        // "1" of "12" would silently change a capacity - so the incomplete member is dropped.
        const string truncatedAtNumber =
            "{\"isEvent\":true,\"title\":\"Run\",\"venueName\":\"Park\",\"maxParticipants\":1";

        var parsed = LlmExtractionResponse.Parse(truncatedAtNumber);

        Assert.NotNull(parsed);
        Assert.Equal("Run", parsed!.Title);
        Assert.Equal("Park", parsed.VenueName);
        Assert.Null(parsed.MaxParticipants);
    }

    [Fact]
    public void Valid_json_is_returned_byte_identical_so_repair_never_perturbs_a_good_reply()
    {
        const string good = "{\"isEvent\":true,\"title\":\"Run\",\"address\":\"1 A St, Wellington\"}";
        Assert.Equal(good, LlmJson.Repair(good));
    }

    [Fact]
    public void A_response_with_no_salvageable_object_is_reported_unparsable_with_the_raw_body()
    {
        const string garbage = "not json at all {{{";

        var ex = Assert.Throws<FormatException>(() => LlmExtractionResponse.Parse(garbage));
        Assert.Contains("raw=", ex.Message);
        Assert.Contains("not json at all", ex.Message);
    }

    [Fact]
    public void A_code_fence_is_stripped_before_parsing()
    {
        const string fenced = "```json\n{\"isEvent\":true,\"title\":\"Run\"}\n```";
        var stripped = LlmJson.StripCodeFence(fenced);

        var parsed = LlmExtractionResponse.Parse(stripped);
        Assert.Equal("Run", parsed!.Title);
    }

    // --- local-time offset normalisation (the 7:30pm -> 6:30am next-day defect) ---

    // The exact shape Claude returned: a Melbourne evening stated as a local wall-clock time
    // but stamped with a +00:00 offset, because the message gave no offset to derive.
    private const string LocalTimeStampedAsUtc =
        "{\"isEvent\":true,\"title\":\"Friday Badminton Night\"," +
        "\"startAt\":\"2026-11-13T19:30:00+00:00\",\"endAt\":\"2026-11-13T22:00:00+00:00\"," +
        "\"timezone\":\"Australia/Melbourne\",\"venueName\":\"Seddon Park Badminton Centre\"," +
        "\"address\":\"42 Railway Ave, Seddon VIC 3011\",\"confidence\":0.95,\"missingFields\":[]}";

    [Fact]
    public void A_local_time_stamped_as_utc_is_re_anchored_to_the_stated_zone()
    {
        var parsed = LlmExtractionResponse.Parse(LocalTimeStampedAsUtc);
        var proposal = parsed!.ToExtractedEvent("v1", "claude-sonnet-5-5", LocalTimeStampedAsUtc);

        // 7:30pm on 13 Nov 2026 is AEDT (UTC+11): the instant must be 08:30Z, not 19:30Z.
        Assert.Equal(
            new DateTimeOffset(2026, 11, 13, 8, 30, 0, TimeSpan.Zero),
            proposal.StartAt!.Value.ToUniversalTime());
        Assert.Equal(
            new DateTimeOffset(2026, 11, 13, 11, 0, 0, TimeSpan.Zero),
            proposal.EndAt!.Value.ToUniversalTime());

        // Rendered back in Melbourne it is the wall-clock time the message actually said,
        // on the date the message actually said - not 6:30am the following day.
        var melbourne = TimeZoneInfo.FindSystemTimeZoneById("Australia/Melbourne");
        var start = TimeZoneInfo.ConvertTime(proposal.StartAt!.Value, melbourne);
        Assert.Equal(13, start.Day);
        Assert.Equal(19, start.Hour);
        Assert.Equal(30, start.Minute);
    }

    [Fact]
    public void An_explicit_non_utc_offset_is_never_rewritten()
    {
        // A real offset the model derived (Sydney AEDT +11) is trusted verbatim; re-deriving
        // it from a zone would fight the message.
        const string json =
            "{\"isEvent\":true,\"title\":\"Run\",\"startAt\":\"2026-11-13T19:30:00+11:00\"," +
            "\"timezone\":\"Australia/Melbourne\"}";
        var parsed = LlmExtractionResponse.Parse(json);
        var proposal = parsed!.ToExtractedEvent("v1", "m", json);

        Assert.Equal(
            new DateTimeOffset(2026, 11, 13, 8, 30, 0, TimeSpan.Zero),
            proposal.StartAt!.Value.ToUniversalTime());
    }

    [Fact]
    public void A_utc_instant_is_left_alone_when_no_zone_is_known()
    {
        // No stated zone and no configured default: nothing to re-anchor against, so the
        // instant passes through untouched rather than being shifted by a guessed zone.
        const string json = "{\"isEvent\":true,\"title\":\"Run\",\"startAt\":\"2026-11-13T19:30:00Z\"}";
        var parsed = LlmExtractionResponse.Parse(json);
        var proposal = parsed!.ToExtractedEvent("v1", "m", json);

        Assert.Equal(
            new DateTimeOffset(2026, 11, 13, 19, 30, 0, TimeSpan.Zero),
            proposal.StartAt!.Value.ToUniversalTime());
    }

    [Fact]
    public void The_configured_default_zone_anchors_a_local_time_when_the_message_states_none()
    {
        // Message gives no timezone field; the configured default supplies the zone to anchor against.
        const string json =
            "{\"isEvent\":true,\"title\":\"Run\",\"startAt\":\"2026-11-13T19:30:00+00:00\"}";
        var parsed = LlmExtractionResponse.Parse(json);
        var proposal = parsed!.ToExtractedEvent("v1", "m", json, "Australia/Melbourne");

        Assert.Equal(
            new DateTimeOffset(2026, 11, 13, 8, 30, 0, TimeSpan.Zero),
            proposal.StartAt!.Value.ToUniversalTime());
    }
}