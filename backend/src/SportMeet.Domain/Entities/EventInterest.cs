namespace SportMeet.Domain.Entities;

/// <summary>
/// A single "Interested" marker: one row per (event, person). Interest is a
/// softer signal than <see cref="EventParticipant"/> — it neither reserves a
/// spot nor counts toward capacity — so it lives in its own table rather than
/// a flag on the participant row.
///
/// Composite (EventId, UserId) key, mirroring EventParticipant: EF needs both
/// properties marked Key, and the key is itself the "a user can only be
/// interested once" guarantee. Joining an event removes the row (the service
/// does this in the join transaction), so the Interested and Joined sets stay
/// disjoint and the Interested count never double-counts a joiner.
/// </summary>
public class EventInterest
{
    public Guid EventId { get; set; }
    public Event Event { get; set; } = null!;

    public Guid UserId { get; set; }
    public User User { get; set; } = null!;

    public DateTimeOffset CreatedAt { get; set; }
}
