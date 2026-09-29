namespace SportMeet.Domain.Entities;

/// <summary>
/// Composite (EventId, UserId) key, so it maps to a keyless-adjacent shape: EF
/// needs both properties marked Key. Created only by the participation
/// endpoints, which are a later milestone; the table and the derived
/// participant count already exist so that milestone adds no migration.
/// </summary>
public class EventParticipant
{
    public Guid EventId { get; set; }
    public Event Event { get; set; } = null!;

    public Guid UserId { get; set; }
    public User User { get; set; } = null!;

    public DateTimeOffset JoinedAt { get; set; }
}
