using Microsoft.EntityFrameworkCore;
using SportMeet.Application.Activity;
using SportMeet.Application.Common;
using SportMeet.Domain.Entities;
using SportMeet.Domain.Enums;
using SportMeet.Infrastructure.Persistence;

namespace SportMeet.Import.Tests;

/// <summary>The Likes / New Followers / Comments tabs: read state, unread counts, and
/// anonymous commenters staying anonymous.</summary>
public sealed class ActivityServiceTests
{
    private static readonly Guid Me = Guid.Parse("00000000-0000-0000-0000-0000000000a1");
    private static readonly ActivityActorDto Priya = new(Guid.NewGuid(), "Priya Raman", null, UserRole.Member);

    [Fact]
    public async Task Items_carry_their_read_state_and_counts_only_count_unread()
    {
        var repo = new FakeActivity();
        repo.Add(Row("a", ActivityKind.Likes));
        repo.Add(Row("b", ActivityKind.Likes));
        repo.Add(Row("c", ActivityKind.Follows));
        var service = new ActivityService(repo, new FakeUser(Me));

        await service.MarkReadAsync(new(["a"]));

        var likes = await service.ListAsync(ActivityKind.Likes);
        Assert.Equal([true, false], likes.Select(l => l.IsRead));
        Assert.Equal(new ActivityCountsDto(1, 1, 0), await service.UnreadCountsAsync());
    }

    [Fact]
    public async Task Anonymous_commenters_stay_anonymous()
    {
        var repo = new FakeActivity();
        repo.Add(Row("k", ActivityKind.Comments) with { ActorIsAnonymous = true });

        var item = Assert.Single(await new ActivityService(repo, new FakeUser(Me)).ListAsync(ActivityKind.Comments));
        Assert.Null(item.Actor);
    }

    [Fact]
    public async Task Marking_read_ignores_blank_and_oversized_keys_and_caps_the_batch()
    {
        var repo = new FakeActivity();
        var service = new ActivityService(repo, new FakeUser(Me));

        await service.MarkReadAsync(new(["", "  ", new string('x', ActivityService.MaxKeyLength + 1), "ok", "ok"]));
        Assert.Equal(["ok"], repo.Read);

        var tooMany = Enumerable.Range(0, ActivityService.MaxKeysPerRead + 1).Select(i => $"k{i}").ToList();
        await Assert.ThrowsAsync<DomainRuleException>(() => service.MarkReadAsync(new(tooMany)));
    }

    [Fact]
    public async Task Signed_out_callers_get_nothing()
    {
        await Assert.ThrowsAsync<DomainRuleException>(() => new ActivityService(new FakeActivity()).ListAsync(ActivityKind.Likes));
    }

    [Fact]
    public void Keys_change_when_the_same_person_acts_again()
    {
        var follower = Guid.NewGuid();
        Assert.NotEqual(
            ActivityKeys.Follow(follower, DateTimeOffset.UnixEpoch),
            ActivityKeys.Follow(follower, DateTimeOffset.UnixEpoch.AddMinutes(1)));
    }

    private static ActivityRow Row(string key, ActivityKind kind) =>
        new(key, kind, Priya, false, "did something", "Title", null, "/x", DateTimeOffset.UtcNow);

    private sealed class FakeActivity : IActivityRepository
    {
        private readonly List<ActivityRow> _rows = [];
        public List<string> Read { get; } = [];

        public void Add(ActivityRow row) => _rows.Add(row);

        public Task<IReadOnlyList<ActivityRow>> ListAsync(
            Guid userId, ActivityKind kind, DateTimeOffset since, int take, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<ActivityRow>>(_rows.Where(r => r.Kind == kind).Take(take).ToList());

        public Task<IReadOnlySet<string>> ListReadAsync(Guid userId, IReadOnlyCollection<string> keys, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlySet<string>>(Read.Where(keys.Contains).ToHashSet());

        public Task MarkReadAsync(Guid userId, IReadOnlyCollection<string> keys, DateTimeOffset at, CancellationToken ct = default)
        {
            Read.AddRange(keys.Where(k => !Read.Contains(k)));
            return Task.CompletedTask;
        }
    }

    private sealed class FakeUser(Guid id) : ICurrentUser
    {
        public Guid? UserId => id;
        public bool IsDemo => false;
        public bool IsAdmin => false;
        public bool CanPublishPublicEvents => false;
    }
}

/// <summary>The activity queries against the real schema: what reaches whom, and what
/// never does (your own actions, automatic interests). Skipped without a local DB.</summary>
public sealed class ActivitySqlTests
{
    private const string ConnectionString =
        "Host=localhost;Port=5432;Database=sportmeet;Username=sportmeet;Password=sportmeet";

