namespace SportMeet.Domain.Entities;

/// <summary>
/// Explicit join entity rather than a skip navigation, because the create path
/// needs to attach already-existing tag rows to a new event without EF issuing
/// inserts for rows that are already there, and because the popularity query
/// groups over this table directly.
/// </summary>
public class EventTag
{
    public Guid EventId { get; set; }
    public Event Event { get; set; } = null!;

    public int TagId { get; set; }
    public Tag Tag { get; set; } = null!;
}
