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
        // Candidates: events opened at least once inside the window. Score: every view
        // they have ever had, so the number shown never goes down.
        //
        // The same public rule as profiles: the feed view (no soft-deleted rows), not
        // cancelled, not awaiting or denied review, Public visibility, not over. A
        // private event's view count must not put its title on a campus-wide list.
        var viewedInWindow = db.EventViews.AsNoTracking()
            .Where(x => x.ViewedAt >= since)
            .Select(x => x.EventId)
            .Distinct();

        var top = await db.EventFeed.AsNoTracking()
            .Where(v => viewedInWindow.Contains(v.Id)
                        && v.Visibility == nameof(EventVisibility.Public)
                        && v.Status != nameof(EventStatus.Cancelled)
                        && !ProfileRepository.UnpublishedStatuses.Contains(v.Status)
                        && v.EndAt >= now)
            .Select(v => new { v.Id, v.Title, Score = db.EventViews.Count(x => x.EventId == v.Id) })
            .OrderByDescending(t => t.Score)
            .Take(take)
            .ToListAsync(ct);

        return top.Select(t => new TrendingItemDto(TrendingKinds.Event, t.Id, t.Title, t.Score)).ToList();
    }

    public async Task<IReadOnlyList<TrendingItemDto>> ListTopPostsAsync(
        DateTimeOffset since, int take, CancellationToken ct = default)
    {
        // Same shape as events: opened inside the window, ranked by the stored total,
        // which only rises (forum_posts.view_count).
        var top = await db.ForumPosts.AsNoTracking()
            .Where(p => p.DeletedAt == null && db.ForumPostViews.Any(v => v.PostId == p.Id && v.ViewedAt >= since))
            .OrderByDescending(p => p.ViewCount)
            .Take(take)
            .Select(p => new { p.Id, p.Title, p.Content, p.ViewCount })
            .ToListAsync(ct);

        return top
            .Where(t => t.ViewCount > 0)
            .Select(t => new TrendingItemDto(TrendingKinds.Post, t.Id, TitleOf(t.Title, t.Content), t.ViewCount))
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
