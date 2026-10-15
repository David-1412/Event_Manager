using SportMeet.Application.Notifications;
using SportMeet.Domain.Entities;
using System.Globalization;

namespace SportMeet.Application.Events;

/// <summary>
/// Writes the reminder notifications for events that are about to start.
///
/// The whole job is: ask the repository which events are owed a reminder, claim
/// each one, and write a notification per approved participant. Everything that
/// makes a reminder correct rather than merely sent lives in the ordering of those
/// three steps:
///
///  - <b>Claim before notify.</b> The claim is a conditional UPDATE that only
///    matches while the sentinel column is null, so it is the lock, not a record of
///    the send. Writing notifications first would mean two workers that overlap -
///    two API replicas on one database, or a cycle that outlived its interval - each
///    delivering a full set. Claiming first means the loser of that race writes
///    nothing at all.
///
///  - <b>One claim per event, not per person.</b> A participant list is read with
///    the candidate and could be a moment stale by the time it is written; claiming
///    once for the whole event means a batch is delivered to the roster it was read
///    with, and a worker that loses the race writes nothing at all.
///
///  - <b>No transaction.</b> Each notification saves on its own. A reminder is
///    idempotent-by-claim and loss-tolerant: if the fifth of six inserts fails, the
///    event is already claimed and the remaining person simply does not get one.
///    Wrapping it in a transaction would roll the claim back too, which re-sends to
///    the five who already saw it. Chosen knowingly over all-or-nothing.
///
/// Cancelled events never reach here - the query excludes them - so a cancelled
/// event sends nothing and keeps its sentinel null, which is what lets a host who
/// cancels and reopens the event still be reminded.
/// </summary>
public sealed class EventReminderService(
    IEventReminderRepository events,
    INotificationRepository notifications)
{
    /// <summary>The horizon each reminder is measured back from.</summary>
    public static readonly TimeSpan TomorrowLead = TimeSpan.FromHours(24);
    public static readonly TimeSpan OneHourLead = TimeSpan.FromHours(1);

    /// <summary>Runs one pass of both reminders and reports what it sent. Returns a
    /// count rather than logging so the caller - the hosted service, or a test -
    /// decides what is worth a log line.</summary>
    public async Task<ReminderRunResult> RunOnceAsync(
        DateTimeOffset now,
        EventReminderOptions options,
        CancellationToken ct = default)
    {
        var tomorrow = await RunKindAsync(
            ReminderKind.Tomorrow,
            (title, start, timezone) => $"{title} starts tomorrow at {FormatTime(start, timezone)}",
            TomorrowLead,
            now, options, ct);

        var soon = await RunKindAsync(
            ReminderKind.OneHour,
            (title, _, _) => $"{title} begins in 1 hour",
            OneHourLead,
            now, options, ct);

        return new ReminderRunResult(tomorrow, soon);
    }

    /// <summary>One reminder kind across the events owed it.
    ///
    /// <paramref name="lead"/> is how far ahead of <c>StartAt</c> this reminder
    /// belongs, and the window is the look-ahead <c>(now, now + lead]</c>. The width of
    /// it is the point: a single instant would mean a worker that wakes on the wrong
    /// side of a tick never sends for that event, and a deploy that pauses the process
    /// for a minute would silently skip reminders. An event that enters the window late
    /// - created an hour out, or approved into a spot an hour out - is reminded at the
    /// first cycle after it becomes eligible, which is the honest best available.</summary>
    private async Task<int> RunKindAsync(
        ReminderKind kind,
        Func<string, DateTimeOffset, string, string> messageFor,
        TimeSpan lead,
        DateTimeOffset now,
        EventReminderOptions options,
        CancellationToken ct)
    {
        var candidates = await events.ListDueAsync(kind, now, options.BatchSize, ct);

        var sent = 0;
        foreach (var candidate in candidates)
        {
            if (ct.IsCancellationRequested) break;

            // The claim is the permission to write. False means another worker got
            // here first, or the event went ineligible between read and write - either
            // way, this worker stays silent.
            //
            // ToUniversalTime() is not cosmetic. Npgsql writes a DateTimeOffset to
            // `timestamp with time zone` only at Offset 0, and the sentinel is written
            // by an UPDATE rather than read back, so a non-UTC `now` would throw here
            // and take the whole cycle down with it. The caller passes UTC; normalising
            // makes that a property of this method instead of a promise the next caller
            // has to remember.
            var sentAt = now.ToUniversalTime();
            if (!await events.TryClaimAsync(candidate.EventId, kind, sentAt, ct)) continue;

            var title = TitleFor(kind);
            var message = messageFor(candidate.Title, candidate.StartAt, candidate.Timezone) + ".";
            // Same reason as the claim: notifications.CreatedAt lands in a
            // `timestamp with time zone` column.
            var createdAt = sentAt;

            foreach (var userId in DistinctParticipants(candidate.ParticipantIds))
            {
                await notifications.AddAsync(new Notification
                {
                    Id = Guid.NewGuid(),
                    UserId = userId,
                    Title = title,
                    Message = message,
                    Type = TypeFor(kind),
                    // The same link shape every other event notification uses, so the
                    // bell routes to the event without the client knowing this type.
                    Link = $"/events/{candidate.EventId}",
                    Read = false,
                    CreatedAt = createdAt,
                }, ct);

                sent++;
            }
        }

        return sent;
    }

    private static string TitleFor(ReminderKind kind) => kind switch
    {
        ReminderKind.Tomorrow => "Starts tomorrow",
        _ => "Starting soon",
    };

    /// <summary>Defensive, not load-bearing: <c>event_participants</c> is keyed
    /// (event_id, user_id), so the roster cannot contain a duplicate today. It costs
    /// one hash set to make sure a future read that joins through something else - a
    /// request table, a tag - cannot send one person two copies of the same reminder,
    /// which is the kind of thing a notification bug report is about.</summary>
    private static IEnumerable<Guid> DistinctParticipants(IReadOnlyList<Guid> participantIds)
        => participantIds.Distinct();

    private static string TypeFor(ReminderKind kind) => kind switch
    {
        ReminderKind.Tomorrow => NotificationTypes.EventStartingTomorrow,
        _ => NotificationTypes.EventStartingSoon,
    };

    /// <summary>The start time as a participant reads it: the event's own wall clock,
    /// not the reader's. This is the reason <see cref="Event.Timezone"/> is stored -
    /// a Melbourne event must say 9:00 AM to someone standing in Perth, because the
    /// time they need to leave home for is the venue's.
    ///
    /// Falls back to the stored instant when the zone id will not resolve. A zone
    /// that has drifted out of the OS tzdata is a reason to show a slightly wrong
    /// hour, not a reason to send no reminder at all.
    ///
    /// <c>h:mm tt</c> under the invariant culture so the string is stable regardless
    /// of the machine's locale - a server in another locale must not start writing
    /// "9.00" where it used to write "9:00".
    ///
    /// Public because the test assembly is a separate one and the exact rendering is
    /// the part of the message a requirement fixes; nothing outside this class and
    /// those tests calls it.</summary>
    public static string FormatTime(DateTimeOffset startAt, string? timezone)
    {
        var zone = ResolveZone(timezone);
        var local = zone is null ? startAt : TimeZoneInfo.ConvertTime(startAt, zone);
        return local.ToString("h:mm tt", CultureInfo.InvariantCulture);
    }

    /// <summary>Resolve the event's IANA zone, falling back to null when it cannot be
    /// resolved.
    ///
    /// No round-trip check on the id, deliberately. On Windows
    /// <c>FindSystemTimeZoneById("Australia/Melbourne")</c> succeeds through the IANA
    /// compatibility mapping and hands back a zone whose <c>Id</c> is the Windows id -
    /// so asserting the id came back unchanged would reject every valid zone on this OS
    /// and silently print UTC, which is the exact bug the demo seeder's own ResolveZone
    /// already documented. The mapping is what it is; the zone it gives back is correct,
    /// and the test over the real Melbourne offset is what proves the conversion rather
    /// than the lookup succeeding.
    ///
    /// <see cref="InvalidTimeZoneException"/> is in the catch for completeness even
    /// though the <c>Find</c> overload only throws the not-found one: the cost of
    /// listing it is one type name, and the cost of missing it is a failed reminder.</summary>
    private static TimeZoneInfo? ResolveZone(string? ianaId)
    {
        if (string.IsNullOrWhiteSpace(ianaId)) return null;
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(ianaId);
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            return null;
        }
    }
}

/// <summary>What one worker pass sent, per reminder kind. Small enough to log as-is.</summary>
public sealed record ReminderRunResult(int TomorrowNotifications, int OneHourNotifications)
{
    public int Total => TomorrowNotifications + OneHourNotifications;
    public bool IsQuiet => Total == 0;
}
