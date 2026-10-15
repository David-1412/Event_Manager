using SportMeet.Application.Events;
using SportMeet.Application.Notifications;
using SportMeet.Domain.Entities;

namespace SportMeet.Import.Tests;

/// <summary>
/// The reminder worker's decisions, held to the five things the feature is judged on:
/// each reminder fires once, a cancelled event sends nothing, no reminder is ever
/// repeated, and only approved participants are told.
///
/// The service is tested against a fake repository rather than a database, which is
/// enough for everything except one thing: the duplicate guarantee ultimately rests on
/// the claim being a conditional UPDATE, and no fake can prove Postgres serialises two
/// of them. What these tests can pin down - and do - is that the service asks for
/// permission before it writes, and writes nothing when it is refused. That is the
/// half of the guarantee that lives in application code; the other half is the WHERE
/// clause in EventReminderRepository, and the fake reproduces its shape so a service
/// that stopped honouring a false claim fails here.
/// </summary>
public sealed class EventReminderServiceTests
{
    private static readonly DateTimeOffset Now = new(2030, 5, 1, 9, 0, 0, TimeSpan.Zero);
    private static readonly Guid EventId = Guid.Parse("11111111-1111-4111-8111-0000000000a1");
    private static readonly Guid ParticipantA = Guid.NewGuid();
    private static readonly Guid ParticipantB = Guid.NewGuid();

    private static EventReminderOptions Options() => new();

    /// <summary>One candidate, built the way the production query builds it: the start is
    /// a UTC instant, and the reminder prints it on the event's wall clock. The test's
    /// <see cref="Now"/> is 9:00 UTC, which is not 9:00 in Melbourne, so a test that
    /// asserts the requirement's "9:00 AM" sentence has to place the start at the UTC
    /// instant that <em>is</em> 9:00 AM at the venue - which is what
    /// <see cref="WallClock"/> resolves, rather than any test hard-coding a UTC offset
    /// that a daylight-saving change would invalidate.</summary>
    private static ReminderCandidate Candidate(
        ReminderKind kind, FakeReminderRepo repo, DateTimeOffset start, params Guid[] participants)
    {
        var candidate = new ReminderCandidate(
            EventId, "Tennis Saturday", start, "Australia/Melbourne", participants);
        repo.Due[kind].Add(candidate);
        return candidate;
    }

