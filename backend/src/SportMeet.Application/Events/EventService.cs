using SportMeet.Application.Common;
using SportMeet.Domain.Entities;
using SportMeet.Domain.Enums;

namespace SportMeet.Application.Events;

/// <summary>
/// Browse, detail and create. Contains no knowledge of HTTP, EF or Firebase:
/// the caller identity arrives as ICurrentUser and persistence as
/// IEventRepository, so the demo-user arrangement is configuration the Api
/// supplies rather than a rule baked in here.
/// </summary>
public sealed class EventService(IEventRepository events, ICurrentUser currentUser) : IEventService
{
    public async Task<PagedResult<EventListItemDto>> ListAsync(EventQueryModel query, CancellationToken ct = default)
    {
        var (rows, totalCount) = await events.QueryAsync(query, ct);

        // Distance is only computed when an origin exists; null here is what
        // tells the card to hide the distance row.
        var items = rows
            .Select(row => ToListItem(row.Event, row, ComputeDistanceKm(row.Event, query)))
            .ToList();

        return new PagedResult<EventListItemDto>(items, query.Page, query.PageSize, totalCount);
    }

    public async Task<EventDetailDto> GetAsync(Guid id, CancellationToken ct = default)
    {
        var row = await events.FindAsync(id, ct)
            ?? throw new NotFoundException("Event", id);

        var participants = await events.ListParticipantsAsync(id, ct);

        return new EventDetailDto
        {
            Id = row.Event.Id,
            Title = row.Event.Title,
            Tags = row.Tags,
            SportIcon = row.SportIcon,
            SkillLevel = row.Event.SkillLevel,
            StartAt = row.Event.StartAt,
            EndAt = row.Event.EndAt,
            Timezone = row.Event.Timezone,
            VenueName = row.Event.VenueName,
            Address = row.Event.Address,
            Latitude = row.Event.Lat,
            Longitude = row.Event.Lng,
            Cost = row.Event.Cost,
            MaxParticipants = row.Event.MaxParticipants,
            ParticipantCount = row.ParticipantCount,
            IsCancelled = row.Event.Status == EventStatus.Cancelled,
            DistanceKm = null,
            Description = row.Event.Description,
            Host = new ParticipantDto(row.Event.HostId, row.HostName, row.HostPhotoUrl),

            // Both relations are evaluated against ICurrentUser, never hardcoded.
            // In Milestone 1 that identity comes from Demo:AsUserId; when a
            // verified token supplies it these lines do not change.
            IsHost = currentUser.UserId is { } host && host == row.Event.HostId,
            IsJoined = currentUser.UserId is { } joiner && await events.IsParticipantAsync(id, joiner, ct),
            Participants = participants
                .Select(u => new ParticipantDto(u.Id, u.Name, u.PhotoUrl))
                .ToList(),
            CancelledAt = row.Event.CancelledAt,
        };
    }

    public async Task<EventDetailDto> CreateAsync(CreateEventDto dto, CancellationToken ct = default)
    {
        // Bounds already passed CreateEventDtoValidator. Tags are normalized here
        // rather than in the validator so the validator can reject a malformed tag
        // while this decides what gets stored: deduped, capped, lowercase.
        var tagNames = TagNormalizer.NormalizeMany(dto.Tags ?? []);

        var hostId = currentUser.UserId
            ?? throw new DomainRuleException("You must be signed in to create an event.");

        if (!Event.HasValidTimeRange(dto.StartAt!.Value, dto.EndAt!.Value))
        {
            throw new DomainRuleException("Finish must be after the start");
        }

        var now = DateTimeOffset.UtcNow;
        var entity = new Event
        {
            Id = Guid.NewGuid(),
            HostId = hostId,
            Title = dto.Title!.Trim(),
            Description = string.IsNullOrWhiteSpace(dto.Description) ? null : dto.Description.Trim(),
            // Null: tags replaced the sport vocabulary and the create form sends no
            // sport, so a new event renders without an emoji.
            SportId = null,
            VenueName = dto.VenueName!.Trim(),
            Address = dto.Address!.Trim(),
            Lat = dto.Latitude!.Value,
            Lng = dto.Longitude!.Value,
            Timezone = string.IsNullOrWhiteSpace(dto.Timezone) ? "Australia/Melbourne" : dto.Timezone,
            StartAt = dto.StartAt.Value,
            EndAt = dto.EndAt.Value,
            MaxParticipants = dto.MaxParticipants!.Value,
            SkillLevel = dto.SkillLevel,
            Cost = dto.Cost,
            Status = EventStatus.Scheduled,
        };

        await events.AddAsync(entity, ct);

        // Separate statement rather than building the navigation in memory: the
        // event row must exist first, and letting EF walk the graph would have it
        // insert tag rows that are already in the table.
        await events.ReplaceEventTagsAsync(entity.Id, tagNames, now, ct);

        // Re-read through the same path as GET so the 201 body is provably what
        // the detail page will render. Assembling it from the request instead is
        // how a field that silently fails to persist stays hidden.
        return await GetAsync(entity.Id, ct);
    }

