namespace SportMeet.Domain.Entities;

/// <summary>
/// One opening of a forum post's page - the forum's counterpart of
/// <see cref="EventView"/>, with the same deduplication: a signed-in reader counts at
/// most once per post per (UTC) day, enforced by a partial unique index; an anonymous
/// opening is recorded each time.
///
/// <see cref="ForumPost.ViewCount"/> is incremented only when a row is actually
/// inserted, so the counter and the rows agree, and it only ever goes up - un-marking
/// "Interested" touches a different table and never this one.
/// </summary>
public class ForumPostView
{
    public Guid Id { get; set; }

    public Guid PostId { get; set; }
    public ForumPost Post { get; set; } = null!;

    public Guid? UserId { get; set; }
    public User? User { get; set; }

    public DateTimeOffset ViewedAt { get; set; }

    public DateOnly ViewedOn { get; set; }
}
