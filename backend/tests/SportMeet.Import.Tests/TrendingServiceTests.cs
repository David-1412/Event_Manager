using SportMeet.Application.Common;
using SportMeet.Application.Trending;

namespace SportMeet.Import.Tests;

/// <summary>How Monash Pulse merges events and posts and which window it asks for.
/// The SQL behind each source - public-only events, interests counted inside the
/// window - is exercised against Postgres, not here.</summary>
public sealed class TrendingServiceTests
{
    [Fact]
    public async Task Events_and_posts_share_one_ranking_capped_at_ten()
    {
        var events = Enumerable.Range(1, 10).Select(i => Item(TrendingKinds.Event, $"Event {i}", i * 2)).ToList();
        var posts = Enumerable.Range(1, 10).Select(i => Item(TrendingKinds.Post, $"Post {i}", i * 2 + 1)).ToList();
        var top = await new TrendingService(new FakeTrending(events, posts)).GetTopAsync("week");

        Assert.Equal(TrendingService.Size, top.Count);
        Assert.Equal(21, top[0].Score);
        Assert.Equal(TrendingKinds.Post, top[0].Kind);
        Assert.Equal(top.OrderByDescending(i => i.Score).Select(i => i.Score), top.Select(i => i.Score));
        Assert.Contains(top, i => i.Kind == TrendingKinds.Event);
    }

    [Theory]
    [InlineData(null, 24)]
    [InlineData("today", 24)]
    [InlineData("3d", 72)]
    [InlineData("week", 168)]
    public async Task Each_window_looks_back_the_right_number_of_hours(string? window, int hours)
    {
        var repo = new FakeTrending([], []);
        await new TrendingService(repo).GetTopAsync(window);

        var lookback = DateTimeOffset.UtcNow - repo.Since!.Value;
        Assert.InRange(lookback.TotalHours, hours - 0.01, hours + 0.01);
    }

    [Fact]
    public async Task An_unknown_window_is_rejected()
    {
        await Assert.ThrowsAsync<DomainRuleException>(() =>
            new TrendingService(new FakeTrending([], [])).GetTopAsync("month"));
    }

    private static TrendingItemDto Item(string kind, string title, int score) =>
        new(kind, Guid.NewGuid(), title, score);

    private sealed class FakeTrending(
        IReadOnlyList<TrendingItemDto> events, IReadOnlyList<TrendingItemDto> posts) : ITrendingRepository
    {
        public DateTimeOffset? Since { get; private set; }

        public Task<IReadOnlyList<TrendingItemDto>> ListTopEventsAsync(
            DateTimeOffset since, DateTimeOffset now, int take, CancellationToken ct = default)
        {
            Since = since;
            return Task.FromResult<IReadOnlyList<TrendingItemDto>>(
                events.OrderByDescending(e => e.Score).Take(take).ToList());
        }

        public Task<IReadOnlyList<TrendingItemDto>> ListTopPostsAsync(
            DateTimeOffset since, int take, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<TrendingItemDto>>(
                posts.OrderByDescending(p => p.Score).Take(take).ToList());
    }
}