    private static readonly Lazy<bool> Usable = new(() =>
    {
        try
        {
            using var db = Open();
            _ = db.ActivityReads.AsNoTracking().Count();
            return db.ForumCategories.AsNoTracking().Any();
        }
        catch (Exception)
        {
            return false;
        }
    });

    private static AppDbContext Open() => new(new DbContextOptionsBuilder<AppDbContext>()
        .UseNpgsql(ConnectionString, npgsql => npgsql.EnableRetryOnFailure(maxRetryCount: 3))
        .UseSnakeCaseNamingConvention()
        .Options);

    [Fact]
    public async Task Each_tab_shows_other_peoples_actions_on_my_things_only()
    {
        if (!Usable.Value) return;
        await using var db = Open();
        var (me, priya, sam) = (await UserAsync(db), await UserAsync(db), await UserAsync(db));
        var now = DateTimeOffset.UtcNow;
        var category = await db.ForumCategories.AsNoTracking().Select(c => c.Id).FirstAsync();
        var myPost = new ForumPost { Id = Guid.NewGuid(), AuthorUserId = me, CategoryId = category, Title = "Mine", Content = "x", CreatedAt = now };
        var theirPost = new ForumPost { Id = Guid.NewGuid(), AuthorUserId = priya, CategoryId = category, Title = "Theirs", Content = "y", CreatedAt = now };
        db.ForumPosts.AddRange(myPost, theirPost);
        await db.SaveChangesAsync();

        var myComment = new ForumComment { Id = Guid.NewGuid(), PostId = theirPost.Id, AuthorUserId = me, Content = "my comment", CreatedAt = now };
        db.ForumComments.Add(myComment);
        await db.SaveChangesAsync();
        db.ForumPostLikes.AddRange(
            new ForumPostLike { PostId = myPost.Id, UserId = priya, CreatedAt = now },
            new ForumPostLike { PostId = myPost.Id, UserId = me, CreatedAt = now },        // my own: excluded
            new ForumPostLike { PostId = theirPost.Id, UserId = sam, CreatedAt = now });   // not mine: excluded
        db.UserFollows.Add(new UserFollow { FollowerUserId = sam, FollowedUserId = me, CreatedAt = now });
        db.ForumComments.AddRange(
            new ForumComment { Id = Guid.NewGuid(), PostId = myPost.Id, AuthorUserId = priya, Content = "on your post", CreatedAt = now },
            new ForumComment { Id = Guid.NewGuid(), PostId = theirPost.Id, AuthorUserId = sam, ParentCommentId = myComment.Id, Content = "reply to you", IsAnonymous = true, CreatedAt = now },
            new ForumComment { Id = Guid.NewGuid(), PostId = myPost.Id, AuthorUserId = me, Content = "my own", CreatedAt = now });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var repo = new ActivityRepository(db);
        try
        {
            var since = now.AddDays(-1);
            var likes = await repo.ListAsync(me, ActivityKind.Likes, since, 50);
            Assert.Equal([priya], likes.Select(l => l.Actor.Id));

            var follows = await repo.ListAsync(me, ActivityKind.Follows, since, 50);
            Assert.Equal([sam], follows.Select(f => f.Actor.Id));

            var comments = await repo.ListAsync(me, ActivityKind.Comments, since, 50);
            Assert.Equal(2, comments.Count);
            Assert.Contains(comments, c => c.Action == "commented on your post" && c.Actor.Id == priya);
            Assert.Contains(comments, c => c.Action == "replied to your comment" && c.ActorIsAnonymous);

            // Priya sees Sam's like on her post, and nothing of mine: the like she gave
            // me is my activity, not hers.
            var priyasLikes = await repo.ListAsync(priya, ActivityKind.Likes, since, 50);
            Assert.Equal([sam], priyasLikes.Select(l => l.Actor.Id));
            Assert.All(priyasLikes, l => Assert.Equal($"/forum/{theirPost.Id}", l.Href));

            await repo.MarkReadAsync(me, [likes[0].Key], now);
            await repo.MarkReadAsync(me, [likes[0].Key], now); // idempotent
            Assert.Equal([likes[0].Key], await repo.ListReadAsync(me, [likes[0].Key, "other"]));
        }
        finally
        {
            await db.ForumPosts.Where(p => p.Id == myPost.Id || p.Id == theirPost.Id).ExecuteDeleteAsync();
            await db.Users.Where(u => u.Id == me || u.Id == priya || u.Id == sam).ExecuteDeleteAsync();
        }
    }

    private static async Task<Guid> UserAsync(AppDbContext db)
    {
        var id = Guid.NewGuid();
        db.Users.Add(new User { Id = id, Name = $"act-{id:N}"[..14], CreatedAt = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        return id;
    }
}
