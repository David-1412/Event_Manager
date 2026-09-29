using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SportMeet.Domain.Entities;
using SportMeet.Domain.Enums;
using SportMeet.Infrastructure.Identity;
using SportMeet.Infrastructure.Persistence;

namespace SportMeet.Infrastructure.Seeding;

/// <summary>
/// Puts the sport vocabulary, the demo host and a set of upcoming demo events
/// into the database.
///
/// Why the demo events are relative to "now": the frontend builds its fixtures
/// with at(daysFromNow, hour) and the browse filter excludes past events, so a
/// hand-written static seed ages out and the UI then shows an empty list that
/// reads as "the API is broken" rather than "this data is stale". Seeding at
/// startup from the same clock the API filters with keeps the two in step.
///
/// Idempotent by construction: every insert checks for its own row, so a
/// container restart adds nothing and re-running is safe.
/// </summary>
public sealed class DemoSeeder(AppDbContext db, IOptions<DemoUserOptions> options, ILogger<DemoSeeder> logger)
{
    public async Task SeedAsync(CancellationToken ct = default)
    {
        await SeedSportsAsync(ct);
        await SeedDemoHostAsync(ct);
        await SeedDemoParticipantAsync(ct);
        await SeedDemoEventsAsync(ct);

        logger.LogInformation(
            "Demo seed complete: {Sports} sports, {Events} events in scope.",
            await db.Sports.CountAsync(ct),
            await db.Events.CountAsync(ct));
    }

    private async Task SeedSportsAsync(CancellationToken ct)
    {
        var existing = await db.Sports.Select(s => s.Slug).ToListAsync(ct);

        var missing = DemoSeed.Sports.Where(s => !existing.Contains(s.Slug, StringComparer.Ordinal)).ToList();
        if (missing.Count == 0)
        {
            return;
        }

        db.Sports.AddRange(missing);
        await db.SaveChangesAsync(ct);
    }

    private async Task SeedDemoHostAsync(CancellationToken ct)
    {
        var id = options.Value.SeedHostUserId;
        if (await db.Users.AnyAsync(u => u.Id == id, ct))
        {
            return;
        }

        db.Users.Add(new User
        {
            Id = id,
            Name = options.Value.SeedHostName,
            Email = "demo@sportmeet.local",
        });
        await db.SaveChangesAsync(ct);
    }

    /// <summary>The second demo user: hosts nothing, so the current-user identity
    /// (which defaults to this id) can genuinely join and leave the demo events.
    /// Its id is <see cref="DemoUserOptions.AsUserId"/>, not
    /// <see cref="SeedHostUserId"/>, precisely so the viewer is never the host.</summary>
    private async Task SeedDemoParticipantAsync(CancellationToken ct)
    {
        var id = options.Value.AsUserId;
        if (id is null || id == Guid.Empty)
        {
            return;
        }

        if (await db.Users.AnyAsync(u => u.Id == id, ct))
        {
            return;
        }

        db.Users.Add(new User
        {
            Id = id.Value,
            Name = options.Value.ParticipantName,
            Email = "participant@sportmeet.local",
        });
        await db.SaveChangesAsync(ct);
    }

    private async Task SeedDemoEventsAsync(CancellationToken ct)
    {
        var hostId = options.Value.SeedHostUserId;
        if (hostId == Guid.Empty)
        {
            logger.LogWarning("Demo:SeedHostUserId is unset; skipping demo events.");
            return;
        }

        var sportsBySlug = await db.Sports.ToDictionaryAsync(s => s.Slug, ct);
        if (sportsBySlug.Count == 0)
        {
            logger.LogWarning("No sports seeded; cannot create demo events.");
            return;
        }

        var existingIds = await db.Events.Select(e => e.Id).ToListAsync(ct);
        var now = DateTimeOffset.UtcNow;

        var additions = new List<Event>();
        foreach (var seed in DemoSeed.Events)
        {
            if (existingIds.Contains(seed.Id))
            {
                continue;
            }

            // Unknown sport is no longer fatal to the row: sport_id only supplies
            // the emoji now, so the event is still real without one.
            sportsBySlug.TryGetValue(seed.SportSlug, out var sport);

            additions.Add(new Event
            {
                Id = seed.Id,
                HostId = hostId,
                Title = seed.Title,
                Description = seed.Description,
                SportId = sport?.Id,
                VenueName = seed.Venue,
                Address = seed.Address,
                Lat = seed.Lat,
                Lng = seed.Lng,
                Timezone = "Australia/Melbourne",
                StartAt = seed.StartFromNow(now),
                EndAt = seed.EndFromNow(now),
                MaxParticipants = seed.Max,
                SkillLevel = seed.Skill,
                Cost = seed.Cost,
                Status = EventStatus.Scheduled,
                CreatedAt = now,
                UpdatedAt = now,
                // No EventTags on purpose: tags are user vocabulary and nobody has
                // typed any into a fresh database. Consequence, accepted
                // deliberately - GET /api/tags/popular returns [] and the home
                // page's popular-tag strip renders its empty state until tags are
                // added by hand through the create form.
            });
        }


        if (additions.Count > 0)
        {
            db.Events.AddRange(additions);
            await db.SaveChangesAsync(ct);
        }
    }
}
