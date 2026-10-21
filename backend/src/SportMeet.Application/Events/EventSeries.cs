using System.Globalization;
using SportMeet.Application.Common;
using SportMeet.Domain.Entities;
using SportMeet.Domain.Enums;

namespace SportMeet.Application.Events;

/// <summary>
/// The Application side of recurring events: turns a create request's
/// <see cref="CreateRecurrenceDto"/> into the domain rule and its sessions, and gives
/// every read path the same one-line description of a series.
/// </summary>
public static class EventSeries
{
    public const string DefaultTimezone = "Australia/Melbourne";

    /// <summary>The event's zone. Falls back to Melbourne, then UTC, rather than
    /// throwing: the validator already rejects an unknown zone on create, so this
    /// only meets a bad id on a row written some other way.</summary>
    public static TimeZoneInfo ResolveZone(string? ianaId)
    {
        foreach (var id in new[] { ianaId, DefaultTimezone })
        {
            if (string.IsNullOrWhiteSpace(id)) continue;
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(id);
            }
            catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
            {
            }
        }
        return TimeZoneInfo.Utc;
    }

    /// <summary>Builds the rule a create request asked for. A bad rule is the host's
    /// to fix, so it surfaces as a 422 with the rule's own message.</summary>
    public static RecurrenceRule ToRule(CreateRecurrenceDto dto, DateTimeOffset firstStart, TimeZoneInfo zone)
    {
        if (!Enum.TryParse<RecurrenceFrequency>(dto.Frequency?.Trim(), true, out var frequency)
            || !Enum.IsDefined(frequency))
            throw new DomainRuleException("Choose how often the event repeats.");

        var firstDate = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(firstStart, zone).DateTime);
        try
        {
            return RecurrenceRule.Create(frequency, dto.Days, dto.EndDate, dto.Count, firstDate);
        }
        catch (InvalidRecurrenceException ex)
        {
            throw new DomainRuleException(ex.Message);
        }
    }

    /// <summary>The sessions of <paramref name="rule"/>, with the rule's errors as 422s.</summary>
    public static IReadOnlyList<(DateTimeOffset Start, DateTimeOffset End)> Expand(
        RecurrenceRule rule, DateTimeOffset firstStart, DateTimeOffset firstEnd, TimeZoneInfo zone)
    {
        try
        {
            return rule.Expand(firstStart, firstEnd, zone);
        }
        catch (InvalidRecurrenceException ex)
        {
            throw new DomainRuleException(ex.Message);
        }
    }

    /// <summary>The instant stored as recurrence_end_date: the end of the last day,
    /// local, so "until 15 Dec" reads back as 15 Dec in the event's zone.</summary>
    public static DateTimeOffset EndOfDay(DateOnly date, TimeZoneInfo zone)
    {
        var wall = date.ToDateTime(new TimeOnly(23, 59, 59), DateTimeKind.Unspecified);
        return new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(wall, zone), TimeSpan.Zero);
    }

    /// <summary>"Weekly on Tue, Thu · until 15 Dec 2026", "Monthly · 6 sessions";
    /// null for a one-off event.</summary>
    public static string? Summary(Event e)
    {
        var zone = ResolveZone(e.Timezone);
        var rule = RecurrenceRule.FromEvent(e, zone);
        if (rule is null) return null;

        var end = rule.EndDate is { } until
            ? $"until {until.ToString("d MMM yyyy", CultureInfo.InvariantCulture)}"
            : rule.Count is { } count ? $"{count} sessions" : null;
        return end is null ? rule.Summary : $"{rule.Summary} · {end}";
    }
}
