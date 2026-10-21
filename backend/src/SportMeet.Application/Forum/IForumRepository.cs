using SportMeet.Domain.Entities;

namespace SportMeet.Application.Forum;

/// <summary>Forum storage. Never filters by viewer and never redacts: rows always
/// carry the real author, and <see cref="ForumService"/> decides what a caller sees.</summary>
public interface IForumRepository
{
    Task<IReadOnlyList<ForumCategory>> ListCategoriesAsync(CancellationToken ct = default);

    Task<ForumCategory?> FindCategoryAsync(string slug, CancellationToken ct = default);

    /// <summary>Visible (not deleted) posts in the query's order, strictly after
    /// <see cref="ForumPostQuery.After"/>, at most <see cref="ForumPostQuery.Take"/>.</summary>
    Task<IReadOnlyList<ForumPostRow>> ListPostsAsync(ForumPostQuery query, CancellationToken ct = default);

    /// <summary>Null for a missing or deleted post.</summary>
    Task<ForumPostRow?> GetPostAsync(Guid id, CancellationToken ct = default);

    /// <summary>Like <see cref="GetPostAsync"/>, but a soft-deleted post (one kept for
    /// its comments) is returned too, with <see cref="ForumPostRow.DeletedAt"/> set.</summary>
    Task<ForumPostRow?> GetPostIncludingDeletedAsync(Guid id, CancellationToken ct = default) =>
        GetPostAsync(id, ct);

    /// <summary>Replaces a live post's title and body and stamps edited_at.</summary>
    Task UpdatePostAsync(Guid id, string? title, string content, DateTimeOffset editedAt, CancellationToken ct = default) =>
        throw new NotSupportedException();

    /// <summary>Soft-deletes the post (sets deleted_at) - used when it has comments.</summary>
    Task SoftDeletePostAsync(Guid id, DateTimeOffset deletedAt, CancellationToken ct = default) =>
        throw new NotSupportedException();

    /// <summary>Removes the post's row. Its likes, views and any deleted comments go
    /// with it (ON DELETE CASCADE).</summary>
    Task HardDeletePostAsync(Guid id, CancellationToken ct = default) =>
        throw new NotSupportedException();

    Task AddPostAsync(ForumPost post, CancellationToken ct = default);

    /// <summary>Marks the post Interested. False when the caller already had - the
    /// key decides, so a double tap is a no-op rather than a second row.</summary>
    Task<bool> AddLikeAsync(Guid postId, Guid userId, DateTimeOffset createdAt, CancellationToken ct = default);

    /// <summary>False when there was nothing to remove.</summary>
    Task<bool> RemoveLikeAsync(Guid postId, Guid userId, CancellationToken ct = default);

    /// <summary>Recounts the post's likes and (non-deleted) comments from their tables
    /// and recomputes its hot
    /// score in one statement; returns the new like count.</summary>
    Task<int> RefreshEngagementAsync(Guid postId, CancellationToken ct = default);

    /// <summary>Record one opening of the post. A signed-in reader counts once per post
    /// per day; the post's view_count goes up only when a row is actually inserted,
    /// so it never decreases. Returns the total afterwards and whether this view was new.</summary>
    Task<(int ViewCount, bool IsNew)> RecordViewAsync(
        Guid postId, Guid? userId, DateTimeOffset viewedAt, CancellationToken ct = default) =>
        throw new NotSupportedException();

    /// <summary>How many view rows this signed-in user has for the post, across all days.</summary>
    Task<int> CountViewsByUserAsync(Guid postId, Guid userId, CancellationToken ct = default) =>
        throw new NotSupportedException();

    /// <summary>Which of <paramref name="postIds"/> the user is interested in.</summary>
    Task<IReadOnlySet<Guid>> ListLikedPostIdsAsync(
        Guid userId, IReadOnlyCollection<Guid> postIds, CancellationToken ct = default);
}
