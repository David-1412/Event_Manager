using SportMeet.Domain.Enums;

namespace SportMeet.Application.Events;

/// <summary>Which of the two reminders a repository call is about. Persisted as the
/// name, like every other status column here, so the two sentinel columns can be
/// read in SQL without the caller spelling either one out.</summary>
public enum ReminderKind
{
    /// <summary>24 hours before <c>StartAt</c>.</summary>
    Tomorrow,

    /// <summary>One hour before <c>StartAt</c>.</summary>
    OneHour,
}

/// <summary>One event that is owed a reminder, plus the people to tell.
///
/// Deliberately a flat record rather than an <c>Event</c> plus a separate
/// participant query: the participant ids arrive with the candidate so the service
/// never has to know how membership is stored, and the batch is consistent - the
/// roster the reminder describes is the roster that was read with it.</summary>
public sealed record ReminderCandidate(
    Guid EventId,
    string Title,
    DateTimeOffset StartAt,
    string Timezone,
    IReadOnlyList<Guid> ParticipantIds);

/// <summary>
/// Persistence seam for the reminder worker.
///
/// Shaped as read-then-claim rather than "here is a row, mutate it" for the same
/// reason the join-request methods are: the claim is a conditional UPDATE, so a
/// second worker that lost the race gets <c>false</c> and writes nothing. That
/// property has to survive the abstraction, which a returned entity handed back for
/// the caller to stamp would not - it would let a caller write a notification
/// without ever having claimed anything.
/// </summary>
public interface IEventReminderRepository
{
    /// <summary>Live events whose <paramref name="kind"/> reminder is due and not yet
    /// sent, each carrying its approved participants.
    ///
    /// "Live" excludes Cancelled (requirement: a cancelled event sends nothing) and
    /// everything the review workflow keeps out of the feed. The horizon is a
    /// look-ahead - start between now and <c>now + lead</c> - so the reminder goes out
    /// in advance rather than as an apology afterwards.
    ///
    /// Ordered soonest start first and capped, so a backlog drains in the order the
    /// events need it instead of the freshest ones monopolising every cycle.</summary>
    Task<List<ReminderCandidate>> ListDueAsync(
        ReminderKind kind,
        DateTimeOffset now,
        int limit,
        CancellationToken ct = default);

    /// <summary>Stamp the sentinel for <paramref name="kind"/>, only if it is still
    /// unset. True when this call is the one that moved it off null - and therefore
    /// the one allowed to notify. False means somebody else already sent it, or the
    /// event stopped being eligible between the read and the write.</summary>
    Task<bool> TryClaimAsync(
        Guid eventId,
        ReminderKind kind,
        DateTimeOffset sentAt,
        CancellationToken ct = default);
}

/// <summary>Settings for the reminder worker, bound from the "Reminders" section.
///
/// Unlike <c>Ingestion:Enabled</c>, <see cref="Enabled"/> defaults to true: the
/// success criterion is that participants automatically receive reminders, so a
/// deployment that says nothing must still send them. The switch exists to turn the
/// worker off, not to opt in.</summary>
public sealed class EventReminderOptions
{
    public const string SectionName = "Reminders";

    public bool Enabled { get; init; } = true;

    /// <summary>How often the worker looks. "Every few minutes" per the requirement.
    /// It is also the answer to "how early does a reminder arrive": the one-hour
    /// reminder reaches a participant between 60 and 60-minus-interval minutes before
    /// the start, so the interval is what to lower if reminders are wanted closer to
    /// the mark.</summary>
    public int IntervalMinutes { get; init; } = 5;

    /// <summary>Events per kind per cycle. A ceiling on the work one cycle can do,
    /// so a large backlog cannot make a single pass outlive the interval.</summary>
    public int BatchSize { get; init; } = 100;
}
