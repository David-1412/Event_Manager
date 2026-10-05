using System.Text.RegularExpressions;
using SportMeet.Application.Ingestion;
using SportMeet.Domain.Entities;
using SportMeet.Domain.Enums;

namespace SportMeet.Infrastructure.Ingestion;

/// <summary>
/// A deterministic, dependency-free extractor. Registered by default so steps 1-2 of
/// the plan's sequence are verifiable today: the queue, the approve path and the
/// review UI can be exercised without an API key, and the OpenAI implementation
/// replaces it through one DI line when credentials exist.
///
/// It is a heuristic, not a parser, and is explicit about the difference: anything it
/// cannot read with confidence goes into <see cref="ExtractedEvent.MissingFields"/>
/// rather than being guessed. A draft with four missing fields a reviewer fills in is
/// a useful result; a draft with four plausible inventions nobody re-reads is the
/// failure mode this feature has to avoid.
///
/// Non-goals, deliberately: it does not read addresses, coordinates or timezones
/// (GeoSearch is still a stub), and it treats its input as
/// inert data - no code path here lets body text influence anything except which
/// field a matched value lands in.
/// </summary>
public sealed partial class HeuristicEventExtractor : IEventExtractor
{
    public string PromptVersion => "heuristic-2";
    public string Model => "heuristic";

    /// <summary>A body this short cannot contain an invitation worth a draft.</summary>
    private const int MinBodyChars = 40;

