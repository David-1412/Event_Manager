using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using SportMeet.Application.Events;
using SportMeet.Domain.Entities;
using SportMeet.Domain.Enums;
using SportMeet.Infrastructure.Persistence.Config;

namespace SportMeet.Infrastructure.Persistence;

/// <summary>
/// Reads come from v_event_feed so a browse page is one round-trip with the
/// participant count already derived; writes go through the Event entity.
/// </summary>
public sealed class EventRepository(AppDbContext db) : IEventRepository
{
    /// <summary>Melbourne is the whole market here, so a generous box never
    /// truncates a legitimate radius query while staying selective enough for the
    /// (lat, lng) index to matter.</summary>
    private const double DegreesPerKm = 1d / 111.32d;

    public async Task<(List<FeedRow> Rows, int TotalCount)> QueryAsync(EventQueryModel query, CancellationToken ct = default)

    {
        var filtered = ApplyFilters(db.EventFeed.AsNoTracking(), query);

        var totalCount = await filtered.CountAsync(ct);

        IQueryable<VwEventFeed> ordered = query.UseDistance
            ? filtered
                .OrderBy(v => AppDbFunctions.HaversineKm(v.Lat, v.Lng, query.OriginLat!.Value, query.OriginLng!.Value))
                .ThenBy(v => v.StartAt)
            : filtered.OrderBy(v => v.StartAt);

        var page = await ordered
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .ToListAsync(ct);

        return (page.Select(ToRow).ToList(), totalCount);
    }

    public async Task<FeedRow?> FindAsync(Guid id, CancellationToken ct = default)
    {
        var feed = await db.EventFeed.AsNoTracking().FirstOrDefaultAsync(v => v.Id == id, ct);
        return feed is null ? null : ToRow(feed);
    }

    public Task<Sport?> FindSportBySlugAsync(string slug, CancellationToken ct = default)
        => db.Sports.AsNoTracking().FirstOrDefaultAsync(s => s.Slug == slug, ct);

    public async Task<Dictionary<string, Tag>> FindTagsByNamesAsync(IReadOnlyList<string> names, CancellationToken ct = default)
    {
        if (names.Count == 0)
        {
            return new Dictionary<string, Tag>(StringComparer.Ordinal);
        }

        // AsNoTracking: callers attach a Tag by id into event_tags, and a tracked
        // instance here would collide with the one this method goes on to add.
        var found = await db.Tags.AsNoTracking()
            .Where(t => names.Contains(t.Name))
            .ToListAsync(ct);

        return found.ToDictionary(t => t.Name, StringComparer.Ordinal);
    }

    public async Task ReplaceEventTagsAsync(Guid eventId, IReadOnlyList<string> names, DateTimeOffset now, CancellationToken ct = default)
    {
        var current = await db.EventTags.Where(x => x.EventId == eventId).ToListAsync(ct);
        var wanted = names.Distinct(StringComparer.Ordinal).ToList();

        var existingByName = await FindTagsByNamesAsync(wanted, ct);

        var resolved = new List<Tag>(wanted.Count);
        foreach (var name in wanted)
        {
            if (existingByName.TryGetValue(name, out var existing))
            {
                resolved.Add(existing);
                continue;
            }

            // Implicit creation on first use - no curation step, so a new tag costs
            // nothing but typing. Relies on the unique index for the race between two
            // events using the same new tag simultaneously: one insert wins and the
            // other surfaces as a constraint violation rather than a silent duplicate.
            var created = new Tag { Name = name, CreatedAt = now };
            db.Tags.Add(created);
            resolved.Add(created);
        }

        await db.SaveChangesAsync(ct);

        var keepTagIds = new HashSet<int>(resolved.Select(t => t.Id));
        var stale = current.Where(x => !keepTagIds.Contains(x.TagId)).ToList();
        db.EventTags.RemoveRange(stale);

        var existingTagIds = new HashSet<int>(current.Select(x => x.TagId));
        db.EventTags.AddRange(resolved
            .Where(t => !existingTagIds.Contains(t.Id))
            .Select(t => new EventTag { EventId = eventId, TagId = t.Id }));

        await db.SaveChangesAsync(ct);
    }

