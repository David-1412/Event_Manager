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

        // joinedCount and interestedCount ride in on the view (one round-trip, no
        // N+1). The per-viewer isJoined / isInterested flags are deliberately NOT
        // here: the browse list is anonymous (swr-fetcher sends no token until a
        // session exists), so both relations resolve through the me/joined and
        // me/interested side-channels the client joins by id. See use-events.tsx.
        //
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

        // An event that has not been published exists only for the two people who
        // have a reason to read it: the creator, and the administrator who has to
        // decide on it. Everyone else gets the same 404 an unknown id gets - the
        // review queue is not something a stranger should be able to probe for by
        // id, and a 403 would confirm that the submission is real.
        //
        // Published events (Scheduled or Published) are readable by anyone, exactly
        // as a private event always was: this rule is about the review state, not
        // about the visibility flag, and the link-sharing contract is unchanged.
        if (!row.Event.Status.IsPublished() && !CanManage(row))
        {
            throw new NotFoundException("Event", id);
        }

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
            ThumbnailUrl = row.Event.ThumbnailUrl,
            Latitude = row.Event.Lat,
            Longitude = row.Event.Lng,
            Cost = row.Event.Cost,
            MaxParticipants = row.Event.MaxParticipants,
            JoinedCount = row.ParticipantCount,
            InterestedCount = row.InterestedCount,
            Status = row.Event.Status.ToString(),
            IsCancelled = row.Event.Status == EventStatus.Cancelled,
            IsPublished = row.Event.Status.IsPublished(),
            Visibility = row.Event.Visibility.ToString(),
            DistanceKm = null,
            Description = row.Event.Description,
            Host = new ParticipantDto(row.Event.HostId, row.HostName, row.HostPhotoUrl),

            // Both relations are evaluated against ICurrentUser, never hardcoded.
            // In Milestone 1 that identity comes from Demo:AsUserId; when a
            // verified token supplies it these lines do not change.
            IsHost = currentUser.UserId is { } host && host == row.Event.HostId,
            IsJoined = currentUser.UserId is { } joiner && await events.IsParticipantAsync(id, joiner, ct),
            IsInterested = currentUser.UserId is { } admirer && await events.IsInterestedAsync(id, admirer, ct),
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
            CreatedAt = now,
            UpdatedAt = now,
            Title = dto.Title!.Trim(),
            Description = string.IsNullOrWhiteSpace(dto.Description) ? null : dto.Description.Trim(),
            // Null: tags replaced the sport vocabulary and the create form sends no
            // sport, so a new event renders without an emoji.
            SportId = null,
            VenueName = dto.VenueName!.Trim(),
            Address = dto.Address!.Trim(),
            ThumbnailUrl = string.IsNullOrWhiteSpace(dto.ThumbnailUrl) ? null : dto.ThumbnailUrl.Trim(),
            Lat = dto.Latitude!.Value,
            Lng = dto.Longitude!.Value,
            Timezone = string.IsNullOrWhiteSpace(dto.Timezone) ? "Australia/Melbourne" : dto.Timezone,
            StartAt = dto.StartAt.Value,
            EndAt = dto.EndAt.Value,
            MaxParticipants = dto.MaxParticipants!.Value,
            SkillLevel = dto.SkillLevel,
            Cost = dto.Cost,
            // The approval workflow's one decision point. A public event written by
            // anyone who is not an administrator waits for review, so it is stored as
            // PendingReview and the feed filter keeps it out of browse until an admin
            // approves it; a private event is the creator's own to share and publishes
            // straight away, and an admin's public event publishes because publishing
            // is exactly what the role is for.
            Status = dto.Visibility != EventVisibility.Private && !currentUser.IsAdmin
                ? EventStatus.PendingReview
                : EventStatus.Scheduled,
            // A caller that sends no visibility gets a public event, so an older
            // client (or an older draft payload) cannot silently create something
            // that never appears in browse.
            Visibility = dto.Visibility ?? EventVisibility.Public,
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

            if (!locked.Status.IsPublished())
            {
                // Cancelled, Completed, or still in the review workflow: none of them
                // is an event anybody can join. The unpublished case cannot be reached
                // through the UI (the client hides Join while isPublished is false), so
                // this is the API refusing a direct call rather than a message a user
                // reads - but it must refuse, or a pending event could be filled with
                // participants before an admin ever saw it.
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

            // Requirement: joining supersedes interest. Clear any interest row in
            // the same transaction so the two sets never overlap and the
            // Interested count never double-counts a joiner. No-op when absent.
            await events.RemoveInterestAsync(eventId, userId, inner);
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

    /// <summary>Toggle the current viewer's interest in one event: adding when
    /// uninterested, removing when interested (the composite key already forbids a
    /// second row). Returns the post-toggle state so the client settles its button
    /// and toast from the server rather than guessing. Interest reserves no spot and
    /// is independent of capacity, so unlike Join there is nothing to lock.</summary>
    public async Task<bool> ToggleInterestAsync(Guid eventId, CancellationToken ct = default)
    {
        var userId = currentUser.UserId
            ?? throw new DomainRuleException("You must be signed in to mark an event interested.");

        // Existence check first so an unknown id is a 404 rather than a row written
        // against a non-existent event (the FK would reject it as a 500 instead).
        _ = await events.FindAsync(eventId, ct)
            ?? throw new NotFoundException("Event", eventId, eventId);

        if (await events.IsInterestedAsync(eventId, userId, ct))
        {
            await events.RemoveInterestAsync(eventId, userId, ct);
            return false;
        }

        await events.AddInterestAsync(eventId, userId, DateTimeOffset.UtcNow, ct);
        return true;
    }

    public async Task<IReadOnlyList<Guid>> ListMyInterestedAsync(CancellationToken ct = default)
    {
        // Mirrors ListMyJoinedAsync: no identity is an empty list, not an error, so
        // My events degrades gracefully rather than blocking on a 401.
        var userId = currentUser.UserId;
        if (userId is null)
        {
            return [];
        }

        return await events.ListInterestedEventIdsAsync(userId.Value, ct);
    }

    public async Task<IReadOnlyList<EventListItemDto>> ListMyHostedAsync(CancellationToken ct = default)
    {
        var userId = currentUser.UserId;
        if (userId is null)
        {
            return [];
        }

        var rows = await events.ListHostedAsync(userId.Value, ct);
        return rows.Select(row => ToListItem(row.Event, row, null)).ToList();
    }

    public Task<EventDetailDto> CancelAsync(Guid eventId, CancellationToken ct = default)
        => ChangeStatusAsync(eventId, EventStatus.Scheduled, EventStatus.Cancelled, ct);

    public Task<EventDetailDto> ReopenAsync(Guid eventId, CancellationToken ct = default)
        => ChangeStatusAsync(eventId, EventStatus.Cancelled, EventStatus.Scheduled, ct);

    /// <summary>The queue, in the shape the review page renders. The rows come from
    /// the same view browse reads, so a card here is the same card the creator sees -
    /// which is the point of reviewing in the product rather than in a database.</summary>
    public async Task<IReadOnlyList<EventListItemDto>> ListPendingReviewAsync(CancellationToken ct = default)
    {
        RequireAdmin(Guid.Empty);
        var rows = await events.ListPendingReviewAsync(ct);
        return rows.Select(row => ToListItem(row.Event, row, null)).ToList();
    }

    public Task<EventDetailDto> ApproveAsync(Guid eventId, CancellationToken ct = default)
        => DecideAsync(eventId, EventStatus.Published, ct);

    public Task<EventDetailDto> RejectAsync(Guid eventId, CancellationToken ct = default)
        => DecideAsync(eventId, EventStatus.Rejected, ct);

    /// <summary>Record an administrator's decision. Guarded on the event actually
    /// awaiting one, so approving something that is already live (or that the other
    /// admin on this machine rejected a second ago) is the same 404 as approving
    /// something that does not exist - the client's list is a snapshot, and the honest
    /// answer to a stale row is "refresh", not a silent second write.</summary>
    private async Task<EventDetailDto> DecideAsync(Guid eventId, EventStatus decision, CancellationToken ct)
    {
        RequireAdmin(eventId);

        var row = await events.FindAsync(eventId, ct)
            ?? throw new NotFoundException("Event", eventId, eventId);

        // PendingReview is the normal case; Rejected is accepted so a creator's
        // resubmission can be approved without the admin first un-rejecting it by
        // hand. Anything else is not awaiting a decision.
        var awaiting = row.Event.Status is EventStatus.PendingReview or EventStatus.Rejected;
        if (!awaiting)
        {
            throw new NotFoundException("Event", eventId, eventId);
        }

        if (!await events.TrySetReviewStatusAsync(eventId, row.Event.Status, decision, DateTimeOffset.UtcNow, ct))
        {
            throw new DomainRuleException("This event was just reviewed by someone else. Refresh and try again.");
        }

        // Re-read through the detail path so the response is what every other reader
        // will now see - including the creator, whose copy just changed status.
        return await GetAsync(eventId, ct);
    }

    private async Task<EventDetailDto> ChangeStatusAsync(
        Guid eventId,
        EventStatus expectedStatus,
        EventStatus status,
        CancellationToken ct)
    {
        var userId = currentUser.UserId
            ?? throw new DomainRuleException("You must be signed in to manage an event.");
        var now = DateTimeOffset.UtcNow;

        if (!await events.TrySetStatusAsync(eventId, userId, expectedStatus, status, now, ct))
        {
            var row = await events.FindAsync(eventId, ct)
                ?? throw new NotFoundException("Event", eventId, eventId);
            if (row.Event.HostId != userId)
            {
                throw new NotFoundException("Event", eventId, eventId);
            }

            if (row.Event.StartAt <= now)
            {
                throw new DomainRuleException("An event cannot be changed after it has started.");
            }

            throw new DomainRuleException("The event status has changed. Refresh and try again.");
        }

        return await GetAsync(eventId, ct);
    }

    private static EventListItemDto ToListItem(Event e, FeedRow row, double? distanceKm) => new()

    {
        Id = e.Id,
        Title = e.Title,
        Tags = row.Tags,
        SportIcon = row.SportIcon,
        HostName = row.HostName,
        HostPhotoUrl = row.HostPhotoUrl,
        SkillLevel = e.SkillLevel,
        StartAt = e.StartAt,
        EndAt = e.EndAt,
        Timezone = e.Timezone,
        VenueName = e.VenueName,
        Address = e.Address,
        ThumbnailUrl = e.ThumbnailUrl,
        Latitude = e.Lat,
        Longitude = e.Lng,
        Cost = e.Cost,
        MaxParticipants = e.MaxParticipants,
        JoinedCount = row.ParticipantCount,
        InterestedCount = row.InterestedCount,
        Status = e.Status.ToString(),
        IsCancelled = e.Status == EventStatus.Cancelled,
        IsPublished = e.Status.IsPublished(),
        Visibility = e.Visibility.ToString(),
        DistanceKm = distanceKm,
    };

    /// <summary>Whether the current viewer may act on an unpublished event: its
    /// creator (who can keep editing and resubmit it) or an administrator (who has
    /// to decide on it). One definition so the detail read and the review decisions
    /// cannot drift apart on who counts as allowed.</summary>
    private bool CanManage(FeedRow row) =>
        currentUser.IsAdmin
        || (currentUser.UserId is { } viewer && viewer == row.Event.HostId);

    /// <summary>The caller the review queue and the decisions demand: a signed-in
    /// administrator. A non-admin gets the NotFound shape rather than a refusal that
    /// names the role, for the same reason the detail read does it.
    /// <paramref name="eventId"/> is the id to name in that 404; the queue has none,
    /// and <see cref="Guid.Empty"/> is what the Problem Details writer renders as
    /// "no specific id".</summary>
    private void RequireAdmin(Guid eventId)
    {
        _ = currentUser.UserId
            ?? throw new DomainRuleException("You must be signed in to review an event.");

        if (!currentUser.IsAdmin)
        {
            throw new NotFoundException("Event", eventId, eventId);
        }
    }

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
