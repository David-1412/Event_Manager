using Microsoft.EntityFrameworkCore;
using SportMeet.Application.Activity;
using SportMeet.Application.Admin;
using SportMeet.Application.Common;
using SportMeet.Domain.Entities;
using SportMeet.Domain.Enums;
using SportMeet.Infrastructure.Persistence;

namespace SportMeet.Import.Tests;

/// <summary>
/// Demo accounts against a real Postgres: their Interested marks and follows never
/// reach another user's activity feed, and they have no public profile. Rows are
/// written inside a transaction that is never committed (see FollowingFeedSqlTests).
/// Returns early (passes) when no database is reachable.
/// </summary>
public sealed class DemoAccountsSqlTests
{
    private const string ConnectionString =
        "Host=localhost;Port=5432;Database=sportmeet;Username=sportmeet;Password=sportmeet";

    [Fact]
    public async Task Demo_accounts_stay_out_of_activity_and_profiles()
    {
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(ConnectionString).UseSnakeCaseNamingConvention().Options);
        try
        {
            await db.Database.BeginTransactionAsync();
        }
        catch (Exception ex) when (ex is Npgsql.NpgsqlException or InvalidOperationException)
        {
            return; // no database here
        }

        var now = DateTimeOffset.UtcNow;
        var host = new User { Id = Guid.NewGuid(), Name = "Demo test host" };
        var real = new User { Id = Guid.NewGuid(), Name = "Demo test real fan" };
        var demo = new User { Id = Guid.NewGuid(), Name = "demo-test", IsDemo = true };
        var evt = new Event
        {
            Id = Guid.NewGuid(), HostId = host.Id, Title = "Demo test event", VenueName = "Test Venue",
            Lat = -37.9, Lng = 145.1, StartAt = now.AddDays(2), EndAt = now.AddDays(2).AddHours(1),
            Status = EventStatus.Scheduled, Visibility = EventVisibility.Public, CreatedAt = now, UpdatedAt = now,
        };
        db.Users.AddRange(host, real, demo);
        db.Events.Add(evt);
        db.EventInterests.AddRange(
            new EventInterest { EventId = evt.Id, UserId = real.Id, CreatedAt = now },
            new EventInterest { EventId = evt.Id, UserId = demo.Id, CreatedAt = now });
        db.UserFollows.Add(new UserFollow { FollowerUserId = demo.Id, FollowedUserId = host.Id, CreatedAt = now });
        await db.SaveChangesAsync();

        var activity = new ActivityRepository(db);
        var likes = await activity.ListAsync(host.Id, ActivityKind.Likes, now.AddDays(-1), 50);
        Assert.Equal([real.Id], likes.Select(l => l.Actor.Id));
        Assert.Empty(await activity.ListAsync(host.Id, ActivityKind.Follows, now.AddDays(-1), 50));

        var profiles = new ProfileRepository(db);
        Assert.Null(await profiles.FindSubjectAsync(demo.Id));
        Assert.NotNull(await profiles.FindSubjectAsync(real.Id));

        // The interest still counts - that is the point of the tool.
        Assert.Equal(2, await db.EventInterests.CountAsync(i => i.EventId == evt.Id));
    }

    [Fact]
    public async Task Only_an_admin_can_use_the_tool()
    {
        var service = new DemoEngagementService(new NoRepository(), new Viewer(isAdmin: false));
        await Assert.ThrowsAsync<NotFoundException>(() => service.AddAsync());
        await Assert.ThrowsAsync<NotFoundException>(() => service.RemoveAsync());
    }

    private sealed class NoRepository : IDemoEngagementRepository
    {
        public Task<int> EnsureDemoAccountsAsync(int count, CancellationToken ct = default) => throw new InvalidOperationException("reached");
        public Task<DemoEngagementResultDto> AddAsync(int minViews, int maxViews, int minInterests, int maxInterests, CancellationToken ct = default) => throw new InvalidOperationException("reached");
        public Task<DemoEngagementResultDto> RemoveAsync(CancellationToken ct = default) => throw new InvalidOperationException("reached");
    }

    private sealed class Viewer(bool isAdmin) : ICurrentUser
    {
        public Guid? UserId => Guid.NewGuid();
        public bool IsDemo => false;
        public bool IsAdmin => isAdmin;
        public bool CanPublishPublicEvents => isAdmin;
    }
}
