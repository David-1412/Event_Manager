namespace SportMeet.Domain.Entities;

/// <summary>
/// A post in the campus forum.
///
/// The author is always recorded, anonymous or not. Ownership (editing, deleting your
/// own post) and moderation both need it; anonymity is a rule about what the API
/// *shows*, enforced where rows become DTOs, never a missing column. A post whose
/// author was not stored could not be taken down for abuse by anyone but an admin
/// deleting rows by hand.
///
/// <see cref="Seq"/> exists for paging. Feeds page by keyset - "the 20 posts after
/// this one" - and a keyset needs a strictly ordered tiebreak. uuid ids are not
/// comparable in EF, and two posts can share a timestamp, so an identity column
/// gives every post a unique, insertion-ordered number the cursor can carry.
/// </summary>
public class ForumPost
{
    public Guid Id { get; set; }

    /// <summary>Insertion order. Database-generated; never set it in code.</summary>
    public long Seq { get; set; }

    public Guid AuthorUserId { get; set; }
    public User Author { get; set; } = null!;

    public int CategoryId { get; set; }
    public ForumCategory Category { get; set; } = null!;

    /// <summary>Optional: a quick question in General Chat does not need a headline.</summary>
    public string? Title { get; set; }
    public string Content { get; set; } = string.Empty;

    public bool IsAnonymous { get; set; }

    public int LikeCount { get; set; }
    public int CommentCount { get; set; }
    public int ViewCount { get; set; }

    /// <summary>Stored, not computed per read, so the Hot feed is an index scan.
    /// See <see cref="ForumRanking.HotScore"/>.</summary>
    public double HotScore { get; set; }

    public bool IsPinned { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }

    /// <summary>When the author last changed the title or body, or null if never.
    /// Shown as "(edited)" next to the timestamp.</summary>
    public DateTimeOffset? EditedAt { get; set; }

    /// <summary>Soft delete: a removed post that has comments keeps its row so the
    /// thread stays readable. A post with no comments is removed outright. The
    /// database also exposes this as the generated column <c>is_deleted</c>.</summary>
    public DateTimeOffset? DeletedAt { get; set; }
}

/// <summary>Feed ranking for the forum.</summary>
public static class ForumRanking
{
    /// <summary>Seconds of age that cost one order of magnitude of engagement (12.5h).</summary>
    public const double DecaySeconds = 45_000;

    /// <summary>
    /// Reddit's "hot" formula: log10 of engagement plus a term that grows with the
    /// post's creation time.
    ///
    /// The time term is fixed at creation, which is the property that matters: a newer
    /// post outranks an older one with the same engagement without any job ever
    /// re-scoring old rows. The score only changes when engagement does, so it can be
    /// stored and indexed. Each 10x of engagement buys a post 12.5 hours.
    ///
    /// Comments count double a like: a reply costs the replier more than a tap, and a
    /// thread people are answering is what a campus feed wants on top.
    /// </summary>
    public static double HotScore(int likes, int comments, DateTimeOffset createdAt)
    {
        var engagement = Math.Max(likes + 2 * comments, 1);
        return Math.Round(
            Math.Log10(engagement) + createdAt.ToUnixTimeSeconds() / DecaySeconds,
            7);
    }
}
