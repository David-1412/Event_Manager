using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using SportMeet.Application.Ingestion;
using SportMeet.Domain.Enums;

namespace SportMeet.Infrastructure.Ingestion;

/// <summary>
/// The model's JSON, mapped onto <see cref="ExtractedEvent"/>.
///
/// Separate from <see cref="ExtractedEvent"/> because the wire shape needs its own
/// contract: every property optional and case-insensitive, since a field the model omits
/// is an ordinary event rather than a crash, and the extra <c>isEvent</c> discriminator
/// exists only to tell "not an invitation" apart from "an invitation I read badly".
///
/// <para><b>Nothing here is taken on trust, including <c>missingFields</c>.</b> The model
/// self-reports which fields it could not find, and self-reports are wrong in a specific
/// useful direction: a model that invents a venue usually also omits it from its own list
/// of gaps, because it believes it found one. So <see cref="MissingFields"/> is rebuilt as
/// <em>union</em> of the declared list and every field that came back null — which catches
/// the confident omission and preserves any gap the model named while returning a value
/// (an out-of-range capacity, an unrecognised zone), which a pure "nulls only" rule
/// would silently drop. <see cref="Confidence"/> is likewise stored as the model's estimate
/// and nothing keys off it.</para>
///
/// <para>Unmatched JSON properties are discarded by the serializer, so a
/// prompt-injected <c>hostId</c> or <c>status</c> has nowhere to land: the projection
/// exposes no such property, and the DTO cannot carry one. That is a structural guard, not
/// a filter that has to be remembered.</para>
///
/// <para>Public rather than internal so the extraction evaluator can score this mapping
/// directly. That is the point: an evaluator that re-implemented the parsing would measure
/// its own copy and drift from what production does.</para>
/// </summary>
public sealed record LlmExtractionResponse
{
    private static readonly JsonSerializerOptions MapOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    /// <summary>The fields a reviewer can act on, i.e. <c>CreateEventDto</c>'s. Used to
    /// rebuild <c>missingFields</c>, and deliberately excludes <c>latitude</c>/<c>longitude</c>:
    /// the extractor cannot geocode, so a coordinate gap is reported by the heuristic's own
    /// path and asking the model for coordinates would only invite it to invent them.</summary>
    private static readonly string[] ReviewableFields =
    [
        "title", "startAt", "endAt", "venueName", "address",
        "maxParticipants", "cost", "tags", "skillLevel", "description",
    ];

    [JsonPropertyName("isEvent")]
    public bool? IsEvent { get; init; }

    [JsonPropertyName("title")]
    public string? Title { get; init; }

    /// <summary>Raw string, not <see cref="DateTimeOffset"/>: the model's timestamp must
    /// survive being wrong so it can be reported as unparsable rather than turning the
    /// whole response into an exception and falling back for an otherwise good draft.</summary>
    [JsonPropertyName("startAt")]
    public string? StartAt { get; init; }

    [JsonPropertyName("endAt")]
    public string? EndAt { get; init; }

    [JsonPropertyName("timezone")]
    public string? Timezone { get; init; }

    [JsonPropertyName("venueName")]
    public string? VenueName { get; init; }

    [JsonPropertyName("address")]
    public string? Address { get; init; }

    [JsonPropertyName("maxParticipants")]
    public int? MaxParticipants { get; init; }

    [JsonPropertyName("cost")]
    public decimal? Cost { get; init; }

    [JsonPropertyName("tags")]
    public List<string>? Tags { get; init; }

    [JsonPropertyName("skillLevel")]
    public string? SkillLevel { get; init; }

    [JsonPropertyName("description")]
    public string? Description { get; init; }

    [JsonPropertyName("confidence")]
    public double? Confidence { get; init; }

    [JsonPropertyName("missingFields")]
    public List<string>? MissingFields { get; init; }

    [JsonPropertyName("reasoning")]
    public string? Reasoning { get; init; }

    /// <summary>Returns the parsed body, or <c>null</c> when the model said this is not an
    /// event. Throws on unparsable JSON, which the caller treats as a failure and answers
    /// with the heuristic.</summary>
    public static LlmExtractionResponse? Parse(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            throw new FormatException("Empty completion content");

        // A model's reply is not machine-emitted JSON: a raw newline inside address/description,
        // or a reply cut off mid-string, makes a strict parser reject text whose leading fields
        // are all correct (the reader dies at the bad byte with "reached end of data"). Repair
        // first so a good extraction is not discarded for a formatting defect, then parse.
        var candidate = LlmJson.Repair(json)
            ?? throw new FormatException(
                $"Completion content was not parsable JSON, even after repair. raw={Truncate(json, 4000)}");

        LlmExtractionResponse? parsed;
        try
        {
            parsed = JsonSerializer.Deserialize<LlmExtractionResponse>(candidate, MapOptions);
        }
        catch (JsonException ex)
        {
            // Embed the raw text so a diagnosis does not depend on a second log line: the
            // exception surfaces in the extractor's fallback warning verbatim.
            throw new FormatException(
                $"{ex.Message} | raw={Truncate(json, 4000)}", ex);
        }

        if (parsed is null)
            throw new FormatException("Completion content was not a JSON object");

        // Explicit false only. A response that omits isEvent has not declined, and treating
        // an absent discriminator as a rejection is how a whole inbox quietly produces
        // nothing while every log line stays green.
        return parsed.IsEvent == false ? null : parsed;
    }

