using SportMeet.Domain.Entities;
using SportMeet.Domain.Enums;

namespace SportMeet.Import.Tests;

/// <summary>
/// RecurrenceRule.Expand - how "weekly on Tue and Thu until 15 Dec" becomes concrete
/// sessions. Pure: no database, no service. Times are checked in Melbourne local time,
/// because that is what a host typed and what must survive a daylight-saving change.
/// </summary>
public class RecurrenceRuleTests
{
    private static readonly TimeZoneInfo Melbourne = TimeZoneInfo.FindSystemTimeZoneById("Australia/Melbourne");

    /// <summary>A Melbourne wall-clock time as an instant.</summary>
    private static DateTimeOffset Local(int y, int m, int d, int h, int min = 0)
    {
        var wall = new DateTime(y, m, d, h, min, 0, DateTimeKind.Unspecified);
        return new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(wall, Melbourne), TimeSpan.Zero);
    }

    private static DateTime ToLocal(DateTimeOffset instant) => TimeZoneInfo.ConvertTime(instant, Melbourne).DateTime;

    private static IReadOnlyList<(DateTimeOffset Start, DateTimeOffset End)> Expand(
        RecurrenceFrequency frequency, string[]? days, DateOnly? until, int? count, DateTimeOffset start, int hours = 2)
    {
        var firstDate = DateOnly.FromDateTime(ToLocal(start));
        var rule = RecurrenceRule.Create(frequency, days, until, count, firstDate);
        return rule.Expand(start, start.AddHours(hours), Melbourne);
    }

    [Fact]
    public void Weekly_on_two_days_follows_them_from_the_first_session()
    {
        // Tue 3 Mar 2026, 6pm.
        var sessions = Expand(RecurrenceFrequency.Weekly, ["TUE", "THU"], null, 5, Local(2026, 3, 3, 18));

        Assert.Equal(
            [new DateTime(2026, 3, 3, 18, 0, 0), new(2026, 3, 5, 18, 0, 0), new(2026, 3, 10, 18, 0, 0),
             new(2026, 3, 12, 18, 0, 0), new(2026, 3, 17, 18, 0, 0)],
            sessions.Select(s => ToLocal(s.Start)));
    }

    [Fact]
    public void Weekly_always_includes_the_first_sessions_own_weekday()
    {
        // Starts on a Tuesday, host only ticked Thursday: the Tuesday is still a session.
        var sessions = Expand(RecurrenceFrequency.Weekly, ["THU"], null, 3, Local(2026, 3, 3, 18));

        Assert.Equal(
            [DayOfWeek.Tuesday, DayOfWeek.Thursday, DayOfWeek.Tuesday],
            sessions.Select(s => ToLocal(s.Start).DayOfWeek));
    }

    [Fact]
    public void Every_session_keeps_the_first_sessions_duration()
    {
        var sessions = Expand(RecurrenceFrequency.Weekly, null, null, 4, Local(2026, 3, 3, 18), hours: 3);

        Assert.All(sessions, s => Assert.Equal(TimeSpan.FromHours(3), s.End - s.Start));
    }

    [Fact]
    public void Fortnightly_skips_alternate_weeks()
    {
        // Wed 4 Mar 2026, with Monday ticked too. Week 0 has only the Wednesday (its
        // Monday is before the start), week 1 is skipped, week 2 has both.
        var sessions = Expand(RecurrenceFrequency.Biweekly, ["MON", "WED"], null, 4, Local(2026, 3, 4, 9));

        Assert.Equal(
            [new DateOnly(2026, 3, 4), new(2026, 3, 16), new(2026, 3, 18), new(2026, 3, 30)],
            sessions.Select(s => DateOnly.FromDateTime(ToLocal(s.Start))));
    }

    [Fact]
    public void Monthly_skips_months_without_the_day()
    {
        // The 31st: no session in Feb, Apr or Jun.
        var sessions = Expand(RecurrenceFrequency.Monthly, null, null, 4, Local(2026, 1, 31, 12));

        Assert.Equal(
            [new DateOnly(2026, 1, 31), new(2026, 3, 31), new(2026, 5, 31), new(2026, 7, 31)],
            sessions.Select(s => DateOnly.FromDateTime(ToLocal(s.Start))));
    }

    [Fact]
    public void Daily_runs_every_day_to_the_end_date_inclusive()
    {
        var sessions = Expand(RecurrenceFrequency.Daily, null, new DateOnly(2026, 3, 7), null, Local(2026, 3, 3, 7));

        Assert.Equal(5, sessions.Count);
        Assert.Equal(new DateTime(2026, 3, 7, 7, 0, 0), ToLocal(sessions[^1].Start));
    }

    [Fact]
    public void Local_time_survives_the_daylight_saving_change()
    {
        // Melbourne clocks go forward on Sun 4 Oct 2026. 6pm stays 6pm local, so the
        // UTC instant moves an hour earlier rather than the session drifting to 7pm.
        var sessions = Expand(RecurrenceFrequency.Weekly, null, null, 2, Local(2026, 9, 29, 18));

        Assert.Equal(new DateTime(2026, 10, 6, 18, 0, 0), ToLocal(sessions[1].Start));
        Assert.Equal(TimeSpan.FromDays(7) - TimeSpan.FromHours(1), sessions[1].Start - sessions[0].Start);
    }

    [Fact]
    public void A_time_skipped_by_daylight_saving_moves_an_hour_later()
    {
        // 2:30am does not exist on 4 Oct 2026 in Melbourne (2am jumps to 3am).
        var sessions = Expand(RecurrenceFrequency.Daily, null, null, 2, Local(2026, 10, 3, 2, 30));

        Assert.Equal(new DateTime(2026, 10, 4, 3, 30, 0), ToLocal(sessions[1].Start));
    }

    [Fact]
    public void Semester_preset_makes_twelve_weekly_sessions()
    {
        // What the form's "Semester 1 (12 weeks)" sends: weekly, ending 12 weeks
        // after the first session less a day.
        var start = Local(2026, 3, 2, 18);
        var sessions = Expand(RecurrenceFrequency.Weekly, null, new DateOnly(2026, 3, 2).AddDays(12 * 7 - 1), null, start);

        Assert.Equal(12, sessions.Count);
        Assert.Equal(new DateOnly(2026, 5, 18), DateOnly.FromDateTime(ToLocal(sessions[^1].Start)));
    }

    [Fact]
    public void Days_are_stored_Monday_first()
    {
        var rule = RecurrenceRule.Create(RecurrenceFrequency.Weekly, ["fri", "MON"], null, 4, new DateOnly(2026, 3, 4));

        Assert.Equal("MON,WED,FRI", rule.DaysCode);
        Assert.Equal("Weekly on Mon, Wed, Fri", rule.Summary);
    }

    [Theory]
    [InlineData(null, null)]          // no end at all
    [InlineData("2026-04-01", 6)]     // both an end date and a count
    [InlineData(null, 1)]             // a series of one
    [InlineData(null, 53)]            // over the cap
    [InlineData("2026-03-01", null)]  // ends before it starts
    [InlineData("2027-06-01", null)]  // runs over a year
    public void Rejects_a_bad_end(string? until, int? count)
    {
        DateOnly? end = until is null ? null : DateOnly.Parse(until);

        Assert.Throws<InvalidRecurrenceException>(() =>
            RecurrenceRule.Create(RecurrenceFrequency.Weekly, null, end, count, new DateOnly(2026, 3, 3)));
    }

    [Fact]
    public void Rejects_an_unknown_day()
    {
        Assert.Throws<InvalidRecurrenceException>(() =>
            RecurrenceRule.Create(RecurrenceFrequency.Weekly, ["FUNDAY"], null, 4, new DateOnly(2026, 3, 3)));
    }

    [Fact]
    public void Rejects_a_schedule_with_more_than_the_cap_of_sessions()
    {
        // Daily until three months out is ~90 sessions.
        Assert.Throws<InvalidRecurrenceException>(() =>
            Expand(RecurrenceFrequency.Daily, null, new DateOnly(2026, 6, 1), null, Local(2026, 3, 3, 7)));
    }

    [Fact]
    public void Rejects_an_end_date_that_leaves_only_the_first_session()
    {
        // Weekly on Tuesday, ending the following Monday.
        Assert.Throws<InvalidRecurrenceException>(() =>
            Expand(RecurrenceFrequency.Weekly, null, new DateOnly(2026, 3, 9), null, Local(2026, 3, 3, 18)));
    }
}
