using Microsoft.EntityFrameworkCore;
using SportMeet.Domain.Entities;
using SportMeet.Domain.Enums;
using SportMeet.Infrastructure.Persistence;

namespace SportMeet.Import.Tests;

/// <summary>
/// Recurring series against a real Postgres: the Add_Recurring_Events columns read
/// back through v_event_feed, the upper-case frequency round-trips, and a review
/// decision moves the rest of the series. Rows are written inside a transaction that
/// is never committed, as in <see cref="FollowingFeedSqlTests"/>, so the shared dev
/// database is left as it was. Returns early (passes) when no database is reachable.
/// </summary>
public sealed class RecurringEventsSqlTests
{
    private const string ConnectionString =
        "Host=localhost;Port=5432;Database=sportmeet;Username=sportmeet;Password=sportmeet";

    private static AppDbContext Open() => new(new DbContextOptionsBuilder<AppDbContext>()
        .UseNpgsql(ConnectionString)
        .UseSnakeCaseNamingConvention()
        .Options);

    private static Event Session(Guid hostId, Guid seriesId, Guid id, DateTimeOffset start, DateTimeOffset now) => new()
    {
        Id = id,
        HostId = hostId,
        Title = "Recurring SQL test session",
        VenueName = "Test Venue",
        Lat = -37.91,
        Lng = 145.13,
        StartAt = start,
        EndAt = start.AddHours(2),
        Status = EventStatus.PendingReview,
        Visibility = EventVisibility.Public,
        CreatedAt = now,
        UpdatedAt = now,
        IsRecurring = true,
        SeriesId = seriesId,
        RecurrenceFrequency = RecurrenceFrequency.Biweekly,
        RecurrenceDays = "MON,WED",
        RecurrenceCount = 3,
    };

    [Fact]
    public async Task Series_columns_read_back_and_a_decision_moves_the_whole_series()
    {
        await using var db = Open();
        try
        {
            await db.Database.BeginTransactionAsync();
        }
        catch (Exception ex) when (ex is Npgsql.NpgsqlException or InvalidOperationException)
        {
            return; // no database here
        }

        var now = DateTimeOffset.UtcNow;
        var hostId = Guid.NewGuid();
        var ids = new[] { Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() };
        var oneOffId = Guid.NewGuid();

        db.Users.Add(new User { Id = hostId, Name = "Recurring SQL Test Host" });
        for (var i = 0; i < ids.Length; i++)
            db.Events.Add(Session(hostId, ids[0], ids[i], now.AddDays(7 + 14 * i), now));
        db.Events.Add(new Event
        {
            Id = oneOffId,
            HostId = hostId,
            Title = "Recurring SQL test one-off",
            VenueName = "Test Venue",
            Lat = -37.91,
            Lng = 145.13,
            StartAt = now.AddDays(3),
            EndAt = now.AddDays(3).AddHours(1),
            Status = EventStatus.PendingReview,
            Visibility = EventVisibility.Public,
            CreatedAt = now,
            UpdatedAt = now,
        });
        await db.SaveChangesAsync();

        var stored = await db.Database
            .SqlQueryRaw<string>("SELECT recurrence_frequency AS \"Value\" FROM sportsmeet.events WHERE id = {0}", ids[1])
            .SingleAsync();
        Assert.Equal("BIWEEKLY", stored);

        var repo = new EventRepository(db);
        var row = await repo.FindAsync(ids[1]);
        Assert.NotNull(row);
        Assert.True(row.Event.IsRecurring);
        Assert.Equal(ids[0], row.Event.SeriesId);
        Assert.Equal(RecurrenceFrequency.Biweekly, row.Event.RecurrenceFrequency);
        Assert.Equal("MON,WED", row.Event.RecurrenceDays);
        Assert.Equal(3, row.Event.RecurrenceCount);

        // The admin decided on the first session; the other two follow, the one-off
        // (no series) does not.
        await repo.TrySetReviewStatusAsync(ids[0], EventStatus.PendingReview, EventStatus.Published, now);
        var moved = await repo.SetSeriesReviewStatusAsync(ids[0], ids[0], EventStatus.PendingReview, EventStatus.Published, now);
        Assert.Equal(2, moved);

        var statuses = await db.Events.AsNoTracking()
            .Where(e => e.HostId == hostId)
            .ToDictionaryAsync(e => e.Id, e => e.Status);
        Assert.All(ids, id => Assert.Equal(EventStatus.Published, statuses[id]));
        Assert.Equal(EventStatus.PendingReview, statuses[oneOffId]);
    }
}