    private static string Truncate(string value, int max)
        => value.Length <= max ? value : value[..max] + "…";

    /// <summary>Project onto the domain proposal, recording every field that had to be
    /// discarded on the way and why.
    ///
    /// The discarded values are reported as missing rather than coerced: an end before the
    /// start, a capacity of 500 or a zone the machine does not recognise are all cases where
    /// the model was confident and wrong, and the reviewer needs to see that the email said
    /// <em>something</em> about that field instead of an unexplained blank.</summary>
    public ExtractedEvent ToExtractedEvent(
        string promptVersion, string model, string rawJson, string? defaultTimezone = null)
    {
        var dropped = new List<string>();

        var zone = ResolveZone(Timezone, dropped) ?? ResolveZone(defaultTimezone, new List<string>());
        var start = AnchorInstant(ParseInstant(StartAt, "startAt", dropped), zone);
        var end = AnchorInstant(ParseInstant(EndAt, "endAt", dropped), zone);
        if (start is not null && end is not null && end <= start)
        {
            // Keep the start — it is the field the reviewer cannot reconstruct from
            // nothing — and report the end as the model's own contradiction.
            dropped.Add($"endAt ({EndAt}) is not after startAt");
            end = null;
        }

        // Local, not a reassignment of MaxParticipants: the property is init-only, and
        // mutating a parsed response mid-projection would leave the two disagreeing for
        // anything else that reads it.
        var capacity = MaxParticipants;
        if (capacity is { } cap && (cap < 2 || cap > 50))
        {
            dropped.Add($"maxParticipants ({cap}) outside 2-50");
            capacity = null;
        }

        var skill = ParseSkill(SkillLevel, dropped);

        var missing = BuildMissingFields(dropped);

        return new ExtractedEvent
        {
            Title = BlankToNull(Title),
            Tags = NormalizeTags(Tags),
            StartAt = start,
            EndAt = end,
            Timezone = zone,
            VenueName = BlankToNull(VenueName),
            Address = BlankToNull(Address),
            MaxParticipants = capacity,
            Cost = Cost,
            Description = BlankToNull(Description),
            SkillLevel = skill,
            Confidence = ToConfidence(Confidence),
            MissingFields = missing,
            Reasoning = BlankToNull(Reasoning),
            PromptVersion = promptVersion,
            Model = model,
            RawExtraction = rawJson,
        };
    }

    /// <summary>Declared gaps ∪ fields that arrived empty ∪ fields rejected by validation.
    ///
    /// Names are canonicalised to the <c>CreateEventDto</c> property names the review queue
    /// already keys on: the model is asked for those, but a response that says
    /// <c>start_at</c> or <c>StartTime</c> would otherwise render as a chip for a field that
    /// does not exist, which reads as a bug in the queue rather than in the extraction.
    /// Dropped-field notes keep their detail in parentheses and are left un-mapped —
    /// <c>StartsWith</c> still matches them.</summary>
    private List<string> BuildMissingFields(IReadOnlyList<string> dropped)
    {
        var declared = MissingFields ?? [];
        var empty = ReviewableFields.Where(f => IsEmpty(f));
        var all = declared
            .Concat(empty)
            .Concat(dropped)
            .Select(NormalizeFieldName)
            .Where(n => n.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();

        // 14 bounds the column's own cap and the schema's maxItems; a model that returns a
        // hundred "missing" fields is having a bad day and must not overflow the row.
        return all.Count > 14 ? all[..14] : all;
    }

    private bool IsEmpty(string field) => field switch
    {
        "title" => string.IsNullOrWhiteSpace(Title),
        "startAt" => string.IsNullOrWhiteSpace(StartAt),
        "endAt" => string.IsNullOrWhiteSpace(EndAt),
        "venueName" => string.IsNullOrWhiteSpace(VenueName),
        "address" => string.IsNullOrWhiteSpace(Address),
        "maxParticipants" => MaxParticipants is null,
        "cost" => Cost is null,
        "tags" => Tags is null or { Count: 0 },
        "skillLevel" => string.IsNullOrWhiteSpace(SkillLevel),
        "description" => string.IsNullOrWhiteSpace(Description),
        _ => true,
    };

    private static string NormalizeFieldName(string name)
    {
        var key = new string(name.Where(char.IsLetterOrDigit).ToArray());
        if (key.Length == 0) return string.Empty;

        foreach (var field in ReviewableFields)
            if (string.Equals(key, field, StringComparison.OrdinalIgnoreCase))
                return field;

        // Unknown, but keep it: a name we do not recognise is still a gap the model
        // reported, and discarding it hides information from the reviewer.
        return name.Trim().ToLowerInvariant();
    }

    /// <summary>ISO-8601, tolerant of a bare date ("2026-10-03") which the model returns
    /// whenever the email gave a day and no time.
    ///
    /// A date-only value is <em>not</em> promoted to midnight: a start of 00:00 would
    /// publish a social badminton night as starting at midnight and looks entirely valid in
    /// the queue, so nothing would catch it. It is reported as unparseable, which keeps the
    /// field missing and puts the real question — what time? — in front of the reviewer.</summary>
    private static DateTimeOffset? ParseInstant(string? value, string field, List<string> dropped)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;

        if (DateTimeOffset.TryParse(
                value, System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.RoundtripKind, out var parsed))
            return parsed;

        dropped.Add($"{field} ({value.Trim()}) could not be read as a date and time");
        return null;
    }

