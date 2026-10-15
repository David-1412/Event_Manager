namespace SportMeet.Domain.Entities;

/// <summary>
/// One "I want to hear about this person's next event" edge: a row per
/// (follower, followed) pair.
///
/// Composite (FollowerUserId, FollowedUserId) key, mirroring
/// <see cref="EventInterest"/>: EF needs both properties marked Key, and the key is
/// itself the "you cannot follow the same person twice" guarantee. The follow
/// endpoint therefore does not check for an existing row before inserting - a
/// check-then-insert is a race in which two taps both pass the read and one then
/// dies on the constraint, whereas insert-with-catch has exactly one winner.
///
/// The self-follow rule is NOT stored here. It is a CHECK constraint in the
/// migration and a guard in the service, and neither is redundant: the constraint
/// is what makes the rule unbreakable by a path nobody thought of (a future import,
/// a hand-run statement), while the guard is what lets the endpoint answer "you
/// cannot follow yourself" instead of a 500 from a constraint violation.
///
/// Both sides cascade on delete. A follow is a relation between two accounts and
/// means nothing once either is gone, which differs from <c>event_views</c> - there
/// user_id goes SET NULL because a view is a historical fact about an event that
/// outlives the viewer.
/// </summary>
public class UserFollow
{
    public Guid FollowerUserId { get; set; }
    public User FollowerUser { get; set; } = null!;

    public Guid FollowedUserId { get; set; }
    public User FollowedUser { get; set; } = null!;

    public DateTimeOffset CreatedAt { get; set; }
}
