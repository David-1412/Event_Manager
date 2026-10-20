using SportMeet.Application.Common;

namespace SportMeet.Application.Trending;

/// <summary>"event" or "post" - what the item links to.</summary>
public static class TrendingKinds
{
    public const string Event = "event";
    public const string Post = "post";
}

/// <param name="Score">How many people marked it Interested inside the window.</param>
public sealed record TrendingItemDto(string Kind, Guid Id, string Title, int Score);

public enum TrendingWindow
{
    Today,
    ThreeDays,
    Week,
}

public interface ITrendingRepository
{
    /// <summary>Public, live, not-yet-ended events ranked by interests created since
    /// <paramref name="since"/>. Only events with at least one such interest.</summary>
    Task<IReadOnlyList<TrendingItemDto>> ListTopEventsAsync(
        DateTimeOffset since, DateTimeOffset now, int take, CancellationToken ct = default);

    /// <summary>Visible forum posts ranked by interests created since
    /// <paramref name="since"/>. Titles never name an author.</summary>
    Task<IReadOnlyList<TrendingItemDto>> ListTopPostsAsync(
        DateTimeOffset since, int take, CancellationToken ct = default);
}

/// <summary>
/// "Monash Pulse": the ten things campus is most interested in right now, events and
/// forum posts on one list.
///
/// Ranked by interest *gained inside the window*, not by total interest: a post with
/// 200 interests from last month is not news today, while one that picked up 15 this
/// morning is. Windows are rolling (the last 24 hours, 72 hours, 7 days) rather than
/// calendar days, so "Today" is never nearly empty at 12:05am.
///
/// One "Interested" counts the same whether it was on an event or a post. That is a
/// deliberate simplification - the two are one action in the UI, with one word.
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