    /// <summary>
    /// Re-anchor an instant whose <em>wall-clock</em> reading is right but whose <em>offset</em>
    /// is not the event's zone.
    ///
    /// <para>The model is told to emit an RFC3339 instant with an offset, but a message that
    /// states only a local wall-clock time ("7:30 PM", no offset) leaves it no offset to derive,
    /// so it defaults to <c>+00:00</c>. The digits are the local time and the zone is the event's,
    /// so the instant is off by exactly that zone's offset — a 7:30pm Melbourne night arrives as
    /// <c>19:30+00:00</c>, which renders as 6:30am the next day once the UI shows it in Melbourne.
    /// Reading the local time back against the zone recovers the instant the message described.</para>
    ///
    /// <para>Only the <c>+00:00</c>/<c>Z</c> sentinel is treated as a placeholder. A genuine,
    /// non-zero offset the model supplies is trusted verbatim: it is either a real offset from the
    /// text or one the model derived, and re-deriving it from the zone would fight the message.
    /// A null zone (no stated or default zone) leaves the instant untouched.</para>
    /// </summary>
    private static DateTimeOffset? AnchorInstant(DateTimeOffset? instant, string? zoneId)
    {
        if (instant is not { } at || string.IsNullOrEmpty(zoneId)) return instant;
        if (at.Offset != TimeSpan.Zero) return at; // an explicit non-UTC offset is trusted as-is
        if (!TimeZoneInfo.TryFindSystemTimeZoneById(zoneId, out var zone)) return at;

        // The UTC instant carries the local wall-clock digits (offset was zero), so read them
        // back against the event's zone and convert to a true UTC instant.
        var local = new DateTime(at.Year, at.Month, at.Day, at.Hour, at.Minute, at.Second);
        return new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(local, zone), TimeSpan.Zero);
    }

    /// <summary>Validate rather than trust: an invented zone would shift every instant in
    /// the draft by hours while looking perfectly ordinary.</summary>
    private static string? ResolveZone(string? value, List<string> dropped)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        if (TimeZoneInfo.TryFindSystemTimeZoneById(value.Trim(), out var zone)) return zone.Id;

        dropped.Add($"timezone ({value.Trim()}) is not a known zone");
        return null;
    }

    private static SkillLevel? ParseSkill(string? value, List<string> dropped)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        if (Enum.TryParse<SkillLevel>(value.Trim(), ignoreCase: true, out var level)) return level;

        dropped.Add($"skillLevel ({value.Trim()}) is not a known level");
        return null;
    }

    /// <summary>Clamped, not rejected. A model reporting 1.2 for confidence is miscalibrated
    /// rather than lying, and the column's CHECK constraint would reject the row outright —
    /// which would lose the whole draft over a number that is advisory anyway.</summary>
    private static decimal ToConfidence(double? value) => value switch
    {
        null => 0m,
        < 0 => 0m,
        > 1 => 1m,
        _ => (decimal)value.Value,
    };

    /// <summary>Trim, drop blanks and hashtags, cap at the schema's six.
    ///
    /// Normalisation of casing and duplicates is <c>TagNormalizer</c>'s at approval; this
    /// only removes what cannot be a tag at all, so the extractor still reports what the
    /// email said rather than a server-side opinion about vocabulary.</summary>
    private static List<string>? NormalizeTags(List<string>? tags)
    {
        if (tags is null || tags.Count == 0) return null;

        var clean = tags
            .Select(t => t.Trim().TrimStart('#'))
            .Where(t => t.Length is > 0 and <= 24)
            .Take(6)
            .ToList();

        return clean.Count > 0 ? clean : null;
    }

    private static string? BlankToNull(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

