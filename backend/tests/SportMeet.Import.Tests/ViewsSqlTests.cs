using Microsoft.EntityFrameworkCore;
using SportMeet.Domain.Entities;
using SportMeet.Infrastructure.Persistence;

namespace SportMeet.Import.Tests;

/// <summary>
/// Post views and Monash Pulse against the real schema: the daily de-duplication, a
/// view total that only rises, Interested living in a different table, and the
/// trending query ranking by total views. Skipped (passes vacuously) when no local
/// database with the schema is reachable, like the other *SqlTests.
/// </summary>
public sealed class ViewsSqlTests
{
    private const string ConnectionString =
        "Host=localhost;Port=5432;Database=sportmeet;Username=sportmeet;Password=sportmeet";

    private static readonly Lazy<bool> Usable = new(() =>
    {
        try
        {
            using var db = Open();
            _ = db.ForumPostViews.AsNoTracking().Count();
            return db.ForumCategories.AsNoTracking().Any();
        }
        catch (Exception)
        {
            return false;
        }
    });

    private static AppDbContext Open() => new(new DbContextOptionsBuilder<AppDbContext>()
        // Retries on, as in production (DependencyInjection): it changes how explicit
        // transactions must be written, and a test without it would not notice.
        .UseNpgsql(ConnectionString, npgsql => npgsql.EnableRetryOnFailure(maxRetryCount: 3))
        .UseSnakeCaseNamingConvention()
        .Options);

    [Fact]
    public async Task Views_count_once_a_day_per_reader_and_never_drop_when_interest_is_removed()
    {
        if (!Usable.Value) return;
        await using var db = Open();
        var (author, reader) = (await SeedUserAsync(db), await SeedUserAsync(db));
        var post = await SeedPostAsync(db, author, "views-sql-test");
        var repo = new ForumRepository(db);
        try
        {
            var now = DateTimeOffset.UtcNow;
            Assert.Equal((1, true), await repo.RecordViewAsync(post, reader, now));
            Assert.Equal((1, false), await repo.RecordViewAsync(post, reader, now));   // same reader, same day
            Assert.Equal((2, true), await repo.RecordViewAsync(post, null, now));      // anonymous counts each time
            Assert.Equal(1, await repo.CountViewsByUserAsync(post, reader));

            await repo.AddLikeAsync(post, reader, now);
            Assert.Equal(1, await repo.RefreshEngagementAsync(post));
            await repo.RemoveLikeAsync(post, reader);
            Assert.Equal(0, await repo.RefreshEngagementAsync(post));

            var stored = await db.ForumPosts.AsNoTracking().SingleAsync(p => p.Id == post);
            Assert.Equal(2, stored.ViewCount);
        }
        finally
        {
            await CleanupAsync(db, [author, reader], [post]);
        }
    }

    [Fact]
    public async Task Pulse_ranks_posts_viewed_in_the_window_by_total_views()
    {
        if (!Usable.Value) return;
        await using var db = Open();
        var author = await SeedUserAsync(db);
        var busy = await SeedPostAsync(db, author, "pulse-sql-busy");
        var quiet = await SeedPostAsync(db, author, "pulse-sql-quiet");
        var stale = await SeedPostAsync(db, author, "pulse-sql-stale");
        var forum = new ForumRepository(db);
        var trending = new TrendingRepository(db);
        try
        {
            var now = DateTimeOffset.UtcNow;
            for (var i = 0; i < 5; i++) await forum.RecordViewAsync(busy, null, now);
            await forum.RecordViewAsync(quiet, null, now);
            // Lots of views, but all of them before the window: not on this list.
            for (var i = 0; i < 9; i++) await forum.RecordViewAsync(stale, null, now.AddDays(-10));

            var top = (await trending.ListTopPostsAsync(now.AddDays(-1), 50))
                .Where(t => t.Id == busy || t.Id == quiet || t.Id == stale).ToList();

            Assert.Equal([busy, quiet], top.Select(t => t.Id));
            Assert.Equal([5, 1], top.Select(t => t.Score));

            // The events half must at least translate and run against the real view.
            _ = await trending.ListTopEventsAsync(now.AddDays(-7), now, 10);
        }
        finally
        {
            await CleanupAsync(db, [author], [busy, quiet, stale]);
        }
    }

    private static async Task<Guid> SeedUserAsync(AppDbContext db)
    {
        var id = Guid.NewGuid();
        db.Users.Add(new User { Id = id, Name = $"views-{id:N}"[..16], CreatedAt = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        return id;
    }

    private static async Task<Guid> SeedPostAsync(AppDbContext db, Guid author, string title)
    {
        var category = await db.ForumCategories.AsNoTracking().Select(c => c.Id).FirstAsync();
        var now = DateTimeOffset.UtcNow;
        var post = new ForumPost
        {
            Id = Guid.NewGuid(),
            AuthorUserId = author,
            CategoryId = category,
            Title = title,
            Content = "body",
            HotScore = ForumRanking.HotScore(0, 0, now),
            CreatedAt = now,
        };
        db.ForumPosts.Add(post);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        return post.Id;
    }

    private static async Task CleanupAsync(AppDbContext db, Guid[] users, Guid[] posts)
    {
        // Views, likes and comments cascade from the posts; posts restrict their author.
        await db.ForumPosts.Where(p => posts.Contains(p.Id)).ExecuteDeleteAsync();
        await db.Users.Where(u => users.Contains(u.Id)).ExecuteDeleteAsync();
    }
}