    public async Task<List<PopularTag>> ListPopularTagsAsync(int limit, CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;

        // Same baseline as ApplyFilters - scheduled, not cancelled, not past - so a
        // chip that appears on the home page always opens a non-empty result set.
        //
        // Grouped into an anonymous type before ordering: projecting straight into
        // the PopularTag record and then OrderBy/ThenBy over its members does not
        // translate (EF cannot see through a positional record constructor in a
        // GroupBy projection and threw "could not be translated" - measured, not
        // guessed). The record is built after the values are in hand.
        var rows = await db.EventTags.AsNoTracking()
            .Where(x => x.Event.Status != EventStatus.Cancelled && x.Event.StartAt >= now)
            .GroupBy(x => x.Tag.Name)
            .Select(g => new { Name = g.Key, Count = g.Count() })
            .OrderByDescending(x => x.Count)
            .ThenBy(x => x.Name)
            .Take(limit)
            .ToListAsync(ct);

        return rows.Select(x => new PopularTag(x.Name, x.Count)).ToList();
    }

    public async Task<List<string>> SuggestTagsAsync(string prefix, int limit, CancellationToken ct = default)
        // Prefix rather than Contains: uses the unique btree on name, and the
        // autocomplete is a prefix tool by design. A substring search would need a
        // trigram index for a case nobody has asked for yet.
        => await db.Tags.AsNoTracking()
            .Where(t => t.Name.StartsWith(prefix))
            .OrderBy(t => t.Name)
            .Select(t => t.Name)
            .Take(limit)
            .ToListAsync(ct);


    public async Task AddAsync(Event eventEntity, CancellationToken ct = default)
    {
        db.Events.Add(eventEntity);
        await db.SaveChangesAsync(ct);
    }

    public async Task<List<User>> ListParticipantsAsync(Guid eventId, CancellationToken ct = default)
        => await db.EventParticipants
            .AsNoTracking()
            .Where(p => p.EventId == eventId)
            .OrderBy(p => p.JoinedAt)
            .Select(p => p.User)
            .ToListAsync(ct);

    public async Task<bool> IsParticipantAsync(Guid eventId, Guid userId, CancellationToken ct = default)
        => await db.EventParticipants.AnyAsync(p => p.EventId == eventId && p.UserId == userId, ct);

    public async Task<List<Guid>> ListJoinedEventIdsAsync(Guid userId, CancellationToken ct = default)
        => await db.EventParticipants
            .AsNoTracking()
            .Where(p => p.UserId == userId)
            .Select(p => p.EventId)
            .ToListAsync(ct);

    public async Task<int> CountParticipantsAsync(Guid eventId, CancellationToken ct = default)
        => await db.EventParticipants.CountAsync(p => p.EventId == eventId, ct);

