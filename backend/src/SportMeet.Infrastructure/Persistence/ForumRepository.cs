using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using SportMeet.Application.Forum;
using SportMeet.Domain.Entities;

namespace SportMeet.Infrastructure.Persistence;

public sealed class ForumRepository(AppDbContext db) : IForumRepository
{
    public async Task<IReadOnlyList<ForumCategory>> ListCategoriesAsync(CancellationToken ct = default) =>
        await db.ForumCategories.AsNoTracking().OrderBy(c => c.SortOrder).ToListAsync(ct);

    public Task<ForumCategory?> FindCategoryAsync(string slug, CancellationToken ct = default) =>
        db.ForumCategories.AsNoTracking().FirstOrDefaultAsync(c => c.Slug == slug, ct);

    public async Task<IReadOnlyList<ForumPostRow>> ListPostsAsync(
        ForumPostQuery query, CancellationToken ct = default)
    {
        var posts = db.ForumPosts.AsNoTracking().Where(p => p.DeletedAt == null);
        if (query.CategoryId is { } categoryId) posts = posts.Where(p => p.CategoryId == categoryId);
        if (query.AuthorUserId is { } authorId) posts = posts.Where(p => p.AuthorUserId == authorId);
        if (query.FollowedByUserId is { } followerId)
        {
            posts = posts.Where(p => !p.IsAnonymous
                && db.UserFollows.Any(f => f.FollowerUserId == followerId && f.FollowedUserId == p.AuthorUserId));
        }
        if (query.Search is { } search)
        {
            // ILIKE with the user's % and _ escaped, so "100%" finds "100%" rather than
            // matching everything after "100".
            var pattern = "%" + EscapeLike(search) + "%";
            posts = posts.Where(p =>
                EF.Functions.ILike(p.Content, pattern, LikeEscape)
                || (p.Title != null && EF.Functions.ILike(p.Title, pattern, LikeEscape)));
        }

        // Keyset paging. Each branch's WHERE is the exact complement of its ORDER BY,
        // and both match an index (ix_forum_posts_[category_]hot / _seq), so a page
        // deep in the feed costs the same as the first.
        if (query.Sort == ForumSort.Hot)
        {
            if (query.After is { HotScore: { } score, Seq: var seq })
            {
                posts = posts.Where(p => p.HotScore < score || (p.HotScore == score && p.Seq < seq));
            }
            posts = posts.OrderByDescending(p => p.HotScore).ThenByDescending(p => p.Seq);
        }
        else
        {
            if (query.After is { Seq: var seq }) posts = posts.Where(p => p.Seq < seq);
            posts = posts.OrderByDescending(p => p.Seq);
        }

        return await posts.Take(query.Take).Select(ToRow).ToListAsync(ct);
    }

    public Task<ForumPostRow?> GetPostAsync(Guid id, CancellationToken ct = default) =>
        db.ForumPosts.AsNoTracking()
            .Where(p => p.Id == id && p.DeletedAt == null)
            .Select(ToRow)
            .FirstOrDefaultAsync(ct);

    public async Task AddPostAsync(ForumPost post, CancellationToken ct = default)
    {
        db.ForumPosts.Add(post);
        await db.SaveChangesAsync(ct);
    }

    public async Task<bool> AddLikeAsync(
        Guid postId, Guid userId, DateTimeOffset createdAt, CancellationToken ct = default)
    {
        var like = new ForumPostLike { PostId = postId, UserId = userId, CreatedAt = createdAt };
        db.ForumPostLikes.Add(like);
        try
        {
            await db.SaveChangesAsync(ct);
            return true;
        }
        catch (DbUpdateException ex) when (ex.InnerException is Npgsql.PostgresException { SqlState: "23505" })
        {
            // Already interested: the key made the decision. Only the duplicate-key
            // code is swallowed, so a deleted post's foreign-key failure still surfaces.
            db.Entry(like).State = EntityState.Detached;
            return false;
        }
    }

    public async Task<bool> RemoveLikeAsync(Guid postId, Guid userId, CancellationToken ct = default) =>
        await db.ForumPostLikes
            .Where(l => l.PostId == postId && l.UserId == userId)
            .ExecuteDeleteAsync(ct) > 0;

