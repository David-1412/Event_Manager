using SportMeet.Domain.Enums;

namespace SportMeet.Application.Forum;

public sealed record ForumCategoryDto(int Id, string Name, string Slug);

/// <summary>Who wrote a post. Null on an anonymous post for everyone but its author
/// and admins - see <see cref="ForumService"/>.</summary>
public sealed record ForumAuthorDto(Guid Id, string Name, string? PhotoUrl, UserRole Role);

/// <param name="Content">The full body on the post page; an excerpt in feeds, with
/// <paramref name="IsTruncated"/> saying so.</param>
/// <param name="IsInterested">Whether the caller has marked this post "Interested".
/// Always false for a signed-out caller.</param>
/// <param name="IsMine">True for the caller's own posts, anonymous ones included, so
/// the author can still find and manage what they posted. It reveals nothing to anyone
/// else: it is computed per caller.</param>
public sealed record ForumPostDto(
    Guid Id,
    ForumCategoryDto Category,
    string? Title,
    string Content,
    bool IsTruncated,
    bool IsAnonymous,
    ForumAuthorDto? Author,
    bool IsMine,
    bool IsInterested,
    int LikeCount,
    int CommentCount,
    bool IsPinned,
    DateTimeOffset CreatedAt,
    int ViewCount = 0);

/// <summary>The settled state after opening a post: its view total (never decreases) and
/// the caller's Interested state, which a first view may just have set.</summary>
public sealed record ForumViewResultDto(int ViewCount, bool IsInterested, int LikeCount);

/// <summary>The settled state after marking or clearing "Interested" on a post.</summary>
public sealed record ForumInterestResultDto(bool IsInterested, int LikeCount);

/// <param name="NextCursor">Pass back as <c>cursor</c> for the next page; null on the
/// last page.</param>
public sealed record ForumPostPage(IReadOnlyList<ForumPostDto> Items, string? NextCursor);

/// <param name="Category">The category slug, e.g. "qa".</param>
public sealed record CreateForumPostDto(string Category, string? Title, string Content, bool IsAnonymous);

public enum ForumSort
{
    /// <summary>Engagement weighted by recency. The default.</summary>
    Hot,

    /// <summary>Newest first.</summary>
    New,
}

/// <summary>A post joined with the author and category columns the DTO needs. The
/// author fields are always populated here; redaction happens in the service.</summary>
public sealed record ForumPostRow(
    Guid Id,
    long Seq,
    int CategoryId,
    string CategoryName,
    string CategorySlug,
    string? Title,
    string Content,
    bool IsAnonymous,
    Guid AuthorUserId,
    string AuthorName,
    string? AuthorPhotoUrl,
    UserRole AuthorRole,
    int LikeCount,
    int CommentCount,
    double HotScore,
    bool IsPinned,
    DateTimeOffset CreatedAt,
    int ViewCount = 0);

/// <param name="Search">Text to find in titles and bodies; null for no search.</param>
/// <param name="AuthorUserId">Only this author's posts, anonymous ones included (My Posts).</param>
/// <param name="FollowedByUserId">Only named posts by people this user follows (Following).
/// Anonymous posts are excluded: listing them here would tell a follower who wrote them.</param>
public sealed record ForumPostQuery(
    int? CategoryId,
    ForumSort Sort,
    ForumCursor? After,
    int Take,
    string? Search = null,
    Guid? AuthorUserId = null,
    Guid? FollowedByUserId = null);

/// <summary>Which posts a feed shows.</summary>
public enum ForumTab
{
    All,
    Following,
    Mine,
}
