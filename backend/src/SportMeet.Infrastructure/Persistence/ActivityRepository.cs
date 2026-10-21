using Microsoft.EntityFrameworkCore;
using SportMeet.Application.Activity;
using SportMeet.Domain.Entities;

namespace SportMeet.Infrastructure.Persistence;

public sealed class ActivityRepository(AppDbContext db) : IActivityRepository
{
    public async Task<IReadOnlyList<ActivityRow>> ListAsync(
        Guid userId, ActivityKind kind, DateTimeOffset since, int take, CancellationToken ct = default) =>
        kind switch
        {
            ActivityKind.Likes => await LikesAsync(userId, since, take, ct),
            ActivityKind.Follows => await FollowsAsync(userId, since, take, ct),
            _ => await CommentsAsync(userId, since, take, ct),
        };

    public async Task<IReadOnlySet<string>> ListReadAsync(
        Guid userId, IReadOnlyCollection<string> keys, CancellationToken ct = default)
    {
        if (keys.Count == 0) return new HashSet<string>();
        var list = keys.ToList();
        var read = await db.ActivityReads.AsNoTracking()
            .Where(r => r.UserId == userId && list.Contains(r.ItemKey))
            .Select(r => r.ItemKey)
            .ToListAsync(ct);
        return read.ToHashSet();
    }

    public async Task MarkReadAsync(Guid userId, IReadOnlyCollection<string> keys, DateTimeOffset at, CancellationToken ct = default)
    {
        var already = await ListReadAsync(userId, keys, ct);
        foreach (var key in keys.Where(k => !already.Contains(k)))
        {
            db.ActivityReads.Add(new ActivityRead { UserId = userId, ItemKey = key, ReadAt = at });
        }
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is Npgsql.PostgresException { SqlState: "23505" })
        {
            // Another tab marked the same item a moment ago - it is read either way.
            db.ChangeTracker.Clear();
        }
    }

    /// <summary>Interested on your posts, and on your events. Automatic (first-view)
    /// event interests are left out: they mean "opened it", not "liked it".</summary>
    private async Task<List<ActivityRow>> LikesAsync(Guid me, DateTimeOffset since, int take, CancellationToken ct)
    {
        var posts = await db.ForumPostLikes.AsNoTracking()
            .Where(l => l.Post.AuthorUserId == me && l.UserId != me && l.Post.DeletedAt == null && l.CreatedAt >= since)
            .OrderByDescending(l => l.CreatedAt)
            .Take(take)
            .Select(l => new
            {
                l.PostId, l.CreatedAt, l.Post.Title, l.Post.Content,
                Actor = new ActivityActorDto(l.User.Id, l.User.Name, l.User.PhotoUrl, l.User.Role),
            })
            .ToListAsync(ct);

        var events = await db.EventInterests.AsNoTracking()
            .Where(i => i.Event.HostId == me && i.UserId != me && !i.IsAuto && i.CreatedAt >= since)
            .OrderByDescending(i => i.CreatedAt)
            .Take(take)
            .Select(i => new
            {
                i.EventId, i.CreatedAt, i.Event.Title,
                Actor = new ActivityActorDto(i.User.Id, i.User.Name, i.User.PhotoUrl, i.User.Role),
            })
            .ToListAsync(ct);

        return posts
            .Select(p => new ActivityRow(
                ActivityKeys.PostLike(p.PostId, p.Actor.Id, p.CreatedAt), ActivityKind.Likes, p.Actor, false,
                "is interested in your post", TrendingRepository.TitleOf(p.Title, p.Content), null,
                $"/forum/{p.PostId}", p.CreatedAt))
            .Concat(events.Select(e => new ActivityRow(
                ActivityKeys.EventInterest(e.EventId, e.Actor.Id, e.CreatedAt), ActivityKind.Likes, e.Actor, false,
                "is interested in your event", e.Title, null, $"/events/{e.EventId}", e.CreatedAt)))
            .OrderByDescending(r => r.CreatedAt)
            .Take(take)
            .ToList();
    }

    private async Task<List<ActivityRow>> FollowsAsync(Guid me, DateTimeOffset since, int take, CancellationToken ct)
    {
        var follows = await db.UserFollows.AsNoTracking()
            .Where(f => f.FollowedUserId == me && f.CreatedAt >= since)
            .OrderByDescending(f => f.CreatedAt)
            .Take(take)
            .Select(f => new
            {
                f.CreatedAt,
                Actor = new ActivityActorDto(f.FollowerUser.Id, f.FollowerUser.Name, f.FollowerUser.PhotoUrl, f.FollowerUser.Role),
            })
            .ToListAsync(ct);

        return follows.Select(f => new ActivityRow(
                ActivityKeys.Follow(f.Actor.Id, f.CreatedAt), ActivityKind.Follows, f.Actor, false,
                "started following you", null, null, $"/profile/{f.Actor.Id}", f.CreatedAt))
            .ToList();
    }

    /// <summary>Comments on your posts, and replies to your comments - never your own.</summary>
    private async Task<List<ActivityRow>> CommentsAsync(Guid me, DateTimeOffset since, int take, CancellationToken ct)
    {
        var comments = await db.ForumComments.AsNoTracking()
            .Where(c => c.DeletedAt == null && c.AuthorUserId != me && c.CreatedAt >= since
                        && c.Post.DeletedAt == null
                        && (c.Post.AuthorUserId == me
                            || (c.ParentComment != null && c.ParentComment.AuthorUserId == me)))
            .OrderByDescending(c => c.CreatedAt)
            .Take(take)
            .Select(c => new
            {
                c.Id, c.PostId, c.Content, c.IsAnonymous, c.CreatedAt,
                IsReplyToMe = c.ParentComment != null && c.ParentComment.AuthorUserId == me,
                PostTitle = c.Post.Title, PostContent = c.Post.Content,
                Actor = new ActivityActorDto(c.Author.Id, c.Author.Name, c.Author.PhotoUrl, c.Author.Role),
            })
            .ToListAsync(ct);

        return comments.Select(c => new ActivityRow(
                ActivityKeys.Comment(c.Id), ActivityKind.Comments, c.Actor, c.IsAnonymous,
                c.IsReplyToMe ? "replied to your comment" : "commented on your post",
                TrendingRepository.TitleOf(c.PostTitle, c.PostContent),
                TrendingRepository.TitleOf(null, c.Content),
                $"/forum/{c.PostId}", c.CreatedAt))
            .ToList();
    }
}