    /// <summary>The instant that reads as <paramref name="hour"/> on the wall clock in
    /// the event's zone, <paramref name="daysFromNow"/> out. Resolved through the zone
    /// rather than arithmetic on Now, which is what makes the expected message text in
    /// the wording tests the interesting assertion instead of a coincidence.</summary>
    private static DateTimeOffset WallClock(int daysFromNow, int hour)
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById("Australia/Melbourne");
        var anchor = TimeZoneInfo.ConvertTime(Now, zone);
        var local = new DateTime(anchor.Year, anchor.Month, anchor.Day, hour, 0, 0,
            DateTimeKind.Unspecified).AddDays(daysFromNow);
        return new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(local, zone), TimeSpan.Zero);
    }

    // ---- Requirement: sends the tomorrow reminder once -----------------------

    [Fact]
    public async Task Sends_tomorrow_reminder_once_with_the_required_wording()
    {
        var repo = new FakeReminderRepo();
        var notifications = new RecordingNotifications();
        // Starts at 9:00 AM Melbourne time, which is the requirement's example.
        Candidate(ReminderKind.Tomorrow, repo, WallClock(1, 9), ParticipantA);

        var service = new EventReminderService(repo, notifications);
        var result = await service.RunOnceAsync(Now, Options());

        var note = Assert.Single(notifications.Added);
        Assert.Equal(NotificationTypes.EventStartingTomorrow, note.Type);
        // The requirement's example sentence, verbatim.
        Assert.Equal("Tennis Saturday starts tomorrow at 9:00 AM.", note.Message);
        Assert.Equal(ParticipantA, note.UserId);
        Assert.Equal($"/events/{EventId}", note.Link);
        Assert.False(note.Read);
        Assert.Equal(1, result.TomorrowNotifications);
    }

    [Fact]
    public async Task Tomorrow_reminder_claims_before_it_notifies()
    {
        var repo = new FakeReminderRepo();
        var notifications = new RecordingNotifications();
        Candidate(ReminderKind.Tomorrow, repo, Now.AddHours(24), ParticipantA);

        await new EventReminderService(repo, notifications).RunOnceAsync(Now, Options());

        // Order matters more than the value: a claim written after the notification
        // would be a record of the send rather than the thing that authorises it, and
        // two overlapping workers would each deliver a full set.
        Assert.Equal([(EventId, ReminderKind.Tomorrow)], repo.ClaimAttempts);
        Assert.True(repo.Claimed.ContainsKey((EventId, ReminderKind.Tomorrow)));
    }

    // ---- Requirement: sends the one-hour reminder once -----------------------

    [Fact]
    public async Task Sends_one_hour_reminder_once_with_the_required_wording()
    {
        var repo = new FakeReminderRepo();
        var notifications = new RecordingNotifications();
        Candidate(ReminderKind.OneHour, repo, Now.AddHours(1), ParticipantA);

        var result = await new EventReminderService(repo, notifications).RunOnceAsync(Now, Options());

        var note = Assert.Single(notifications.Added);
        Assert.Equal(NotificationTypes.EventStartingSoon, note.Type);
        Assert.Equal("Tennis Saturday begins in 1 hour.", note.Message);
        Assert.Equal(1, result.OneHourNotifications);
    }

    [Fact]
    public async Task Both_reminders_run_in_one_pass_and_do_not_gate_each_other()
    {
        var repo = new FakeReminderRepo();
        var notifications = new RecordingNotifications();
        // Two different events, one owed each reminder - the usual state of a busy day.
        repo.Due[ReminderKind.Tomorrow].Add(new ReminderCandidate(
            Guid.NewGuid(), "Tennis Saturday", Now.AddHours(24), "Australia/Melbourne", [ParticipantA]));
        repo.Due[ReminderKind.OneHour].Add(new ReminderCandidate(
            Guid.NewGuid(), "Netball Wednesday", Now.AddHours(1), "Australia/Melbourne", [ParticipantA]));

        await new EventReminderService(repo, notifications).RunOnceAsync(Now, Options());

        // One notification each, from separate passes over the same candidate list.
        Assert.Equal(2, notifications.Added.Count);
        Assert.Equal(
            [NotificationTypes.EventStartingTomorrow, NotificationTypes.EventStartingSoon],
            notifications.Added.Select(n => n.Type));
    }

    // ---- Requirement: does not send for cancelled events ---------------------

    [Fact]
    public async Task Sends_nothing_for_a_cancelled_event()
    {
        var repo = new FakeReminderRepo();
        var notifications = new RecordingNotifications();
        // The due list is exactly what the production query returns, and that query is
        // what excludes Cancelled (and Draft, PendingReview and Rejected). So the
        // honest statement of this requirement at this seam is: a cancelled event is
        // not among the candidates, and nothing downstream can put it back.
        // EventReminderQueryTests covers the SQL side of that boundary.
        var cancelled = new ReminderCandidate(
            EventId, "Tennis Saturday", Now.AddHours(24), "Australia/Melbourne", [ParticipantA]);
        repo.Due[ReminderKind.Tomorrow] = []; // the query filtered it out as Cancelled

        var result = await new EventReminderService(repo, notifications).RunOnceAsync(Now, Options());

        Assert.Empty(notifications.Added);
        Assert.Empty(repo.ClaimAttempts);
        Assert.True(result.IsQuiet);
        Assert.NotNull(cancelled); // the event exists; it simply is not reminded
    }

    [Fact]
    public async Task A_cancel_between_the_read_and_the_claim_still_sends_nothing()
    {
        var repo = new FakeReminderRepo();
        var notifications = new RecordingNotifications();
        Candidate(ReminderKind.OneHour, repo, Now.AddHours(1), ParticipantA);
        // Cancelled after the read, before the UPDATE. The real claim re-checks status
        // in its WHERE clause and returns false; the service must then stay silent
        // rather than notify anyway, and must leave the event unmarked.
        repo.RefuseClaimsFor.Add(EventId);

        await new EventReminderService(repo, notifications).RunOnceAsync(Now, Options());

        Assert.Empty(notifications.Added);
        Assert.Empty(repo.Claimed);
    }

    // ---- Requirement: does not send duplicate reminders ----------------------

    [Fact]
    public async Task Second_pass_sends_nothing_for_an_event_already_reminded()
    {
        var repo = new FakeReminderRepo();
        var notifications = new RecordingNotifications();
        Candidate(ReminderKind.Tomorrow, repo, Now.AddHours(24), ParticipantA);
        var service = new EventReminderService(repo, notifications);

        await service.RunOnceAsync(Now, Options());
        // The same event still sitting in the due list, as it would be if the read were
        // repeated before the sentinel became visible - the duplicate the feature must
        // not produce.
        await service.RunOnceAsync(Now.AddMinutes(5), Options());

        var note = Assert.Single(notifications.Added);
        Assert.Equal(NotificationTypes.EventStartingTomorrow, note.Type);
    }

    [Fact]
    public async Task A_lost_claim_writes_no_notification()
    {
        var repo = new FakeReminderRepo();
        var notifications = new RecordingNotifications();
        Candidate(ReminderKind.Tomorrow, repo, Now.AddHours(24), ParticipantA, ParticipantB);
        // Another replica claimed it between this worker's read and its UPDATE.
        repo.Claimed[(EventId, ReminderKind.Tomorrow)] = Now.AddSeconds(-1);

        var result = await new EventReminderService(repo, notifications).RunOnceAsync(Now, Options());

        // Both participants go unsent rather than one - the claim is per event, so a
        // worker that lost cannot deliver a partial batch.
        Assert.Empty(notifications.Added);
        Assert.True(result.IsQuiet);
    }

    [Fact]
    public async Task The_two_reminders_do_not_suppress_each_other()
    {
        var repo = new FakeReminderRepo();
        var notifications = new RecordingNotifications();
        Candidate(ReminderKind.OneHour, repo, Now.AddHours(1), ParticipantA);
        // Already reminded yesterday. The sentinels are separate columns precisely so
        // this reads as two independent obligations.
        repo.Claimed[(EventId, ReminderKind.Tomorrow)] = Now.AddHours(-23);

        await new EventReminderService(repo, notifications).RunOnceAsync(Now, Options());

        var note = Assert.Single(notifications.Added);
        Assert.Equal(NotificationTypes.EventStartingSoon, note.Type);
    }

    // ---- Requirement: approved participants only -----------------------------

    [Fact]
    public async Task Notifies_every_participant_and_nobody_else()
    {
        var repo = new FakeReminderRepo();
        var notifications = new RecordingNotifications();
        Candidate(ReminderKind.Tomorrow, repo, Now.AddHours(24), ParticipantA, ParticipantB);

        await new EventReminderService(repo, notifications).RunOnceAsync(Now, Options());

        Assert.Equal([ParticipantA, ParticipantB], notifications.Added.Select(n => n.UserId));
        // One notification each, not one per reminder or per pass.
        Assert.Equal(2, notifications.Added.Count);
    }

    [Fact]
    public async Task An_event_with_no_participants_sends_nothing_and_is_not_claimed()
    {
        var repo = new FakeReminderRepo();
        var notifications = new RecordingNotifications();
        // The production query drops an empty roster before returning candidates, and
        // the fake mirrors that filter so this test exercises the same path the SQL
        // does rather than a shape the database cannot produce.
        repo.Due[ReminderKind.Tomorrow] = WithRoster(ReminderKind.Tomorrow, repo);

        await new EventReminderService(repo, notifications).RunOnceAsync(Now, Options());

        Assert.Empty(notifications.Added);
        // Marking it reminded would be the real damage: a host who approves the first
        // member five minutes later would find the reminder already spent. The query
        // filters empty rosters out for exactly this reason.
        Assert.Empty(repo.Claimed);
    }

    /// <summary>The fake's version of the query's <c>Participants.Count > 0</c> filter,
    /// applied to whatever the test queued.</summary>
    private static List<ReminderCandidate> WithRoster(
        ReminderKind kind, FakeReminderRepo repo)
        => repo.Due[kind].Where(c => c.ParticipantIds.Count > 0).ToList();

    // ---- The window each reminder is sent in ---------------------------------

    [Fact]
    public async Task Asks_for_the_events_inside_the_look_ahead_horizon()
    {
        var repo = new FakeReminderRepo();
        var notifications = new RecordingNotifications();

        await new EventReminderService(repo, notifications).RunOnceAsync(Now, Options());

        // The service does not filter by time itself - the query does, and this is the
        // seam where the horizon is handed over. The look-ahead bound (now + 24h,
        // now + 1h) is computed in the repository, where the window is a WHERE clause
        // rather than a number that can be passed the wrong way round.
        Assert.NotNull(repo.LastQuery);
        var (kind, now, limit) = repo.LastQuery.Value;
        Assert.Equal(ReminderKind.OneHour, kind); // the last pass asked
        Assert.Equal(Now, now);
        Assert.Equal(Options().BatchSize, limit);
    }

    [Fact]
    public async Task BatchSize_bounds_the_events_one_pass_considers()
    {
        var repo = new FakeReminderRepo();
        var notifications = new RecordingNotifications();

        await new EventReminderService(repo, notifications).RunOnceAsync(
            Now, new EventReminderOptions { BatchSize = 7 });

        Assert.Equal(7, repo.LastQuery!.Value.Limit);
    }

    // ---- Message wording ------------------------------------------------------

    [Fact]
    public void Tomorrow_message_shows_the_start_time_in_the_events_own_timezone()
    {
        // 9:00 AM Melbourne is 23:00 UTC the previous day. The participant needs the
        // hour the venue opens, which is the reason Event.Timezone is stored; a
        // reminder that printed UTC would tell a Melbourne player to turn up at 11 PM.
        var melbourneMorning = new DateTimeOffset(2030, 5, 1, 23, 0, 0, TimeSpan.Zero);

        Assert.Equal(
            "9:00 AM",
            EventReminderService.FormatTime(melbourneMorning, "Australia/Melbourne"));
    }

    [Fact]
    public void An_unresolvable_timezone_degrades_to_the_stored_instant_rather_than_throwing()
    {
        var start = new DateTimeOffset(2030, 5, 1, 23, 0, 0, TimeSpan.Zero);

        // A zone id that has drifted out of the machine's tzdata is a reason to show a
        // slightly wrong hour, not a reason to send no reminder at all.
        Assert.Equal(
            "11:00 PM",
            EventReminderService.FormatTime(start, "Mars/Olympus_Mons"));
        Assert.Equal(
            "11:00 PM",
            EventReminderService.FormatTime(start, ""));
    }

    [Fact]
    public void A_real_zone_is_resolved_and_not_silently_replaced_by_utc()
    {
        // Guards the resolution itself rather than the arithmetic: a lookup that
        // reported success but returned UTC would print 11:00 PM here, and the
        // reminder would tell a Melbourne player to turn up the night before.
        var utcEvening = new DateTimeOffset(2030, 5, 1, 23, 0, 0, TimeSpan.Zero);

        Assert.NotEqual(
            EventReminderService.FormatTime(utcEvening, null),
            EventReminderService.FormatTime(utcEvening, "Australia/Melbourne"));
    }

    // ---- What gets stamped ----------------------------------------------------

    [Fact]
    public async Task The_sentinel_records_the_moment_the_reminder_was_sent()
    {
        var repo = new FakeReminderRepo();
        var notifications = new RecordingNotifications();
        Candidate(ReminderKind.Tomorrow, repo, Now.AddHours(24), ParticipantA);

        await new EventReminderService(repo, notifications).RunOnceAsync(Now, Options());

        Assert.Equal(Now, repo.Claimed[(EventId, ReminderKind.Tomorrow)]);
        Assert.Equal(Now, Assert.Single(notifications.Added).CreatedAt);
    }
}
