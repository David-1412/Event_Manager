using Microsoft.EntityFrameworkCore;
using SportMeet.Application.Trending;
using SportMeet.Domain.Enums;

namespace SportMeet.Infrastructure.Persistence;

public sealed class TrendingRepository(AppDbContext db) : ITrendingRepository
{
    /// <summary>Untitled posts are named by the start of their body, cut here.</summary>
    private const int UntitledLength = 80;

    public async Task<IReadOnlyList<TrendingItemDto>> ListTopEventsAsync(
        DateTimeOffset since, DateTimeOffset now, int take, CancellationToken ct = default)
    {
        // The same public rule as profiles: the feed view (no soft-deleted rows), not
        // cancelled, not awaiting or denied review, Public visibility. A private
        // event's interest count must not leak its title onto a campus-wide list.
        var top = await (
                from i in db.EventInterests.AsNoTracking()
                where i.CreatedAt >= since
                join v in db.EventFeed.AsNoTracking() on i.EventId equals v.Id
                where v.Visibility == nameof(EventVisibility.Public)
                      && v.Status != nameof(EventStatus.Cancelled)
                      && !ProfileRepository.UnpublishedStatuses.Contains(v.Status)
                      && v.EndAt >= now
                group i by new { v.Id, v.Title } into g
                orderby g.Count() descending
                select new { g.Key.Id, g.Key.Title, Score = g.Count() })
            .Take(take)
            .ToListAsync(ct);

        return top.Select(t => new TrendingItemDto(TrendingKinds.Event, t.Id, t.Title, t.Score)).ToList();
    }

    public async Task<IReadOnlyList<TrendingItemDto>> ListTopPostsAsync(
        DateTimeOffset since, int take, CancellationToken ct = default)
    {
        var top = await (
                from l in db.ForumPostLikes.AsNoTracking()
                where l.CreatedAt >= since && l.Post.DeletedAt == null
                group l by l.PostId into g
                orderby g.Count() descending
                select new { PostId = g.Key, Score = g.Count() })
            .Take(take)
            .ToListAsync(ct);

        if (top.Count == 0) return [];

        var ids = top.Select(t => t.PostId).ToList();
        var titles = await db.ForumPosts.AsNoTracking()
            .Where(p => ids.Contains(p.Id))
            .Select(p => new { p.Id, p.Title, p.Content })
            .ToDictionaryAsync(p => p.Id, ct);

        return top
            .Where(t => titles.ContainsKey(t.PostId))
            .Select(t => new TrendingItemDto(
                TrendingKinds.Post, t.PostId, TitleOf(titles[t.PostId].Title, titles[t.PostId].Content), t.Score))
            .ToList();
    }

    /// <summary>A one-line name for the list: the title, or the body's opening words.
    /// Only ever the post's own text - an anonymous post's list entry names no one.</summary>
    internal static string TitleOf(string? title, string content)
    {
        if (!string.IsNullOrWhiteSpace(title)) return title.Trim();
        var line = string.Join(' ', content.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return line.Length <= UntitledLength ? line : line[..UntitledLength].TrimEnd() + "…";
    }
}