    public async Task RunInTransactionAsync(Func<CancellationToken, Task> action, CancellationToken ct = default)
    {
        // EnableRetryOnFailure wraps execution in NpgsqlRetryingExecutionStrategy,
        // which rejects a manual BeginTransaction unless the whole unit - transaction
        // included - runs inside its ExecuteAsync. A retry re-runs `action` from the
        // start against a fresh transaction, so FOR UPDATE semantics hold either way.
        var strategy = db.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            await using IDbContextTransaction transaction =
                await db.Database.BeginTransactionAsync(ct);
            await action(ct);
            await transaction.CommitAsync(ct);
        });
    }

    public async Task<Event?> FindWithLockAsync(Guid eventId, CancellationToken ct = default)
        // FOR UPDATE has no EF equivalent, so this is raw SQL. The @eventId
        // placeholder is a real DbParameter, never string concatenation. Must be
        // called inside RunInTransactionAsync, or the row lock releases
        // immediately. SELECT * stays correct as the entity gains columns; the
        // model binder maps by name.
        => await db.Events
            .FromSqlRaw(
                $"SELECT * FROM {SportConfiguration.Schema}.events WHERE id = @eventId FOR UPDATE",
                new NpgsqlParameter<Guid>("@eventId", eventId))
            .FirstOrDefaultAsync(ct);

    public async Task AddParticipantAsync(Guid eventId, Guid userId, DateTimeOffset joinedAt, CancellationToken ct = default)
    {
        db.EventParticipants.Add(new EventParticipant
        {
            EventId = eventId,
            UserId = userId,
            JoinedAt = joinedAt,
        });
        await db.SaveChangesAsync(ct);
    }

    public async Task<bool> RemoveParticipantAsync(Guid eventId, Guid userId, DateTimeOffset cancelledAt, CancellationToken ct = default)
    {
        var row = await db.EventParticipants
            .FirstOrDefaultAsync(p => p.EventId == eventId && p.UserId == userId, ct);
        if (row is not null)
        {
            db.EventParticipants.Remove(row);
        }

        var cancelled = false;
        var @event = await db.Events.FirstOrDefaultAsync(e => e.Id == eventId, ct);
        if (@event is not null && @event.HostId == userId && @event.Status == EventStatus.Scheduled)
        {
            @event.Status = EventStatus.Cancelled;
            @event.CancelledAt = cancelledAt;
            cancelled = true;
        }

        if (row is not null || cancelled)
        {
            await db.SaveChangesAsync(ct);
        }

        return cancelled;
    }


    private static IQueryable<VwEventFeed> ApplyFilters(IQueryable<VwEventFeed> source, EventQueryModel query)
    {
        // Cancelled events stay out of browse; the client has no control asking
        // for them, and spec §11 keeps them off the feed.
        source = source.Where(v => v.Status != EventStatus.Cancelled);

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim();

            // Tags joined by OR rather than AND with the text fields: "tennis" must
            // find an event tagged #tennis as readily as one titled "Tennis Social",
            // and a user has no way to know which the host used.
            source = source.Where(v =>
                EF.Functions.ILike(v.Title, $"%{term}%") ||
                EF.Functions.ILike(v.VenueName, $"%{term}%") ||
                EF.Functions.ILike(v.HostName, $"%{term}%") ||
                (v.Tags != null && EF.Functions.ILike(v.Tags, $"%{term}%")));
        }

        if (!string.IsNullOrWhiteSpace(query.TagName))
        {
            var tag = query.TagName;

            // Matched against the comma-joined aggregate with delimiters so #club
            // cannot match an event tagged #nightclub. Start/EndsWith cover the
            // aggregate sitting at either edge of the string.
            source = source.Where(v => v.Tags != null &&
                (v.Tags == tag ||
                 v.Tags.StartsWith(tag + ",") ||
                 v.Tags.EndsWith("," + tag) ||
                 v.Tags.Contains("," + tag + ",")));
        }


        // Always bounded below by now. use-events.ts's fixture mirror treats the
        // filter as an upper bound only, which is why "Today" there can list
        // yesterday's game; the server does not reproduce that.
        var now = DateTimeOffset.UtcNow;
        source = query.DateFilter switch
        {
            EventDateFilter.Today => source.Where(v => v.StartAt >= now && v.StartAt < now.AddDays(1)),
            EventDateFilter.Week => source.Where(v => v.StartAt >= now && v.StartAt < now.AddDays(7)),
            _ => source.Where(v => v.StartAt >= now),
        };

        if (query.RadiusKm is { } radius && query.HasOrigin)
        {
            var originLat = query.OriginLat!.Value;
            var originLng = query.OriginLng!.Value;

            // Bounding box first so the (lat, lng) index narrows the set before
            // the exact great-circle test. The longitude tolerance must widen with
            // latitude or a radius query far from the equator silently drops rows.
            var latDelta = radius * DegreesPerKm;
            var lonScale = Math.Max(0.01, Math.Cos(originLat * Math.PI / 180d));
            var lngDelta = radius * DegreesPerKm / lonScale;

            source = source
                .Where(v =>
                    v.Lat >= originLat - latDelta &&
                    v.Lat <= originLat + latDelta &&
                    v.Lng >= originLng - lngDelta &&
                    v.Lng <= originLng + lngDelta)
                .Where(v => AppDbFunctions.HaversineKm(v.Lat, v.Lng, originLat, originLng) <= radius);
        }

        return source;
    }

    private static FeedRow ToRow(VwEventFeed v) => new(
        ToEvent(v),
        SplitTags(v.Tags),
        v.SportIcon,
        v.HostName,
        v.HostPhotoUrl,
        v.CurrentParticipants);

    /// <summary>The view returns tags as one comma-joined string (see the
    /// string_agg note in Init_Views); empty list rather than null when the event
    /// has none, so the DTO never needs a null branch.</summary>
    private static List<string> SplitTags(string? tags) => string.IsNullOrEmpty(tags)
        ? []
        : tags.Split(',', StringSplitOptions.RemoveEmptyEntries).ToList();


    /// <summary>
    /// The view is not a tracked entity, so the Event it describes is rebuilt by
    /// hand rather than attached to the change tracker. One place, so a new view
    /// column cannot be forgotten in two.
    /// </summary>
    private static Event ToEvent(VwEventFeed v) => new()
    {
        Id = v.Id,
        HostId = v.HostId,
        Title = v.Title,
        Description = v.Description,
        SportId = v.SportId,
        VenueName = v.VenueName,
        Address = v.Address,
        PlaceId = v.PlaceId,
        Lat = v.Lat,
        Lng = v.Lng,
        Timezone = v.Timezone,
        StartAt = v.StartAt,
        EndAt = v.EndAt,
        MaxParticipants = v.MaxParticipants,
        SkillLevel = v.SkillLevel,
        Cost = v.Cost,
        Status = v.Status,
        CancelledAt = v.CancelledAt,
        CreatedAt = v.CreatedAt,
        UpdatedAt = v.UpdatedAt,
    };
}