    /// <summary>Words that make a message worth a look. Deliberately wider than "is
    /// definitely an invite": a false positive costs one rejected draft, a false
    /// negative silently drops someone's event.
    ///
    /// Two bugs in the form this replaced, both invisible in a casual read:
    /// the trailing <c>\\b</c> rejected every inflected form - "playing", "meets", "running",
    /// "invites", "games", "sessions", "classes" - and the list itself omitted activity names,
    /// which live in <see cref="ActivityWords"/>. Together those meant "Badminton this Friday"
    /// matched no signal at all, and only a message that happened to contain a bare generic
    /// word ("run", "join", "social") reached extraction. The golden set caught it: two of six
    /// cases returned no_event for this reason and nothing else.
    ///
    /// A leading boundary is kept so "classmate", "playwright" and "eventually" do not
    /// qualify. This is a cheap pre-filter, so over-matching is the correct direction.</summary>
    [GeneratedRegex(@"\b(?:join|play|game|session|social|meet|kickoff|run|train|practice|class|event|invite|rsvp)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex InvitationSignal();

    /// <summary>"7:30pm", "19:00", "at 7".</summary>
    [GeneratedRegex(@"(?:at\s+|^|\s)(\d{1,2})(?::(\d{2}))?\s*(am|pm)?\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex TimeOfDay();

    [GeneratedRegex(@"\b(?:(?:Monday|Tuesday|Wednesday|Thursday|Friday|Saturday|Sunday)\s+)?\d{1,2}(?:st|nd|rd|th)?\s+(?:January|February|March|April|May|June|July|August|September|October|November|December|Jan|Feb|Mar|Apr|Jun|Jul|Aug|Sep|Sept|Oct|Nov|Dec)\s+\d{4}\b|\b(?:January|February|March|April|May|June|July|August|September|October|November|December|Jan|Feb|Mar|Apr|Jun|Jul|Aug|Sep|Sept|Oct|Nov|Dec)\s+\d{1,2}(?:st|nd|rd|th)?[,]?\s+\d{4}\b|\b\d{1,2}[/-]\d{1,2}[/-]\d{4}\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex AbsoluteDate();

    [GeneratedRegex(@"(?m)^\s{0,3}#{1,6}\s+(.+?)\s*#*\s*$", RegexOptions.CultureInvariant)]
    private static partial Regex MarkdownHeading();

    [GeneratedRegex(@"(?im)^\s*\*\*(?:title|event|name)\s*:\*\*\s*(.+?)\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Multiline)]
    private static partial Regex LabeledTitle();

    [GeneratedRegex(@"(?m)^\s*\*\*(.{3,})\*\*\s*$", RegexOptions.CultureInvariant)]
    private static partial Regex BoldTitle();

    [GeneratedRegex(@"(?im)^\s*(?:[-*]\s*)?(?:\*\*)?(?:where|venue|location)(?:\*\*)?\s*:\s*(?:\*\*)?(.+?)(?:\*\*)?\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Multiline)]
    private static partial Regex VenueLine();

    [GeneratedRegex(@"(?<=\d)(?:st|nd|rd|th)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex OrdinalSuffix();

    [GeneratedRegex(@"^(?:Monday|Tuesday|Wednesday|Thursday|Friday|Saturday|Sunday)\s+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex WeekdayPrefix();

    /// <summary>Day names plus the two relative words unambiguous without a calendar.
    /// "soon"/"next week" are absent on purpose: they resolve to several valid dates,
    /// which is a guess.
    ///
    /// <b>No trailing word boundary.</b> It was there, and it silently broke every weekday:
    /// "Friday" ends in "y", a word character, so "\b" after the captured "day" could never
    /// match (only the alternation's first branch ends in a word character, which is why
    /// "today"/"tomorrow" were unaffected and looked like proof the pattern worked). The
    /// consequence was that an invite for "Tuesday" resolved to the *current* date rather
    /// than the next Tuesday - a confidently wrong day, the one failure this class's
    /// docstring claims to design against, and it survived because no test used a weekday.
    ///
    /// Dropping the boundary admits a prefix match ("Tuesdays" still yields Tuesday), which
    /// is harmless and intended here. It does not need reordering to be safe: of the seven
    /// capture prefixes, only "frid" begins a longer weekday word, and it is also the longest
    /// form of its own word, so the leftmost-alternative rule cannot pick a shorter prefix.
    /// The same claim for the others would need "thur", "satu" and "sund" added to the guard.</summary>
    [GeneratedRegex(@"\b(mon|tues|wednes|thurs|frid|satur|sund)day|\btoday\b|\btomorrow\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex DayWord();

    /// <summary>The zone a wall-clock reading is taken in. This class needs it twice, to
    /// resolve a start and then to place its end on the correct calendar day, and both sites
    /// already had to agree - so it is one member rather than a string repeated in two
    /// methods. Not injected: the extractor has no options to inject, and the entity's own
    /// default is this same zone, with the service layer applying the configured value.</summary>
    private static readonly TimeZoneInfo WallClockZone =
        TimeZoneInfo.FindSystemTimeZoneById("Australia/Melbourne");

    /// <summary>The only duration form read. Inferring an end from "a couple of
    /// hours" is how an event silently ends at the wrong hour.</summary>
    [GeneratedRegex(@"(\d{1,2})(?::(\d{2}))?\s*(am|pm)?\s*(?:-|--|to|until|through|\u2012|\u2013|\u2014|\u2015)\s*(\d{1,2})(?::(\d{2}))?\s*(am|pm)?", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex TimeRange();

    [GeneratedRegex(@"\b(?:max(?:imum)?|up to|capped at|limit)\D{0,12}(\d{1,3})\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Capacity();

    /// <summary>The only money form read: an explicit per-head or per-person price.
    ///
    /// Deliberately absent, in order of how expensive each error would be: a bare "$10",
    /// which could be a gate fee, a per-person share, or the total an organiser collected; and
    /// any amount followed by a date, so the "$10 due by 20 Oct" of a committee's dues
    /// reminder cannot become a $10 event. Anything the phrasing leaves ambiguous is reported
    /// missing, the same rule the rest of the class follows.
    ///
    /// Requires whitespace before the amount, so the "$1000" inside "A$1000" cannot match.</summary>
    [GeneratedRegex(@"\$\s*(\d+(?:\.\d{1,2})?)\s+(?:a|per|each)\s+(?:head|person|player|pp|entry|ticket|game)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Cost();

    /// <summary>Explicit ability statements, matched longest-phrase-first so "advanced
    /// beginners" reports Advanced rather than Beginner — the order of the alternation is
    /// the precedence, and reading that phrase as beginner-only would turn away the
    /// intermediate players it is actually aimed at.
    ///
    /// Deliberately absent: "social", "casual", "fun" and "newcomers welcome" on their own.
    /// Those describe the tone of a game, not a minimum standard, and inferring a level from
    /// them is how a draft tells intermediate players a game is not for them.</summary>
    [GeneratedRegex(
        @"\b(?:advanced|competitive|intermediate|experienced|all\s+(?:abilities|levels|skill(?:s)?)|beginners?\s+welcome|beginner\s+friendly)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SkillPhrase();

    /// <summary>Activity words that become tags. A fixed list rather than any noun:
    /// "the" and "time" must not become tags. Not a lookup against the sports table -
    /// tags replaced that vocabulary, so there is no slug to resolve and no FK to
    /// satisfy, which is what removed the plan's sport-matching step entirely.</summary>
    private static readonly string[] ActivityWords =
    [
        "badminton", "basketball", "tennis", "netball", "footy", "soccer", "football",
        "rugby", "cricket", "volleyball", "pickleball", "baseball", "hockey", "futsal",
        "running", "run", "walk", "cycling", "swimming", "yoga", "pilates", "climbing",
        "table tennis", "squash", "padel", "darts", "bowling", "esports",
    ];

    /// <inheritdoc />
    /// <remarks>Delegates with "now" as the received date. That makes a relative date
    /// ("next Friday") resolve against when the extraction ran rather than when the mail
    /// arrived, which is correct for a live poll of unread messages and wrong for a replay;
    /// <see cref="ExtractAsync(string,string,string,DateTimeOffset,CancellationToken)"/> is the
    /// form that takes the arrival time, and the golden set uses it so results do not move
    /// with the calendar.</remarks>
    public Task<ExtractedEvent?> ExtractAsync(string subject, string bodyText, CancellationToken ct = default)
        => ExtractAsync(subject, sender: null, bodyText, DateTimeOffset.UtcNow, ct);

    /// <summary>
    /// The form that takes the sender and the arrival time.
    ///
    /// The heuristic uses <paramref name="receivedAt"/> and nothing else from the two extra
    /// arguments: a weekday or "tomorrow" can only be resolved against the day the message
    /// arrived, and resolving it against the wall clock made the same email produce a
    /// different start date depending on when it was processed - which is fatal for a golden
    /// set and merely wrong for the poller's up-to-seven-day lookback. Sender is accepted and
    /// ignored, since inventing a title from an address is not something this class should do.
    /// </summary>
    public Task<ExtractedEvent?> ExtractAsync(
        string subject, string? sender, string bodyText, DateTimeOffset receivedAt,
        CancellationToken ct = default)
    {
        var body = (bodyText ?? string.Empty).Trim();
        var head = (subject ?? string.Empty).Trim();

        if (body.Length < MinBodyChars && head.Length < MinBodyChars)
            return Task.FromResult<ExtractedEvent?>(null);

        // Pre-filter (plan §4): most mail is not an invite. Null here is the same
        // NoEvent outcome an LLM would report, paid for with a regex.
        //
        // An activity name qualifies as a signal in any form, matched as a prefix exactly as
        // InvitationSignal is. The tag extraction below keeps its whole-word ContainsWord, and
        // the asymmetry is the point: this gate asks "could this be about an activity?" while
        // tags ask "was this activity actually named as a word?". "Footy" must let a message
        // through, and must not become a tag on a message that only contains "Mount Gambier".
        if (!InvitationSignal().IsMatch(head) && !InvitationSignal().IsMatch(body)
            && !ActivityWords.Any(w => ContainsPrefix(body, w) || ContainsPrefix(head, w)))
            return Task.FromResult<ExtractedEvent?>(null);

        var missing = new List<string>();

        // The subject is the better title source when present; truncating keeps a
        // long subject from smuggling a paragraph into a bounded column.
        var title = FindTitle(head, body);
        if (title is null) missing.Add("title");

        var tags = ActivityWords
            .Where(w => ContainsWord(body, w) || ContainsWord(head, w))
            .Take(TagNormalizer.MaxTagsPerEvent)
            .ToList();

        var start = FindStart(body, head, receivedAt);
        if (start is null) missing.Add("startAt");

        // Checked here, immediately, before anything else is read: an event that has already
        // finished is not a draft with a missing field, and every other value derived from a
        // stale date inherits the staleness. Reported through MissingFields so the row still
        // records why nothing was proposed, rather than vanishing.
        if (start is { } resolvedStart && IsPast(resolvedStart))
        {
            return Task.FromResult<ExtractedEvent?>(new ExtractedEvent
            {
                Title = head.Length >= 3 ? Truncate(head, 80) : null,
                StartAt = resolvedStart,
                Confidence = 0m,
                MissingFields = ["pastEvent"],
                Reasoning = $"The stated date resolves to {resolvedStart:yyyy-MM-dd}, which has "
                          + "already passed, so no draft is proposed. If this is a recurring "
                          + "fixture, approve the next occurrence manually.",
                PromptVersion = PromptVersion,
                Model = Model,
            });
        }

        // Subject first: an invitation titled "Advanced Badminton" means what it says, and
        // a stray "beginners welcome" in the footer of a forwarded mail should not overrule
        // it. Null is reported rather than defaulted — a wrong level actively deters the
        // wrong players, which is the one way this field does real harm.
        var skill = FindSkill(head) ?? FindSkill(body);
        if (skill is null) missing.Add("skillLevel");

        var end = start is { } s ? FindEnd(body, s) : null;
        if (end is null) missing.Add("endAt");

        // Null when unstated, deliberately *not* the validator's floor. A capacity of 2 is a
        // specific, plausible-looking claim about someone else's event, and it reads as
        // extracted rather than as defaulted - which is the failure mode this whole extractor
        // is written to avoid. An empty field shows up as a chip the reviewer has to fill; a
        // fabricated one gets approved without being looked at.
        var stated = FindCapacity(body);
        if (stated is null) missing.Add("maxParticipants");

        // Read after capacity so a body containing both is still read in full. Null when the
        // phrasing is ambiguous - a wrong price on a public event page deters people from
        // turning up, which is worse than the reviewer typing one in.
        var cost = FindCost(body);
        if (cost is null) missing.Add("cost");

        var venueName = FindVenue(body);
        if (venueName is null) missing.Add("venueName");
        // Text can identify a venue, but only the picker can resolve it to a map point.
        missing.AddRange(["address", "latitude", "longitude"]);

        var hasTime = start is not null;
        var confidence = (title, hasTime, tags.Count) switch
        {
            (not null, true, > 0) => 0.6m,
            (not null, true, _) => 0.45m,
            (not null, _, _) => 0.3m,
            _ => 0.15m,
        };

        return Task.FromResult<ExtractedEvent?>(new ExtractedEvent
        {
            Title = title,
            Tags = tags.Count > 0 ? tags : null,
            StartAt = start,
            EndAt = end,
            // Null, not "Australia/Melbourne": the service applies the configured
            // default, so the draft records that the zone came from configuration
            // rather than from the email.
            Timezone = null,
            MaxParticipants = stated,
            Cost = cost,
            SkillLevel = skill,
            VenueName = venueName,
            Description = body.Length > MinBodyChars ? Truncate(CleanMarkup(body), 1000) : null,
            Confidence = confidence,
            MissingFields = missing,
            Reasoning = hasTime
                                ? "Matched an invitation title, date and clock time. Confirm the extracted details and choose a map pin before publishing."
                : "Matched an invitation keyword but no usable date/time, so the start "
                  + "is unset. Treat every other field as unverified.",
            PromptVersion = PromptVersion,
            Model = Model,
        });
    }

    /// <summary>Resolves the day, then the clock time within it. Returns null when
    /// either is missing rather than defaulting to "today": a wrong-but-plausible
    /// date on an approved event is the real harm this feature could cause.</summary>
    private static DateTimeOffset? FindStart(string body, string head, DateTimeOffset receivedAt)
    {
        var day = FindExplicitDate(body) ?? FindExplicitDate(head)
                  ?? FindDay(body, receivedAt) ?? FindDay(head, receivedAt);
        if (day is not { } dayDate) return null;

        var range = TimeRange().Match(body);
        var time = range.Success ? range : TimeRange().Match(head);
        if (time.Success)
        {
            var meridiem = time.Groups[3].Success ? time.Groups[3] : time.Groups[6];
            if (!TryClock(time.Groups[1], time.Groups[2], meridiem, out var rangeHour, out var rangeMinute))
                return null;
            return AtWallClock(dayDate, rangeHour, rangeMinute);
        }

        var match = TimeOfDay().Match(body);
        if (!match.Success) match = TimeOfDay().Match(head);
        if (!match.Success || !TryClock(match.Groups[1], match.Groups[2], match.Groups[3], out var hour, out var minute))
            return null;

        return AtWallClock(dayDate, hour, minute);
    }

    private static DateTimeOffset AtWallClock(DateOnly dayDate, int hour, int minute)
    {
        // Melbourne wall clock, because that is this product's market and the entity's
        // own default. The offset is taken for the resolved date so a date crossing an
        // AEDT/AEST boundary is not shifted by an hour.
        //
        // Then converted to UTC, because Npgsql refuses to write a DateTimeOffset with
        // any offset other than zero to timestamptz - it throws ArgumentException at
        // parameter-bind time, so this is a hard requirement and not a preference. The
        // wall-clock reading the zone was chosen for survives in the payload's
        // "timezone" field, which is the same division of labour DemoSeeder.At uses
        // and what lets a Melbourne event still read "7:30pm" to another viewer.
        var zone = WallClockZone;
        var wall = new DateTime(dayDate.Year, dayDate.Month, dayDate.Day, hour, minute, 0);
        return new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(wall, zone), TimeSpan.Zero);
    }

    private static string? FindTitle(string subject, string body)
    {
        foreach (var match in new[] { LabeledTitle().Match(body), MarkdownHeading().Match(body), BoldTitle().Match(body) })
        {
            if (match.Success)
            {
                var title = CleanMarkup(match.Groups[1].Value);
                if (title.Length >= 3) return Truncate(title, 120);
            }
        }

        return subject.Length >= 3 ? Truncate(CleanMarkup(subject), 120) : null;
    }

    private static string? FindVenue(string body)
    {
        var match = VenueLine().Match(body);
        if (!match.Success) return null;
        var venue = CleanMarkup(match.Groups[1].Value).Trim().TrimEnd('.', ',');
        return venue.Length > 0 ? Truncate(venue, 120) : null;
    }

    private static DateOnly? FindExplicitDate(string text)
    {
        var match = AbsoluteDate().Match(text);
        if (!match.Success) return null;

        var value = OrdinalSuffix().Replace(match.Value, string.Empty);
        value = WeekdayPrefix().Replace(value, string.Empty);
        var formats = new[]
        {
            "d MMMM yyyy", "dd MMMM yyyy", "d MMM yyyy", "dd MMM yyyy",
            "MMMM d yyyy", "MMMM dd yyyy", "MMM d yyyy", "MMM dd yyyy",
            "d/M/yyyy", "dd/MM/yyyy", "d-M-yyyy", "dd-MM-yyyy",
        };
        return DateOnly.TryParseExact(
            value, formats, System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None, out var date)
            ? date
            : null;
    }

    /// <summary>Hour/minute from a time capture. Rejects an ambiguous bare number (a
    /// lone "7" could be 07:00 or 19:00) rather than coin-flipping it, and rejects an
    /// out-of-range value rather than clamping it.</summary>
    private static bool TryClock(
        Group hourGroup, Group minuteGroup, Group meridiemGroup, out int hour, out int minute)
    {
        hour = 0;
        minute = 0;
        if (!int.TryParse(hourGroup.Value, out hour)) return false;
        if (minuteGroup.Success && !int.TryParse(minuteGroup.Value, out minute)) return false;

        var meridiem = meridiemGroup.Value.ToLowerInvariant();
        // An evening social silently read as 07:00 is exactly the failure the plan's
        // §4 timezone warning describes, so ambiguity is reported, not resolved.
        if (string.IsNullOrEmpty(meridiem) && hour is < 6 or > 23) return false;
        if (hour > 23 || minute > 59) return false;

        if (meridiem == "pm" && hour < 12) hour += 12;
        if (meridiem == "am" && hour == 12) hour = 0;
        return hour <= 23 && minute <= 59;
    }

    /// <summary>The calendar day a relative date in the message refers to.
    ///
    /// <b>Anchored on the message's own day, never the machine clock.</b> "Tomorrow" in a
    /// message that arrived three days ago is not the day this code happens to run, and the
    /// poller deliberately reads up to a week of unread mail in one cycle - anchoring on
    /// <c>DateTime.UtcNow</c> made every backlogged message resolve to the poll date, and made
    /// a golden-set answer depend on the day the evaluator was run.
    ///
    /// Deterministic on purpose: given the same message and the same arrival date, this returns
    /// the same day forever. Whether that day has since passed is a separate question, asked
    /// separately by <see cref="IsPast"/>, and conflating the two would make every case in the
    /// golden set expire on a date nobody recorded.
    ///
    /// <b>Known limitation.</b> A message forwarded weeks later ("it's still on Friday!")
    /// resolves from its original date rather than the resend's, because the forwarded copy
    /// carries no better anchor. The LLM path receives the same instant and can reason about it.</summary>
    private static DateOnly? FindDay(string text, DateTimeOffset receivedAt)
    {
        var match = DayWord().Match(text);
        if (!match.Success) return null;

        // The message's own calendar day, in the zone the event's wall clock will be read in,
        // so a message posted at 11pm Melbourne time on the 21st is anchored on the 21st and
        // not on the 20th as the UTC calendar would have it.
        var anchor = DateOnly.FromDateTime(
            TimeZoneInfo.ConvertTime(receivedAt, WallClockZone).DateTime);
        var word = match.Value.ToLowerInvariant();

        return word switch
        {
            var w when w.StartsWith("today") => anchor,
            var w when w.StartsWith("tomorrow") => anchor.AddDays(1),
            _ => NextWeekdayAfter(anchor, word),
        };
    }

    /// <summary>The next occurrence of a named weekday on or after <paramref name="from"/>.
    ///
    /// "Today included" is what makes it right: an invitation to "Friday" that arrives on
    /// Friday means tonight, and tonight is the forward-looking answer. A weekday that has
    /// genuinely passed resolves to the same name next week, which is the only forward reading
    /// available without a calendar - and is checked by <see cref="IsPast"/> before anything is
    /// drafted.</summary>
    private static DateOnly? NextWeekdayAfter(DateOnly from, string word)
    {
        var target = word switch
        {
            var w when w.StartsWith("mon") => DayOfWeek.Monday,
            var w when w.StartsWith("tues") => DayOfWeek.Tuesday,
            var w when w.StartsWith("wednes") => DayOfWeek.Wednesday,
            var w when w.StartsWith("thurs") => DayOfWeek.Thursday,
            var w when w.StartsWith("frid") => DayOfWeek.Friday,
            var w when w.StartsWith("satur") => DayOfWeek.Saturday,
            var w when w.StartsWith("sund") => DayOfWeek.Sunday,
            _ => (DayOfWeek?)null,
        };
        if (target is not { } day) return null;

        return from.AddDays(((int)day - (int)from.DayOfWeek + 7) % 7);
    }

    /// <summary>True when the resolved start has already happened, in the zone the event will
    /// be shown in.
    ///
    /// The guard against the pipeline's worst output: a draft for an event that is already
    /// over, which looks exactly like a live one in a newest-first queue unless someone reads
    /// its date. It fires mainly on replay - the poller re-reads up to <c>LookbackDays</c> of
    /// inbox, so a recurring fixture's older invitations are all still unread and each would
    /// otherwise become a draft for a night that has passed.
    ///
    /// A whole day, not an instant: an event starting in twenty minutes is still worth a draft,
    /// and a same-day invite whose time has slipped by is a reviewer's call, not a discard.
    /// Compared in the wall-clock zone rather than in UTC, because that is the day the reviewer
    /// and the event page will both show.</summary>
    private static bool IsPast(DateTimeOffset start)
    {
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, WallClockZone).DateTime);
        var startDay = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(start, WallClockZone).DateTime);
        return startDay < today;
    }

    private static DateTimeOffset? FindEnd(string body, DateTimeOffset start)
    {
        var match = TimeRange().Match(body);
        if (!match.Success) return null;
        var meridiem = match.Groups[6].Success ? match.Groups[6] : match.Groups[3];
        if (!TryClock(match.Groups[4], match.Groups[5], meridiem, out var hour, out var minute))
            return null;

        // Rebuilt as wall clock in the same zone as the start, not by reusing
        // start.Offset: start is UTC, and a UTC offset carried over as if it were a
        // Melbourne one would place a 9:30pm finish nine and a half hours out. The
        // start's *local* date must be read by converting into the zone - taking
        // start.Year/Month/Day would use the UTC calendar date, which differs either
        // side of midnight and would shift a late finish by a whole day.
        var zone = WallClockZone;
        var startLocal = TimeZoneInfo.ConvertTime(start, zone);
        var local = new DateTime(startLocal.Year, startLocal.Month, startLocal.Day, hour, minute, 0);
        DateTimeOffset end = new(TimeZoneInfo.ConvertTimeToUtc(local, zone), TimeSpan.Zero);

        // "9:30pm-11pm" ends on the following calendar day. The comparison is made on
        // instants rather than on this wall-clock local, and that distinction only bites when
        // start is already in the past - which the day resolver can still produce from a body
        // that mentions a weekday in passing, or from an unparseable date reaching this path
        // another day. Comparing the local here would place a 9pm finish a day after a start
        // that ended last week, and a draft 24 hours long looks like a plausible long event
        // rather than like the bug it is. Comparing instants is what keeps the end-after-start
        // rule satisfiable instead of yielding a draft the validator rejects for a reason the
        // reviewer cannot see.
        if (end <= start) end = end.AddDays(1);

        return end;
    }

    /// <summary>An explicit ability phrase, or null. "All abilities"/"all levels" maps to
    /// Beginner: the game is open to a beginner, which is what a reader of the badge needs
    /// to know, and there is no "Any" member in <see cref="SkillLevel"/> to invent.</summary>
    private static SkillLevel? FindSkill(string text)
    {
        var match = SkillPhrase().Match(text);
        if (!match.Success) return null;

        var phrase = match.Value.ToLowerInvariant();
        if (phrase.StartsWith("advanced") || phrase.StartsWith("competitive")) return SkillLevel.Advanced;
        if (phrase.StartsWith("intermediate") || phrase.StartsWith("experienced")) return SkillLevel.Intermediate;
        return SkillLevel.Beginner;
    }

    /// <summary>A per-head price, or null for anything the phrasing leaves ambiguous. See the
    /// <see cref="Cost"/> pattern for exactly which forms are admitted.</summary>
    private static decimal? FindCost(string body)
    {
        if (Regex.IsMatch(body,
            @"\bfree\s+(?:tickets?|entry|admission|to\s+attend)\b|\bno\s+charge\b|\bcomplimentary\s+(?:tickets?|entry|admission)\b|\b(?:tickets?|entry|admission)\s+(?:are\s+)?free\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            return 0m;

        var match = Cost().Match(body);
        if (!match.Success || !decimal.TryParse(
                match.Groups[1].Value, System.Globalization.NumberStyles.AllowDecimalPoint,
                System.Globalization.CultureInfo.InvariantCulture, out var amount))
            return null;

        // An order of magnitude beyond a games fee means the pattern latched onto something
        // else, and a four-figure cost on a social event reads as a real value in the queue
        // rather than as a bug. Treated as unstated, matching FindCapacity's rule.
        return amount is >= 0 and <= 500 ? amount : null;
    }

    private static int? FindCapacity(string body)
    {
        var match = Capacity().Match(body);
        if (!match.Success || !int.TryParse(match.Groups[1].Value, out var n)) return null;
        // Out of range is treated as "not stated" rather than clung to: the caller's
        // default then applies and the field is flagged missing.
        return n is >= 2 and <= 50 ? n : null;
    }

    private static bool ContainsWord(string text, string word)
        => Regex.IsMatch(text, $@"\b{Regex.Escape(word)}\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>Prefix form of <see cref="ContainsWord"/>, for the pre-filter only. See the
    /// comment at its call site for why the gate is looser than the tag match.</summary>
    private static bool ContainsPrefix(string text, string word)
        => Regex.IsMatch(text, $@"\b{Regex.Escape(word)}", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static string Truncate(string value, int max)
        => value.Length <= max ? value : value[..max].TrimEnd();

    private static string CleanMarkup(string value)
    {
        var cleaned = Regex.Replace(value, @"\[([^\]]+)\]\([^)]*\)", "$1", RegexOptions.CultureInvariant);
        cleaned = Regex.Replace(cleaned, @"[*_~`]", string.Empty, RegexOptions.CultureInvariant);
        return cleaned.Trim().TrimStart('#', '>', ' ', '\t').TrimEnd('#', ' ', '\t');
    }
}
