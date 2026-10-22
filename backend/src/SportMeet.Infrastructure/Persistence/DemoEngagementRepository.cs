using Microsoft.EntityFrameworkCore;
using SportMeet.Application.Admin;
using SportMeet.Domain.Entities;
using SportMeet.Domain.Enums;

namespace SportMeet.Infrastructure.Persistence;

/// <summary>
/// The demo-engagement tool's writes, as set-based SQL: one statement per kind of
/// row for the whole site, rather than a round-trip per event.
///
/// Views are anonymous rows tagged <c>is_demo</c>, spread over the last three days so
/// they count in every Campus Pulse window. Interested marks come from the
/// <c>is_demo</c> accounts, each event or post picking ones that have not marked it
/// yet. No notifications are written - the tool bypasses the services that send
/// "someone is interested" - so nobody is told about engagement that did not happen.
/// Each call runs in one transaction inside the execution strategy (see
/// EventRepository.RunInTransactionAsync).
/// </summary>
public sealed class DemoEngagementRepository(AppDbContext db) : IDemoEngagementRepository
{
    public async Task<int> EnsureDemoAccountsAsync(int count, CancellationToken ct = default)
    {
        var existing = await db.Users.Where(u => u.IsDemo).Select(u => u.Name).ToListAsync(ct);
        var now = DateTimeOffset.UtcNow;
        for (var i = 1; i <= count; i++)
        {
            var name = $"demo{i}";
            if (existing.Contains(name)) continue;
            db.Users.Add(new User { Id = Guid.NewGuid(), Name = name, Role = UserRole.Member, IsDemo = true, CreatedAt = now });
        }
        await db.SaveChangesAsync(ct);
        return await db.Users.CountAsync(u => u.IsDemo, ct);
    }

