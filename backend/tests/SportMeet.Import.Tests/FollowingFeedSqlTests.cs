using Microsoft.EntityFrameworkCore;
using SportMeet.Application.Common;
using SportMeet.Application.Events;
using SportMeet.Application.Notifications;
using SportMeet.Domain.Entities;
using SportMeet.Domain.Enums;
using SportMeet.Infrastructure.Persistence;

namespace SportMeet.Import.Tests;

/// <summary>
/// The Following feed's SQL, executed against a real Postgres.
///
/// <see cref="FollowingFeedTests"/> proves the service asks for one narrowed
/// query. It cannot prove the narrowing <em>translates</em>: the follow
/// condition is an <c>EXISTS</c> over <c>user_follows</c> correlated to the
/// <c>v_event_feed</c> view's <c>host_id</c>, composed with the status
/// exclusion, the visibility rule, the ILIKE search and the Haversine radius.
/// Composition is exactly where the Npgsql provider has broken before — the
/// Activity feed's <c>Concat</c> threw while EF built the query, so the endpoint
/// 500ed before reading a row while every service-level test stayed green.
/// Translation is a property of the provider, so it is tested against the
/// provider.
///
/// Rows are inserted and rolled back rather than committed: the database under
/// test is the shared dev one, and a follow edge left behind would change what
/// the next person's Following tab shows.
/// </summary>
public sealed class FollowingFeedSqlTests
{
    private const string ConnectionString =
        "Host=localhost;Port=5432;Database=sportmeet;Username=sportmeet;Password=sportmeet";

    private static readonly Guid ViewerId = Guid.NewGuid();
    private static readonly Guid FollowedHostId = Guid.NewGuid();
    private static readonly Guid OtherHostId = Guid.NewGuid();
    private static readonly Guid FollowedEventId = Guid.NewGuid();
    private static readonly Guid OtherEventId = Guid.NewGuid();

    /// <summary>Whether a run here can translate anything at all.
    ///
    /// EF Core 8's LINQ interpreter cannot construct
    /// <c>FuncCallInstruction&lt;T0, ReadOnlySpan&lt;string&gt;&gt;</c>, which it
    /// builds for any <c>Contains</c> over a string collection — and the browse
    /// status exclusion is written exactly that way. A machine whose only shared
    /// runtime is .NET 10 rolls forward into that bug and fails on queries no
    /// matter what the code looks like (the five
    /// <c>JoinRequestWireShapeApiTests</c> failures are the same cause).
    /// Detecting it keeps a real regression distinguishable from the
    /// environment.</summary>
    private static readonly Lazy<bool> ProviderUsable = new(() =>
    {
        try
        {
            using var probe = Open();
            _ = probe.Users.AsNoTracking()
                .Where(u => new[] { "Member", "Admin" }.Contains(u.Name)).Count();
            return true;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
        catch (Exception)
        {
            // Unreachable database, missing schema — also "nothing to check here".
            return false;
        }
    });

    private static AppDbContext Open()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(ConnectionString)
            .UseSnakeCaseNamingConvention()
            .Options;

        return new AppDbContext(options);
    }

    /// <summary>A context over the dev database inside an open transaction, or null
    /// when this machine cannot host the check. The caller disposes it without
    /// committing, which is what keeps the shared dev database clean.</summary>
    private static async Task<AppDbContext?> TryOpenSeededAsync()
    {
        if (!ProviderUsable.Value) return null;

        var db = Open();
        try
        {
            await db.Database.BeginTransactionAsync();

            var now = DateTimeOffset.UtcNow;
            db.Users.AddRange(
                new User { Id = ViewerId, Name = "Following Feed Viewer" },
                new User { Id = FollowedHostId, Name = "Following Feed Followed Host" },
                new User { Id = OtherHostId, Name = "Following Feed Other Host" });

            db.Events.AddRange(
                new Event
                {
                    Id = FollowedEventId,
                    HostId = FollowedHostId,
                    Title = "Following Feed Followed Host Event",
                    VenueName = "Test Venue",
                    Lat = -37.8136,
                    Lng = 144.9631,
                    StartAt = now.AddDays(1),
                    EndAt = now.AddDays(1).AddHours(2),
                    Status = EventStatus.Scheduled,
                    Visibility = EventVisibility.Public,
                    CreatedAt = now,
                    UpdatedAt = now,
                },
                new Event
                {
                    Id = OtherEventId,
                    HostId = OtherHostId,
                    Title = "Following Feed Other Host Event",
                    VenueName = "Test Venue",
                    Lat = -37.8136,
                    Lng = 144.9631,
                    StartAt = now.AddDays(2),
                    EndAt = now.AddDays(2).AddHours(2),
                    Status = EventStatus.Scheduled,
                    Visibility = EventVisibility.Public,
                    CreatedAt = now,
                    UpdatedAt = now,
                });

            db.UserFollows.Add(new UserFollow
            {
                FollowerUserId = ViewerId,
                FollowedUserId = FollowedHostId,
                CreatedAt = now,
            });

            // The view is not a table EF knows about, but it reads the rows this
            // same transaction wrote, so no refresh is needed - Postgres sees them.
            await db.SaveChangesAsync();
            return db;
        }
        catch (Exception ex) when (ex is Npgsql.NpgsqlException or InvalidOperationException)
        {
            db.Dispose();
            return null;
        }
    }

    private static EventQueryModel NarrowedBy(Guid viewer) => new() { FollowedByUserId = viewer };

}
