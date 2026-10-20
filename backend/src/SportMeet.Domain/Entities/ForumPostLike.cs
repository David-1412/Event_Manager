namespace SportMeet.Domain.Entities;

/// <summary>
/// One "Interested" on a forum post - the forum's like. Shown as "Interested" in the
/// UI so posts and events share one action and one word.
///
/// Composite (PostId, UserId) key: the key is the "once per person" rule, so marking
/// interest is an insert that either lands or hits the key, never a
/// check-then-insert race. <see cref="CreatedAt"/> is what the trending windows
/// count - "interested today" is rows created today, not posts created today.
/// </summary>
public class ForumPostLike
{
    public Guid PostId { get; set; }
    public ForumPost Post { get; set; } = null!;

    public Guid UserId { get; set; }
    public User User { get; set; } = null!;

    public DateTimeOffset CreatedAt { get; set; }
}
