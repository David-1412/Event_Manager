namespace SportMeet.Domain.Entities;

/// <summary>
/// A comment on a forum post, or a reply to one.
///
/// Replies go one level deep: <see cref="ParentCommentId"/> always points at a
/// top-level comment, never at another reply. Replying to a reply attaches to that
/// reply's parent, so a thread is a list of comments each with a flat list of
/// replies - the shape a phone screen can show without indenting off the edge.
///
/// As with posts, the author is always stored and anonymity is a display rule.
/// A deleted comment that still has replies stays as a "[deleted]" placeholder so
/// the replies keep their context (soft delete); one without replies is removed.
/// </summary>
public class ForumComment
{
    public Guid Id { get; set; }

    public Guid PostId { get; set; }
    public ForumPost Post { get; set; } = null!;

    public Guid AuthorUserId { get; set; }
    public User Author { get; set; } = null!;

    public Guid? ParentCommentId { get; set; }
    public ForumComment? ParentComment { get; set; }

    public string Content { get; set; } = string.Empty;
    public bool IsAnonymous { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>When the author last changed the text, or null if never.</summary>
    public DateTimeOffset? EditedAt { get; set; }

    /// <summary>Set on a soft delete. Exposed by the database as <c>is_deleted</c>.</summary>
    public DateTimeOffset? DeletedAt { get; set; }
}
