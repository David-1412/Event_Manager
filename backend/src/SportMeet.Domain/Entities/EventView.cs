namespace SportMeet.Domain.Entities;

/// <summary>
/// One recorded opening of one event's detail page.
///
/// An append-only fact, not a counter. The host's "Views" number is a COUNT over
/// these rows, which is the same decision <c>v_event_feed</c> already makes for
/// participants and interests: a stored counter would need locking on every page
/// load to survive concurrent readers, and a miscounted counter cannot be
/// recomputed after the fact while these rows can be.
///
/// <b>The shape of the deduplication is the whole design.</b> The rule is "at most
/// one view per user per event per day", and a day is a calendar day, not a rolling
/// 24 hours - midnight UTC. <see cref="ViewedOn"/> stores that calendar day as a
/// column so the rule becomes a unique index on (EventId, UserId, ViewedOn) rather
/// than a "has anyone inserted today?" query. A query has to be correct twice -
/// once to decide and once to write - and two simultaneous refreshes can both pass
/// the check before either inserts. The index cannot be raced: the database refuses
/// the second row. It is also what makes the count cheap to reason about, since the
/// invariant is enforced by the schema rather than by every writer remembering to
/// check.
/// </summary>
public class EventView
{
    public Guid Id { get; set; }

    public Guid EventId { get; set; }
    public Event Event { get; set; } = null!;

    /// <summary>Who opened it, or null when an anonymous visitor did.
    ///
    /// Nullable rather than a sentinel like Guid.Empty: "nobody" is a real state
    /// here, and inventing an id for it would create one anonymous person who
    /// shares a daily allowance with every other visitor on the internet. Null
    /// rows are excluded from the unique index instead, so each anonymous visit
    /// counts - which is honest, because there is nothing to deduplicate it
    /// against.</summary>
    public Guid? UserId { get; set; }
    public User? User { get; set; }

    /// <summary>When the page was opened. Kept alongside
    /// <see cref="ViewedOn"/> because the timestamp is the useful half for
    /// everything except the deduplication - a host asking "when did interest
    /// spike" needs the instant, and the day alone cannot answer it.</summary>
    public DateTimeOffset ViewedAt { get; set; }

    /// <summary>The UTC calendar day <see cref="ViewedAt"/> falls on.
    ///
    /// UTC and not the event's timezone deliberately: the requirement is about not
    /// inflating a number through refreshes, and a day that moved with the
    /// viewer's timezone would let the same person view the same event twice in
    /// one of their own days by being in two of them. One fixed day boundary is
    /// the only version of "per day" that two writers can agree on without one
    /// another's context.
    ///
    /// Derived, never supplied by a caller - it is a function of
    /// <see cref="ViewedAt"/>, and storing it is purely so Postgres can enforce
    /// the rule in an index.</summary>
    public DateOnly ViewedOn { get; set; }
}
