using System.Text.RegularExpressions;

namespace SportMeet.Application.Ingestion;

/// <summary>
/// A minimal, dependency-free parser for the iCalendar objects (RFC 5545) that
/// ride along with calendar-invite emails.
///
/// <b>Why this exists next to the LLM extractor rather than instead of it:</b> a
/// VEVENT states its facts in fields — SUMMARY is the title, DTSTART is the start,
/// LOCATION is the venue — so reading them is exact, while extracting the same
/// facts from prose is a guess the model has to get right. The pipeline therefore
/// tries here first and only falls back to text/LLM extraction when this returns
/// null (no attachment, no VEVENT, or too little to build a proposal from).
///
/// <b>Scope is deliberately the invite subset:</b> one VEVENT's properties, line
/// unfolding, and UTC/local date-times with optional TZID. RECURRENCE-ID, RDATE and
/// per-attendee overrides are out of scope — a recurring series still carries its
/// DTSTART, which is all a draft needs, and a reviewer confirms the rest.
///
/// Like every extractor, everything it cannot read goes to
/// <see cref="ExtractedEvent.MissingFields"/> rather than being guessed, and a
/// partial VEVENT still produces a draft: missing fields are review guidance,
/// never blockers.
/// </summary>
public static partial class IcsEventParser
{
    public const string IcsModelName = "ics";
    public const string IcsPromptVersion = "ics-1";

    /// <summary>Content lengths mirror the LLM prompt's schema bounds, so an
    /// ICS-sourced proposal and an LLM-sourced one are interchangeable
    /// downstream.</summary>
    private const int TitleMax = 120;
    private const int VenueMax = 160;
    private const int DescriptionMax = 900;

    private static readonly string[] DateFormats =
    [
        "yyyyMMddTHHmmssZ",
        "yyyyMMddTHHmmss",
        "yyyyMMdd",
    ];

    /// <summary>Parse the first VEVENT in an ICS document into a proposal.
    /// Returns null when there is no VEVENT or it carries neither a usable
    /// SUMMARY nor a parseable DTSTART — i.e. when it would tell a reviewer
    /// nothing the email body did not already say.</summary>
    public static ExtractedEvent? TryParse(string? icsData, DateTimeOffset receivedAt)
    {
        if (string.IsNullOrWhiteSpace(icsData)) return null;

        var properties = FirstVEvent(icsData);
        if (properties is null) return null;

        var title = CleanUnescaped(properties.GetValueOrDefault("SUMMARY"));
        if (title is { Length: < 3 }) title = null;

        var start = ParseIcsInstant(properties.GetValueOrDefault("DTSTART"));
        var end = ParseIcsInstant(properties.GetValueOrDefault("DTEND"));
        if (title is null && start is null) return null;

        var location = CleanUnescaped(properties.GetValueOrDefault("LOCATION"));
        if (location is { Length: > VenueMax }) location = location[..VenueMax];
        if (location is { Length: < 2 }) location = null;

        // ORGANIZER is stored into the description slot with a clear label: it is
        // genuinely useful context ("who is running this"), and the payload type
        // has no organizer field — inventing one would leak model chatter past
        // CreateEventDto, which is exactly what that type exists to prevent.
        var organizer = CleanUnescaped(properties.GetValueOrDefault("ORGANIZER"));
        var description = CleanUnescaped(properties.GetValueOrDefault("DESCRIPTION"));
        if (description is { Length: > DescriptionMax }) description = description[..DescriptionMax];
        if (!string.IsNullOrWhiteSpace(organizer))
        {
            var line = $"Organizer: {organizer}";
            description = string.IsNullOrWhiteSpace(description) ? line : $"{description}\n\n{line}";
        }
        if (description is { Length: > DescriptionMax })
            description = description[..DescriptionMax];

        var missing = new List<string>();
        if (title is null) missing.Add("title");
        if (start is null) missing.Add("startAt");
        if (end is null) missing.Add("endAt");
        if (location is null) missing.Add("venueName");
        missing.AddRange(["address", "latitude", "longitude", "maxParticipants", "cost", "skillLevel"]);

        var confidence = (title, start) switch
        {
            (not null, not null) => 0.95m,
            (not null, null) => 0.5m,
            (null, not null) => 0.45m,
            _ => 0.2m,
        };

        return new ExtractedEvent
        {
            Title = title is { Length: > TitleMax } ? title[..TitleMax] : title,
            StartAt = start,
            EndAt = end is not null && start is not null && end <= start ? null : end,
            // A DTSTART with a TZID param carries the zone; a UTC one does not
            // name one, and the service applies the configured default then.
            Timezone = ZoneOf(properties.GetValueOrDefault("DTSTART")),
            VenueName = location,
            // LOCATION mixes name and address in one line and there is no way to
            // tell them apart here; the reviewer splits them in the form.
            Address = null,
            Description = description,
            Confidence = confidence,
            MissingFields = missing,
            Reasoning = "Read from the message's calendar invite (ICS) rather than its prose.",
            PromptVersion = IcsPromptVersion,
            Model = IcsModelName,
        };
    }

