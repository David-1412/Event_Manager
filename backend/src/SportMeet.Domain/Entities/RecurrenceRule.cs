using SportMeet.Domain.Enums;

namespace SportMeet.Domain.Entities;

/// <summary>Why a recurrence could not be built - the message is shown to the host.</summary>
public sealed class InvalidRecurrenceException(string message) : Exception(message);

/// <summary>
/// A repeating schedule, in the iCalendar sense (RRULE FREQ / BYDAY / UNTIL / COUNT),
/// cut down to what a campus event needs.
///
/// <see cref="Expand"/> turns it into concrete sessions. It works in the event's own
/// timezone, so "every Tuesday at 6pm" stays 6pm local across a daylight-saving
/// change instead of drifting an hour, which is what repeating a fixed UTC offset
/// would do. The first session is always the start the host picked: for weekly and
/// fortnightly series its weekday is added to the chosen days, as Google Calendar
/// pre-selects it, so the series can never silently skip the date on the form.
/// </summary>
public sealed record RecurrenceRule
{
    /// <summary>Longest series accepted. A semester is ~12-13 weeks; a whole year of
    /// weekly sessions is 52. Anything larger is a mistake, not a schedule.</summary>
    public const int MaxOccurrences = 52;

    /// <summary>A series may not run further than this past its first session.</summary>
    public const int MaxSpanDays = 366;

    private static readonly DayOfWeek[] WeekOrder =
    [
        DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday,
        DayOfWeek.Friday, DayOfWeek.Saturday, DayOfWeek.Sunday,
    ];

