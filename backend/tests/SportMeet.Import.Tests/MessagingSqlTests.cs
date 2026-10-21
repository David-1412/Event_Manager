using Microsoft.EntityFrameworkCore;
using SportMeet.Application.Messaging;
using SportMeet.Domain.Entities;
using SportMeet.Infrastructure.Persistence;

namespace SportMeet.Import.Tests;

/// <summary>
/// Direct-message isolation and pair ordering against the real Postgres schema.
///
/// The service tests prove the rules with an in-memory store; these prove the SQL
/// actually filters by participant, and that <see cref="Conversation.Order"/> agrees
/// with the database's own uuid ordering - the CHECK user_a_id &lt; user_b_id would
/// reject a row the two disagreed about. Skipped (passes vacuously) when no local
/// database with the schema is reachable, as FollowingFeedSqlTests does.
/// </summary>
public sealed class MessagingSqlTests
{
    private const string ConnectionString =
        "Host=localhost;Port=5432;Database=sportmeet;Username=sportmeet;Password=sportmeet";

    private static readonly Lazy<bool> Usable = new(() =>
    {
        try
        {
            using var db = Open();
            _ = db.Conversations.AsNoTracking().Count();
            return true;
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
    public async Task Ordered_pairs_always_satisfy_the_database_check()
    {
        if (!Usable.Value) return;
        await using var db = Open();
        var users = await SeedUsersAsync(db, 12);
        var repo = new MessagingRepository(db);
        try
        {
            // Every pair of 12 random ids, given both ways round: 66 conversations, each
            // must insert without violating user_a_id < user_b_id, and the reversed call
            // must find the same row rather than a second one.
            for (var i = 0; i < users.Count; i++)
            for (var j = i + 1; j < users.Count; j++)
            {
                var (a, b) = Conversation.Order(users[j], users[i]);
                var first = await repo.GetOrCreateAsync(a, b, DateTimeOffset.UtcNow);
                var (a2, b2) = Conversation.Order(users[i], users[j]);
                Assert.Equal(first, await repo.GetOrCreateAsync(a2, b2, DateTimeOffset.UtcNow));
            }
            Assert.Equal(66, await db.Conversations.CountAsync(c => users.Contains(c.UserAId)));
        }
        finally
        {
            await CleanupAsync(db, users);
        }
    }

    [Fact]
    public async Task Queries_never_return_another_pairs_conversation_or_messages()
    {
        if (!Usable.Value) return;
        await using var db = Open();
        var users = await SeedUsersAsync(db, 3);
        var (alice, bob, eve) = (users[0], users[1], users[2]);
        var repo = new MessagingRepository(db);
        try
        {
            var (a, b) = Conversation.Order(alice, bob);
            var id = await repo.GetOrCreateAsync(a, b, DateTimeOffset.UtcNow);
            await repo.AddMessageAsync(id, alice, "private", DateTimeOffset.UtcNow);

            Assert.Null(await repo.GetForUserAsync(id, eve));
            Assert.Empty(await repo.ListMessagesAsync(id, eve, null, 50));
            Assert.Empty(await repo.ListForUserAsync(eve));
            Assert.Equal(0, await repo.CountUnreadAsync(eve));

            Assert.Single(await repo.ListMessagesAsync(id, bob, null, 50));
            Assert.Equal(1, await repo.CountUnreadAsync(bob));
            Assert.Equal(0, await repo.CountUnreadAsync(alice));

            await repo.MarkReadAsync(id, bob, DateTimeOffset.UtcNow.AddSeconds(1));
            Assert.Equal(0, await repo.CountUnreadAsync(bob));
        }
        finally
        {
            await CleanupAsync(db, users);
        }
    }

    private static async Task<List<Guid>> SeedUsersAsync(AppDbContext db, int count)
    {
        var ids = Enumerable.Range(0, count).Select(_ => Guid.NewGuid()).ToList();
        foreach (var id in ids)
        {
            db.Users.Add(new User { Id = id, Name = $"msg-test-{id:N}"[..20], CreatedAt = DateTimeOffset.UtcNow });
        }
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        return ids;
    }

    private static async Task CleanupAsync(AppDbContext db, List<Guid> users)
    {
        // Conversations and messages cascade from the users.
        await db.Users.Where(u => users.Contains(u.Id)).ExecuteDeleteAsync();
    }
}