    /// <summary>Flatten and split the first VEVENT block into its first-valued
    /// property map. Property names are upper-cased; unknown/nested blocks are
    /// skipped so a VALARM's SUMMARY cannot masquerade as the event's.</summary>
    private static Dictionary<string, string>? FirstVEvent(string ics)
    {
        var lines = Unfold(ics);
        Dictionary<string, string>? inside = null;
        var depth = 0;

        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (line.Length == 0) continue;

            var nameEnd = line.IndexOfAny([':', ';']);
            if (nameEnd <= 0) continue;
            var name = line[..nameEnd].ToUpperInvariant();

            if (name == "BEGIN" && line.EndsWith("VEVENT", StringComparison.OrdinalIgnoreCase))
            {
                if (inside is not null) continue; // Only the first VEVENT.
                inside = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                depth = 1;
                continue;
            }

            if (inside is null) continue;

            if (name == "BEGIN") { depth++; continue; }
            if (name == "END")
            {
                depth--;
                if (depth == 0) return inside;
                continue;
            }

            if (depth != 1) continue; // Nested component content is not the event's.

            var colon = line.IndexOf(':');
            if (colon <= 0) continue;
            var value = line[(colon + 1)..];
            if (!inside.ContainsKey(name)) inside[name] = value;
        }

        return inside;
    }

    /// <summary>RFC 5545 line unfolding: a line beginning with space or tab
    /// continues the previous one. Long LOCATION/DESCRIPTION values wrap, so
    /// without this a folded property silently loses its tail.</summary>
    private static IEnumerable<string> Unfold(string ics)
    {
        var current = new System.Text.StringBuilder();
        var started = false;

        foreach (var raw in ics.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
        {
            if (started && (raw.StartsWith(' ') || raw.StartsWith('\t')))
            {
                current.Append(raw.AsSpan(1));
                continue;
            }
            if (started) yield return current.ToString();
            current.Clear().Append(raw);
            started = true;
        }
        if (started) yield return current.ToString();
    }

    private static DateTimeOffset? ParseIcsInstant(string? propertyValue)
    {
        if (string.IsNullOrWhiteSpace(propertyValue)) return null;
        var value = propertyValue[(propertyValue.IndexOf(':') + 1)..].Trim();

        if (!DateTimeOffset.TryParseExact(
                value, DateFormats, System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out var parsed))
            return null;

        // No offset means floating/local time. The ICS spec says resolve it in the
        // TZID's zone; absent a TZID we read it as Melbourne wall clock — the same
        // convention HeuristicEventExtractor uses for prose times — so a local
        // "10:00" from a Melbourne club does not become 10am UTC.
        if (value.EndsWith('Z')) return parsed.ToUniversalTime();

        var zone = ZoneOf(propertyValue) ?? "Australia/Melbourne";
        try
        {
            var tz = TimeZoneInfo.FindSystemTimeZoneById(zone);
            var local = new DateTime(parsed.Year, parsed.Month, parsed.Day,
                parsed.Hour, parsed.Minute, parsed.Second);
            return new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(local, tz), TimeSpan.Zero);
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            return new DateTimeOffset(parsed.UtcDateTime, TimeSpan.Zero);
        }
    }

    /// <summary>The TZID parameter of a date-time property ("DTSTART;TZID=
    /// Australia/Melbourne:..."), or null for UTC/floating values.</summary>
    private static string? ZoneOf(string? propertyValue)
    {
        if (string.IsNullOrWhiteSpace(propertyValue)) return null;
        var match = TzId().Match(propertyValue);
        return match.Success ? match.Groups[1].Value : null;
    }

    [GeneratedRegex(@"TZID=([^:;]+)", RegexOptions.CultureInvariant)]
    private static partial Regex TzId();

    /// <summary>Trim, normalise ICS escapes (\, \; \n) to text, and collapse
    /// whitespace runs (HTML from a DESCRIPTION) down to single spaces/newlines.</summary>
    private static string? CleanUnescaped(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var text = value.Replace("\\n", "\n", StringComparison.Ordinal)
                        .Replace("\\N", "\n", StringComparison.Ordinal)
                        .Replace("\\,", ",", StringComparison.Ordinal)
                        .Replace("\\;", ";", StringComparison.Ordinal);
        text = Whitespace().Replace(text, m => m.Value.Contains('\n') ? "\n" : " ");
        text = text.Trim();
        return text.Length == 0 ? null : text;
    }

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)]
    private static partial Regex Whitespace();
}