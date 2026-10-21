using SportMeet.Application.Common;

namespace SportMeet.Application.Trending;

/// <summary>"event" or "post" - what the item links to.</summary>
public static class TrendingKinds
{
    public const string Event = "event";
    public const string Post = "post";
}

/// <param name="Score">Total page views, all time. Only ever rises - un-marking Interested
/// never lowers it.</param>
public sealed record TrendingItemDto(string Kind, Guid Id, string Title, int Score);

public enum TrendingWindow
{
    Today,
    ThreeDays,
    Week,
}

public interface ITrendingRepository
{
    /// <summary>Public, live, not-yet-ended events viewed since <paramref name="since"/>,
    /// ranked by their total views.</summary>
    Task<IReadOnlyList<TrendingItemDto>> ListTopEventsAsync(
        DateTimeOffset since, DateTimeOffset now, int take, CancellationToken ct = default);

    /// <summary>Visible forum posts viewed since <paramref name="since"/>, ranked by their
    /// total views. Titles never name an author.</summary>
    Task<IReadOnlyList<TrendingItemDto>> ListTopPostsAsync(
        DateTimeOffset since, int take, CancellationToken ct = default);
}

/// <summary>
/// "Campus Pulse": the ten most-viewed things on campus right now, events and forum
/// posts on one list.
///
/// The score is total page views, and it never goes down: views are counted, never
/// removed, and they are a separate table from Interested, which people can turn off.
/// A window cannot shrink a total, so the windows choose *which* items compete
/// instead: Today lists what was opened in the last 24 hours (Past 3 Days: 72 hours,
/// This Week: 7 days), ranked by everything those items have ever been viewed. A
/// post nobody has opened this week drops off This Week, but its number never falls.
///
/// Signed-in readers count once per item per day; anonymous openings count each time
/// (rate-limited for posts). One view counts the same on an event as on a post.
/// </summary>
public sealed class TrendingService(ITrendingRepository trending)
{
    public const int Size = 10;

    public async Task<IReadOnlyList<TrendingItemDto>> GetTopAsync(string? window, CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        var since = now - Span(ParseWindow(window));

        // Each source contributes its own top ten, which is enough: the merged top ten
        // can never need an eleventh item from either side.
        var events = await trending.ListTopEventsAsync(since, now, Size, ct);
        var posts = await trending.ListTopPostsAsync(since, Size, ct);

        return events.Concat(posts)
            .OrderByDescending(i => i.Score)
            .ThenBy(i => i.Title, StringComparer.OrdinalIgnoreCase)
            .Take(Size)
            .ToList();
    }

    public static TrendingWindow ParseWindow(string? window) => window?.ToLowerInvariant() switch
    {
        null or "" or "today" => TrendingWindow.Today,
        "3d" or "3days" => TrendingWindow.ThreeDays,
        "week" or "7d" => TrendingWindow.Week,
        _ => throw new DomainRuleException("Choose \"today\", \"3d\" or \"week\"."),
    };

    private static TimeSpan Span(TrendingWindow window) => window switch
    {
        TrendingWindow.Today => TimeSpan.FromHours(24),
        TrendingWindow.ThreeDays => TimeSpan.FromDays(3),
        _ => TimeSpan.FromDays(7),
    };
}