    public async Task<IReadOnlyList<PopularTag>> ListPopularTagsAsync(int limit = 12, CancellationToken ct = default)
        => await events.ListPopularTagsAsync(Math.Clamp(limit, 1, 50), ct);

    public async Task<IReadOnlyList<string>> SuggestTagsAsync(string? prefix, int limit = 8, CancellationToken ct = default)
    {
        var normalized = TagNormalizer.Normalize(prefix);

        // An unnormalizable prefix ("#") has no possible match, and answering with
        // the whole tag list would read as a bug rather than an empty result.
        if (normalized is null)
        {
            return [];
        }

        return await events.SuggestTagsAsync(normalized, Math.Clamp(limit, 1, 25), ct);
    }

    public async Task<EventDetailDto> JoinAsync(Guid eventId, CancellationToken ct = default)
    {
        var userId = currentUser.UserId
            ?? throw new DomainRuleException("You must be signed in to join an event.");

        // Lock, count and insert in one transaction: two racing joins cannot both
        // take the last spot, because the second waiter sees the first one's row
        // once the lock transfers to it (IMPLEMENTATION_PLAN.md §2).
        await events.RunInTransactionAsync(async inner =>
        {
            var locked = await events.FindWithLockAsync(eventId, inner)
                ?? throw new NotFoundException("Event", eventId, eventId);

            if (locked.Status != EventStatus.Scheduled)
            {
                throw new DomainRuleException("This event is no longer open for joining.");
            }

            if (locked.HostId == userId)
            {
                throw new DomainRuleException("You cannot join an event you are hosting.");
            }

            if (locked.StartAt <= DateTimeOffset.UtcNow)
            {
                throw new DomainRuleException("This event has already started.");
            }

            if (await events.IsParticipantAsync(eventId, userId, inner))
            {
                // Re-join or a retried request: converge without touching counts.
                return;
            }

            var current = await events.CountParticipantsAsync(eventId, inner);
            if (current >= locked.MaxParticipants)
            {
                throw new EventFullException(eventId);
            }

            await events.AddParticipantAsync(eventId, userId, DateTimeOffset.UtcNow, inner);
        }, ct);

        return await GetAsync(eventId, ct);
    }

    public async Task<EventDetailDto> LeaveAsync(Guid eventId, CancellationToken ct = default)
    {
        var userId = currentUser.UserId
            ?? throw new DomainRuleException("You must be signed in to leave an event.");

        // Existence check first so an unknown id is a 404 rather than a silent
        // no-op; the write itself carries the host-cancel rule.
        _ = await events.FindAsync(eventId, ct)
            ?? throw new NotFoundException("Event", eventId, eventId);

        await events.RemoveParticipantAsync(eventId, userId, DateTimeOffset.UtcNow, ct);

        return await GetAsync(eventId, ct);
    }

    public async Task<IReadOnlyList<Guid>> ListMyJoinedAsync(CancellationToken ct = default)
    {
        // No identity configured (no auth yet) is not an error here — an empty list
        // just means My events shows only the Interested set.
        var userId = currentUser.UserId;
        if (userId is null)
        {
            return [];
        }

        return await events.ListJoinedEventIdsAsync(userId.Value, ct);
    }

    private static EventListItemDto ToListItem(Event e, FeedRow row, double? distanceKm) => new()

    {
        Id = e.Id,
        Title = e.Title,
        Tags = row.Tags,
        SportIcon = row.SportIcon,
        SkillLevel = e.SkillLevel,
        StartAt = e.StartAt,
        EndAt = e.EndAt,
        Timezone = e.Timezone,
        VenueName = e.VenueName,
        Address = e.Address,
        Latitude = e.Lat,
        Longitude = e.Lng,
        Cost = e.Cost,
        MaxParticipants = e.MaxParticipants,
        ParticipantCount = row.ParticipantCount,
        IsCancelled = e.Status == EventStatus.Cancelled,
        DistanceKm = distanceKm,
    };

    private static double? ComputeDistanceKm(Event e, EventQueryModel query)
    {
        if (!query.HasOrigin)
        {
            return null;
        }

        var km = HaversineKm(query.OriginLat!.Value, query.OriginLng!.Value, e.Lat, e.Lng);

        // One decimal, matching fixtures.ts so the card stops changing between
        // refetches.
        return Math.Round(km, 1, MidpointRounding.AwayFromZero);
    }

    private static double HaversineKm(double lat1, double lng1, double lat2, double lng2)
    {
        const double earthRadiusKm = 6371d;
        var dLat = ToRadians(lat2 - lat1);
        var dLng = ToRadians(lng2 - lng1);
        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2)
                + Math.Cos(ToRadians(lat1)) * Math.Cos(ToRadians(lat2))
                * Math.Sin(dLng / 2) * Math.Sin(dLng / 2);
        return earthRadiusKm * 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
    }

    private static double ToRadians(double degrees) => degrees * Math.PI / 180d;
}
