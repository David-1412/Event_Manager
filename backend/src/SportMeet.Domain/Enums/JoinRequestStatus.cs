namespace SportMeet.Domain.Enums;

/// <summary>
/// The lifecycle of a request to join an event that has
/// <c>Event.RequiresJoinApproval</c> set. Three states, and the host's decision
/// is terminal in both directions by design: a rejected member who wants back in
/// submits a fresh request rather than the host undoing a decision, which keeps
/// "the host said no" from silently reversing itself.
///
/// Deliberately separate from <see cref="EventStatus"/>. That enum describes
/// whether an <em>event</em> is live; this one describes one <em>person's</em>
/// standing within it, and the two are decided by different people.
///
/// Persisted as text under a CHECK constraint, matching how status and visibility
/// are stored, so a bad write fails at the database rather than landing a value
/// the client has no rendering for.
/// </summary>
public enum JoinRequestStatus
{
    /// <summary>Submitted, awaiting the host. Reserves no spot.</summary>
    Pending = 0,

    /// <summary>Approved. Carries no capacity of its own - the participant row
    /// written in the same transaction is what makes the member a joiner, so this
    /// status records the decision while <c>event_participants</c> stays the
    /// single source of truth for who is in.</summary>
    Approved = 1,

    /// <summary>Declined. The member stays out of the event.</summary>
    Rejected = 2,
}
