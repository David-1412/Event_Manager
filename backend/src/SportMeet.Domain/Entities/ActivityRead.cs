namespace SportMeet.Domain.Entities;

/// <summary>
/// "I have seen this" for one item in the Likes / New Followers / Comments tabs.
///
/// The items themselves are not stored: they are read live from the tables that
/// already record them (forum_post_likes, event_interests, user_follows,
/// forum_comments), so they can never disagree with what actually happened, and an
/// unliked post or an unfollow simply disappears from the list. Only the read mark
/// needs a home, keyed by a stable string per item (see ActivityKeys).
/// </summary>
public class ActivityRead
{
    public Guid UserId { get; set; }
    public User User { get; set; } = null!;

    public string ItemKey { get; set; } = string.Empty;
    public DateTimeOffset ReadAt { get; set; }
}
