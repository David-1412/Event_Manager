using SportMeet.Domain.Enums;

namespace SportMeet.Domain.Entities;

/// <summary>
/// A member's request to join an event whose host has turned on join approval.
///
/// Composite (EventId, UserId) key, exactly like <see cref="EventParticipant"/>:
/// one person holds one standing per event, and that key is the idempotency guard
/// the request endpoint leans on so a double-tapped Join cannot queue two requests.
///
/// The status is a decision record, not a membership record. Approval writes the
/// participant row in the same transaction, so "is this person in?" is always
/// answered by <c>event_participants</c> and never by this table - which is what
/// keeps the browse participant count and the request list from being able to
/// disagree.
/// </summary>
public class EventJoinRequest
{
    public Guid EventId { get; set; }
    public Event Event { get; set; } = null!;

    public Guid UserId { get; set; }
    public User User { get; set; } = null!;

    public JoinRequestStatus Status { get; set; } = JoinRequestStatus.Pending;

    /// <summary>Set when the host approves or rejects; null while Pending. Kept
    /// separate from <see cref="RequestedAt"/> so the host's response time is
    /// measurable and a pending request can be told apart from a decided one
    /// without comparing two timestamps.</summary>
    public DateTimeOffset? DecidedAt { get; set; }

    public DateTimeOffset RequestedAt { get; set; }
}