    public Task<DemoEngagementResultDto> AddAsync(
        int minViews, int maxViews, int minInterests, int maxInterests, CancellationToken ct = default) =>
        InTransactionAsync(async () =>
        {
            // How many of each every row gets, drawn once per row and kept in temp
            // tables so the inserts and the post counter agree on the same numbers.
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                CREATE TEMP TABLE demo_events ON COMMIT DROP AS
                SELECT id,
                       ({minViews} + floor(random() * ({maxViews} - {minViews} + 1)))::int AS views,
                       ({minInterests} + floor(random() * ({maxInterests} - {minInterests} + 1)))::int AS interests
                  FROM sportsmeet.events WHERE deleted_at IS NULL;
                CREATE TEMP TABLE demo_posts ON COMMIT DROP AS
                SELECT id,
                       ({minViews} + floor(random() * ({maxViews} - {minViews} + 1)))::int AS views,
                       ({minInterests} + floor(random() * ({maxInterests} - {minInterests} + 1)))::int AS interests
                  FROM sportsmeet.forum_posts WHERE deleted_at IS NULL;
                """, ct);

            var eventViews = await db.Database.ExecuteSqlRawAsync("""
                INSERT INTO sportsmeet.event_views (id, event_id, user_id, viewed_at, viewed_on, is_demo)
                SELECT gen_random_uuid(), e.id, NULL, t.at, (t.at AT TIME ZONE 'UTC')::date, true
                  FROM demo_events e
                  CROSS JOIN LATERAL generate_series(1, e.views) g
                  CROSS JOIN LATERAL (SELECT now() - (random() + 0 * g) * interval '3 days' AS at) t;
                """, ct);
            var postViews = await db.Database.ExecuteSqlRawAsync("""
                INSERT INTO sportsmeet.forum_post_views (id, post_id, user_id, viewed_at, viewed_on, is_demo)
                SELECT gen_random_uuid(), p.id, NULL, t.at, (t.at AT TIME ZONE 'UTC')::date, true
                  FROM demo_posts p
                  CROSS JOIN LATERAL generate_series(1, p.views) g
                  CROSS JOIN LATERAL (SELECT now() - (random() + 0 * g) * interval '3 days' AS at) t;
                """, ct);
            await db.Database.ExecuteSqlRawAsync("""
                UPDATE sportsmeet.forum_posts f SET view_count = f.view_count + p.views
                  FROM demo_posts p WHERE f.id = p.id;
                """, ct);

            var eventInterests = await db.Database.ExecuteSqlRawAsync("""
                INSERT INTO sportsmeet.event_interests (event_id, user_id, interested_at, is_auto)
                SELECT e.id, d.id, now() - random() * interval '3 days', false
                  FROM demo_events e
                  CROSS JOIN LATERAL (
                      SELECT u.id FROM sportsmeet.users u
                       WHERE u.is_demo
                         AND NOT EXISTS (SELECT 1 FROM sportsmeet.event_interests i
                                          WHERE i.event_id = e.id AND i.user_id = u.id)
                       ORDER BY random() LIMIT e.interests) d;
                """, ct);
            var postInterests = await db.Database.ExecuteSqlRawAsync("""
                INSERT INTO sportsmeet.forum_post_likes (post_id, user_id, created_at)
                SELECT p.id, d.id, now() - random() * interval '3 days'
                  FROM demo_posts p
                  CROSS JOIN LATERAL (
                      SELECT u.id FROM sportsmeet.users u
                       WHERE u.is_demo
                         AND NOT EXISTS (SELECT 1 FROM sportsmeet.forum_post_likes l
                                          WHERE l.post_id = p.id AND l.user_id = u.id)
                       ORDER BY random() LIMIT p.interests) d;
                """, ct);
            await RefreshPostEngagementAsync(ct);

            var events = await CountAsync("SELECT count(*)::int AS \"Value\" FROM demo_events", ct);
            var posts = await CountAsync("SELECT count(*)::int AS \"Value\" FROM demo_posts", ct);
            var accounts = await db.Users.CountAsync(u => u.IsDemo, ct);
            return new DemoEngagementResultDto(accounts, events, posts, eventViews + postViews, eventInterests + postInterests);
        }, ct);

    public Task<DemoEngagementResultDto> RemoveAsync(CancellationToken ct = default) =>
        InTransactionAsync(async () =>
        {
            var events = await CountAsync("""
                SELECT count(DISTINCT event_id)::int AS "Value" FROM (
                    SELECT event_id FROM sportsmeet.event_views WHERE is_demo
                    UNION SELECT event_id FROM sportsmeet.event_interests
                     WHERE user_id IN (SELECT id FROM sportsmeet.users WHERE is_demo)) x
                """, ct);
            var posts = await CountAsync("""
                SELECT count(DISTINCT post_id)::int AS "Value" FROM (
                    SELECT post_id FROM sportsmeet.forum_post_views WHERE is_demo
                    UNION SELECT post_id FROM sportsmeet.forum_post_likes
                     WHERE user_id IN (SELECT id FROM sportsmeet.users WHERE is_demo)) x
                """, ct);

            // The post counter first, from the rows about to go, so it drops by exactly
            // what the tool added.
            await db.Database.ExecuteSqlRawAsync("""
                UPDATE sportsmeet.forum_posts f SET view_count = greatest(f.view_count - c.n, 0)
                  FROM (SELECT post_id, count(*)::int AS n FROM sportsmeet.forum_post_views
                         WHERE is_demo GROUP BY post_id) c
                 WHERE f.id = c.post_id;
                """, ct);
            var views = await db.Database.ExecuteSqlRawAsync(
                "DELETE FROM sportsmeet.event_views WHERE is_demo;", ct);
            views += await db.Database.ExecuteSqlRawAsync(
                "DELETE FROM sportsmeet.forum_post_views WHERE is_demo;", ct);
            var interests = await db.Database.ExecuteSqlRawAsync("""
                DELETE FROM sportsmeet.event_interests
                 WHERE user_id IN (SELECT id FROM sportsmeet.users WHERE is_demo);
                """, ct);
            interests += await db.Database.ExecuteSqlRawAsync("""
                DELETE FROM sportsmeet.forum_post_likes
                 WHERE user_id IN (SELECT id FROM sportsmeet.users WHERE is_demo);
                """, ct);
            await RefreshPostEngagementAsync(ct);

            var accounts = await db.Users.CountAsync(u => u.IsDemo, ct);
            return new DemoEngagementResultDto(accounts, events, posts, views, interests);
        }, ct);

    /// <summary>Every post's like count recounted from its rows, and its hot score
    /// recomputed with ForumRanking.HotScore's formula (as RefreshEngagementAsync does
    /// for one post), so the Hot feed reflects the change.</summary>
    private Task RefreshPostEngagementAsync(CancellationToken ct) =>
        db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE sportsmeet.forum_posts p
               SET like_count = c.likes,
                   hot_score = round(
                       log(greatest(c.likes + 2 * p.comment_count, 1)::numeric)
                       + floor(extract(epoch FROM p.created_at))::numeric / {ForumRanking.DecaySeconds}::numeric,
                       7)::double precision
              FROM (SELECT f.id,
                           (SELECT count(*)::int FROM sportsmeet.forum_post_likes l WHERE l.post_id = f.id) AS likes
                      FROM sportsmeet.forum_posts f) c
             WHERE p.id = c.id AND p.like_count IS DISTINCT FROM c.likes;
            """, ct);

    private async Task<int> CountAsync(string sql, CancellationToken ct) =>
        (await db.Database.SqlQueryRaw<int>(sql).ToListAsync(ct)).Single();

    private Task<T> InTransactionAsync<T>(Func<Task<T>> work, CancellationToken ct) =>
        db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            var result = await work();
            await tx.CommitAsync(ct);
            return result;
        });
}