    private static readonly Dictionary<string, DayOfWeek> DayCodes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["MON"] = DayOfWeek.Monday, ["TUE"] = DayOfWeek.Tuesday, ["WED"] = DayOfWeek.Wednesday,
        ["THU"] = DayOfWeek.Thursday, ["FRI"] = DayOfWeek.Friday, ["SAT"] = DayOfWeek.Saturday,
        ["SUN"] = DayOfWeek.Sunday,
    };

    public RecurrenceFrequency Frequency { get; }

    /// <summary>The weekdays a weekly/fortnightly series meets on; empty otherwise.</summary>
    public IReadOnlySet<DayOfWeek> Days { get; }

    /// <summary>Last day of the series, inclusive, in the event's local calendar.</summary>
    public DateOnly? EndDate { get; }

    /// <summary>Total number of sessions, the first included.</summary>
    public int? Count { get; }

    private RecurrenceRule(RecurrenceFrequency frequency, IReadOnlySet<DayOfWeek> days, DateOnly? endDate, int? count)
    {
        Frequency = frequency;
        Days = days;
        EndDate = endDate;
        Count = count;
    }

    /// <summary>
    /// Builds a rule for a series whose first session is on <paramref name="firstDate"/>
    /// (local). Exactly one of <paramref name="endDate"/> and <paramref name="count"/>
    /// must be given, as in RRULE, where UNTIL and COUNT are mutually exclusive.
    /// </summary>
    public static RecurrenceRule Create(
        RecurrenceFrequency frequency,
        IEnumerable<string>? dayCodes,
        DateOnly? endDate,
        int? count,
        DateOnly firstDate)
    {
        if (endDate is null == count is null)
            throw new InvalidRecurrenceException("Choose when the series ends: on a date, or after a number of sessions.");
        if (count is < 2 or > MaxOccurrences)
            throw new InvalidRecurrenceException($"A series has between 2 and {MaxOccurrences} sessions.");
        if (endDate is { } end)
        {
            if (end <= firstDate)
                throw new InvalidRecurrenceException("The series has to end after its first session.");
            if (end.DayNumber - firstDate.DayNumber > MaxSpanDays)
                throw new InvalidRecurrenceException("A series can run for at most a year.");
        }

        var days = new HashSet<DayOfWeek>();
        if (frequency is RecurrenceFrequency.Weekly or RecurrenceFrequency.Biweekly)
        {
            foreach (var code in dayCodes ?? [])
            {
                if (!DayCodes.TryGetValue(code.Trim(), out var day))
                    throw new InvalidRecurrenceException($"\"{code}\" is not a day of the week.");
                days.Add(day);
            }
            days.Add(firstDate.DayOfWeek);
        }

        return new RecurrenceRule(frequency, days, endDate, count);
    }

    /// <summary>Rebuilds the rule stored on an event (for its summary line).</summary>
    public static RecurrenceRule? FromEvent(Event e, TimeZoneInfo zone)
    {
        if (!e.IsRecurring || e.RecurrenceFrequency is not { } frequency) return null;
        var days = (e.RecurrenceDays ?? "")
            .Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Where(DayCodes.ContainsKey)
            .Select(c => DayCodes[c])
            .ToHashSet();
        DateOnly? end = e.RecurrenceEndDate is { } until
            ? DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(until, zone).DateTime)
            : null;
        return new RecurrenceRule(frequency, days, end, e.RecurrenceCount);
    }

    /// <summary>"MON,WED" - the stored form of <see cref="Days"/>, Monday first.</summary>
    public string? DaysCode =>
        Days.Count == 0 ? null : string.Join(',', WeekOrder.Where(Days.Contains).Select(DayCode));

    /// <summary>A short line for event pages: "Weekly on Tue, Thu", "Every 2 weeks on
    /// Mon", "Monthly", "Daily".</summary>
    public string Summary
    {
        get
        {
            var on = string.Join(", ", WeekOrder.Where(Days.Contains).Select(d => d.ToString()[..3]));
            return Frequency switch
            {
                RecurrenceFrequency.Daily => "Daily",
                RecurrenceFrequency.Weekly => $"Weekly on {on}",
                RecurrenceFrequency.Biweekly => $"Every 2 weeks on {on}",
                _ => "Monthly",
            };
        }
    }

    /// <summary>
    /// The series as concrete sessions: the first is (<paramref name="firstStart"/>,
    /// <paramref name="firstEnd"/>), the rest keep its local start time and its
    /// duration. Throws when the schedule would produce more than
    /// <see cref="MaxOccurrences"/> sessions, or none beyond the first.
    /// </summary>
    public IReadOnlyList<(DateTimeOffset Start, DateTimeOffset End)> Expand(
        DateTimeOffset firstStart, DateTimeOffset firstEnd, TimeZoneInfo zone)
    {
        var duration = firstEnd - firstStart;
        var local = TimeZoneInfo.ConvertTime(firstStart, zone);
        var firstDate = DateOnly.FromDateTime(local.DateTime);
        var time = TimeOnly.FromDateTime(local.DateTime);
        var weekStart = firstDate.AddDays(-(((int)firstDate.DayOfWeek + 6) % 7)); // Monday of week 0

        var sessions = new List<(DateTimeOffset, DateTimeOffset)>();
        foreach (var date in CandidateDates(firstDate, weekStart))
        {
            if (EndDate is { } end && date > end) break;
            if (Count is { } count && sessions.Count == count) break;
            if (sessions.Count == MaxOccurrences)
                throw new InvalidRecurrenceException(
                    $"That schedule makes more than {MaxOccurrences} sessions. Pick an earlier end date.");

            var start = date == firstDate ? firstStart : ToInstant(date, time, zone);
            sessions.Add((start, start + duration));
        }

        if (sessions.Count < 2)
            throw new InvalidRecurrenceException("That schedule only has one session. Pick a later end date.");
        return sessions;
    }

    private IEnumerable<DateOnly> CandidateDates(DateOnly firstDate, DateOnly weekStart)
    {
        var last = firstDate.AddDays(MaxSpanDays);
        switch (Frequency)
        {
            case RecurrenceFrequency.Daily:
                for (var d = firstDate; d <= last; d = d.AddDays(1)) yield return d;
                break;

            case RecurrenceFrequency.Weekly:
            case RecurrenceFrequency.Biweekly:
                for (var d = firstDate; d <= last; d = d.AddDays(1))
                {
                    var week = (d.DayNumber - weekStart.DayNumber) / 7;
                    if (Frequency == RecurrenceFrequency.Biweekly && week % 2 != 0) continue;
                    if (Days.Contains(d.DayOfWeek)) yield return d;
                }
                break;

            default:
                // The same day of the month. A month without it (the 31st in June) is
                // skipped rather than moved, as calendars do.
                for (var i = 0; i <= 12; i++)
                {
                    var d = firstDate.AddMonths(i);
                    if (d.Day == firstDate.Day) yield return d;
                }
                break;
        }
    }

    /// <summary>Local date + local time in the zone, as an instant. A time that does
    /// not exist that day (the hour skipped when clocks go forward) moves an hour later.</summary>
    private static DateTimeOffset ToInstant(DateOnly date, TimeOnly time, TimeZoneInfo zone)
    {
        var wall = date.ToDateTime(time, DateTimeKind.Unspecified);
        if (zone.IsInvalidTime(wall)) wall = wall.AddHours(1);
        var utc = TimeZoneInfo.ConvertTimeToUtc(wall, zone);
        return new DateTimeOffset(utc, TimeSpan.Zero);
    }

    private static string DayCode(DayOfWeek day) => day.ToString()[..3].ToUpperInvariant();
}