    public async Task<int> RefreshEngagementAsync(Guid postId, CancellationToken ct = default)
    {
        // Recount rather than +1/-1: a counter nudged by each request drifts after a
        // retried or concurrent tap, while a recount is correct whatever ran before.
        // The hot score is ForumRanking.HotScore in SQL - log10 of engagement plus
        // whole created-at seconds over the decay constant, rounded to 7 places - so a
        // post scored at creation and one re-scored here sort on the same scale.
        var counts = await db.Database.SqlQuery<int>($"""
            UPDATE sportsmeet.forum_posts p
            SET like_count = c.likes,
                comment_count = c.comments,
                hot_score = round(
                    log(greatest(c.likes + 2 * c.comments, 1)::numeric)
                    + floor(extract(epoch FROM p.created_at))::numeric / {ForumRanking.DecaySeconds}::numeric,
                    7)::double precision
            FROM (SELECT
                    (SELECT count(*)::int FROM sportsmeet.forum_post_likes WHERE post_id = {postId}) AS likes,
                    (SELECT count(*)::int FROM sportsmeet.forum_comments
                     WHERE post_id = {postId} AND deleted_at IS NULL) AS comments) c
            WHERE p.id = {postId}
            RETURNING p.like_count AS "Value"
            """).ToListAsync(ct);
        return counts.SingleOrDefault();
    }

    public async Task<(int ViewCount, bool IsNew)> RecordViewAsync(
        Guid postId, Guid? userId, DateTimeOffset viewedAt, CancellationToken ct = default)
    {
        // The row and the counter move together or not at all, so view_count always
        // equals the rows that were counted - and nothing else ever lowers it.
        //
        // Inside the execution strategy: the API enables EnableRetryOnFailure, which
        // refuses a hand-opened transaction outside its ExecuteAsync (see
        // EventRepository.RunInTransactionAsync). A retry re-runs the whole unit, so
        // each attempt starts from a fresh entity and a clean change tracker.
        var isNew = true;
        await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            isNew = true;
            db.ChangeTracker.Clear();
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            db.ForumPostViews.Add(new ForumPostView
            {
                Id = Guid.NewGuid(),
                PostId = postId,
                UserId = userId,
                ViewedAt = viewedAt,
                ViewedOn = DateOnly.FromDateTime(viewedAt.UtcDateTime),
            });
            try
            {
                await db.SaveChangesAsync(ct);
                await db.ForumPosts.Where(p => p.Id == postId)
                    .ExecuteUpdateAsync(s => s.SetProperty(p => p.ViewCount, p => p.ViewCount + 1), ct);
                await tx.CommitAsync(ct);
            }
            catch (DbUpdateException ex) when (ex.InnerException is Npgsql.PostgresException { SqlState: "23505" })
            {
                // Already counted today for this reader: the daily index decided.
                isNew = false;
                db.ChangeTracker.Clear();
                await tx.RollbackAsync(ct);
            }
        });

        var count = await db.ForumPosts.AsNoTracking().Where(p => p.Id == postId).Select(p => p.ViewCount).FirstAsync(ct);
        return (count, isNew);
    }

    public Task<int> CountViewsByUserAsync(Guid postId, Guid userId, CancellationToken ct = default) =>
        db.ForumPostViews.CountAsync(v => v.PostId == postId && v.UserId == userId, ct);

    public async Task<IReadOnlySet<Guid>> ListLikedPostIdsAsync(
        Guid userId, IReadOnlyCollection<Guid> postIds, CancellationToken ct = default)
    {
        var liked = await db.ForumPostLikes.AsNoTracking()
            .Where(l => l.UserId == userId && postIds.Contains(l.PostId))
            .Select(l => l.PostId)
            .ToListAsync(ct);
        return liked.ToHashSet();
    }

    private const string LikeEscape = @"\";

    /// <summary>Makes user text literal inside a LIKE pattern: the escape character
    /// itself, then the two wildcards.</summary>
    internal static string EscapeLike(string text) =>
        text.Replace(@"\", @"\\").Replace("%", @"\%").Replace("_", @"\_");

    /// <summary>One projection for every read, so the feed and the post page can never
    /// disagree about what a row contains.</summary>
    private static readonly Expression<Func<ForumPost, ForumPostRow>> ToRow = p => new ForumPostRow(
        p.Id,
        p.Seq,
        p.CategoryId,
        p.Category.Name,
        p.Category.Slug,
        p.Title,
        p.Content,
        p.IsAnonymous,
        p.AuthorUserId,
        p.Author.Name,
        p.Author.PhotoUrl,
        p.Author.Role,
        p.LikeCount,
        p.CommentCount,
        p.HotScore,
        p.IsPinned,
        p.CreatedAt,
        p.ViewCount);
}
