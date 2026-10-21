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
public sealed class EventService(
    IEventRepository events,
    ICurrentUser currentUser,
    SportMeet.Application.Notifications.INotificationRepository notifications,
    // Optional so the join/reminder/review tests that construct an EventService for an
    // unrelated reason do not have to build a follower fan-out. Null means "announce to
    // nobody", which is the correct default for a test and is never what the container
    // supplies - AddApplication always registers the notifier, so a production
    // EventService always has one.
    SportMeet.Application.Profiles.ICreatorFollowNotifier? followers = null) : IEventService
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

    /// <summary>
    /// The Following tab. One query, identical to browse except for the
    /// follow-graph narrowing the repository folds into its WHERE clause as an
    /// EXISTS over user_follows — no per-creator fetch, and every browse filter
    /// (search, tag, date, radius, sort, page) arrives untouched through
    /// <paramref name="query"/>.
    /// </summary>
    public Task<PagedResult<EventListItemDto>> ListFollowingAsync(
        EventQueryModel query, CancellationToken ct = default)
    {
        // Signed-in is the precondition, not a preference: the feed's definition
        // is "people *you* follow", and answering an anonymous caller with an
        // empty list would read as "nobody you follow has events" rather than
        // "we don't know who you are". The client shows a sign-in prompt instead.
        var viewerId = currentUser.UserId
            ?? throw new DomainRuleException("You must be signed in to see events from people you follow.");

        // EventQueryModel is a class, not a record, so the narrowed copy is
        // spelled out rather than produced by `with`. Every field is carried
        // across deliberately: a browse filter silently dropped here would go
        // missing from the Following tab only in production, where the filter bar
        // sends all of them.
        return ListAsync(new EventQueryModel
        {
            Search = query.Search,
            TagName = query.TagName,
            DateFilter = query.DateFilter,
            RadiusKm = query.RadiusKm,
            OriginLat = query.OriginLat,
            OriginLng = query.OriginLng,
            Sort = query.Sort,
            Page = query.Page,
            PageSize = query.PageSize,
            FollowedByUserId = viewerId,
        }, ct);
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
            RequiresJoinApproval = row.Event.RequiresJoinApproval,
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
            // Only worth a lookup when the event actually gates joining and the
            // viewer is not the host: on an open event the answer is always null and
            // a host never has a request of their own, so the common path stays at
            // the queries it already made.
            MyJoinRequestStatus = row.Event.RequiresJoinApproval
                                  && currentUser.UserId is { } asker
                                  && asker != row.Event.HostId
                ? await events.FindMyJoinRequestStatusAsync(id, asker, ct)
                : null,
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
            // Null passes through as null, which is the whole point: an empty Spots
            // field means "no limit" and has to reach the column as NULL. The old
            // `!.Value` is what made an empty field impossible, and it is now
            // reachable - a client may send maxParticipants: null, or omit it
            // entirely - so unwrapping here would throw on a legitimate request.
            // The 2-50 bound is still enforced when a number *is* sent, by the
            // validator and by the database CHECK.
            MaxParticipants = dto.MaxParticipants,
            SkillLevel = dto.SkillLevel,
            Cost = dto.Cost,
            // Public events from Members wait for review. Moderators can publish
            // immediately, but that capability is deliberately separate from the
            // Admin-only review and user-management checks.
            Status = dto.Visibility != EventVisibility.Private && !currentUser.CanPublishPublicEvents
                ? EventStatus.PendingReview
                : EventStatus.Scheduled,
            // A caller that sends no visibility gets a public event, so an older
            // client (or an older draft payload) cannot silently create something
            // that never appears in browse.
            Visibility = dto.Visibility ?? EventVisibility.Public,

            // Same absent-means-open rule as visibility above: an older client, an
            // older draft payload, or an ingestion path that never heard of this
            // field all create an event that joins immediately.
            RequiresJoinApproval = dto.RequiresJoinApproval ?? false,
        };

        await events.AddAsync(entity, ct);

        // Separate statement rather than building the navigation in memory: the
        // event row must exist first, and letting EF walk the graph would have it
        // insert tag rows that are already in the table.
        await events.ReplaceEventTagsAsync(entity.Id, tagNames, now, ct);

        // Followers hear about it only if it is actually out. A public event from a
        // Member landed in PendingReview above and is not visible to anyone but its
        // creator, so the notifier sees that and writes nothing; it will fire from the
        // admin's approval instead. A private event never fires at all. The rule is
        // re-checked inside the notifier rather than decided from the status computed
        // here, so this call site cannot be the place the rule gets it wrong.
        await (followers?.PublishAsync(entity, ct) ?? Task.CompletedTask);

        // Re-read through the same path as GET so the 201 body is provably what
        // the detail page will render. Assembling it from the request instead is
        // how a field that silently fails to persist stays hidden.
        return await GetAsync(entity.Id, ct);
    }

    public async Task<EventDetailDto> UpdateHostedAsync(
        Guid eventId,
        UpdateAdminEventRequest request,
        CancellationToken ct = default)
    {
        var userId = currentUser.UserId
            ?? throw new DomainRuleException("You must be signed in to manage an event.");
        EventUpdateRules.Validate(request);

        var current = await events.FindAsync(eventId, ct)
            ?? throw new NotFoundException("Event", eventId, eventId);
        if (current.Event.HostId != userId)
            throw new NotFoundException("Event", eventId, eventId);

        var now = DateTimeOffset.UtcNow;
        Event? newlyVisible = null;
        await events.RunInTransactionAsync(async inner =>
        {
            var entity = await events.FindWithLockAsync(eventId, inner)
                ?? throw new NotFoundException("Event", eventId, eventId);
            if (entity.HostId != userId)
                throw new NotFoundException("Event", eventId, eventId);

            var oldStatus = entity.Status;
            var oldVisibility = entity.Visibility;
            EventUpdateRules.Apply(entity, request, now);
            entity.EditedAt = now;

            if (!currentUser.CanPublishPublicEvents)
            {
                entity.Visibility = oldVisibility;
                entity.Status = oldVisibility == EventVisibility.Public
                    ? EventStatus.PendingReview
                    : oldStatus;
                if (entity.Status == EventStatus.PendingReview)
                    entity.RejectionReason = null;
            }
            else if (entity.Visibility == EventVisibility.Public
                && entity.Status is EventStatus.PendingReview or EventStatus.Rejected)
            {
                entity.Status = EventStatus.Published;
                entity.RejectionReason = null;
            }

            entity.CancelledAt = entity.Status == EventStatus.Cancelled
                ? oldStatus == EventStatus.Cancelled ? entity.CancelledAt : now
                : null;

            // Captured inside the transaction so the decision is made against the
            // locked row, and announced only after the commit below. The test is a
            // transition and not a state: an already-live event that is merely having
            // its description corrected was visible before this request and is visible
            // after it, so nothing new became discoverable and followers have nothing
            // new to hear. Without the "was not already visible" half, every edit to a
            // live event would re-announce it.
            var wasVisible = oldVisibility == EventVisibility.Public && oldStatus.IsPublished();
            var isVisible = entity.Visibility == EventVisibility.Public && entity.Status.IsPublished();
            if (!wasVisible && isVisible)
            {
                newlyVisible = entity;
            }

            await events.SaveChangesAsync(inner);
            await events.ReplaceEventTagsAsync(eventId, TagNormalizer.NormalizeMany(request.Tags), now, inner);
        }, ct);

        // Outside the transaction, deliberately. This path notifies on a transition
        // that is a side effect of an edit, not the edit's purpose: a creator fixing a
        // typo should not have their save fail because a notification could not be
        // written. CreateAsync and DecideAsync keep the fan-out inside instead, because
        // there the publish is the request and telling the audience is part of doing it.
        // The window this opens is a committed event whose announcement is a second
        // away late; the window the alternative opens is a lost edit.
        if (newlyVisible is not null)
        {
            await (followers?.PublishAsync(newlyVisible, ct) ?? Task.CompletedTask);
        }

        return await GetAsync(eventId, ct);
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

            if (locked.RequiresJoinApproval)
            {
                // Gated event: a request, not a spot. Everything above still applies
                // - published, not the host, not started - and it is checked against
                // the locked row, so a host who turns approval on cannot be raced by a
                // join that read the old value.
                //
                // Deliberately before the capacity check. A pending request reserves
                // nothing, so a full gated event still accepts requests; the host
                // decides who takes a spot, and capacity is enforced at approval
                // instead. Filling the queue would otherwise let unactioned requests
                // lock the host out of their own event.
                await RequestJoinAsync(locked, userId, inner);
                return;
            }

            var current = await events.CountParticipantsAsync(eventId, inner);
            // A null capacity is "no limit": there is nothing to be full against, so
            // the comparison is skipped rather than run against a stand-in number.
            // The count itself stays unconditional - it is one cheap aggregate and the
            // notification below reads it - but `current >= null` must never be
            // collapsed into something like `current >= 0`, which would reject every
            // join to an unlimited event.
            if (locked.MaxParticipants is { } max && current >= max)
            {
                throw new EventFullException(eventId);
            }

            await events.AddParticipantAsync(eventId, userId, DateTimeOffset.UtcNow, inner);

            // Requirement: joining supersedes interest. Clear any interest row in
            // the same transaction so the two sets never overlap and the
            // Interested count never double-counts a joiner. No-op when absent.
            await events.RemoveInterestAsync(eventId, userId, inner);

            await NotifyCreatorAsync(
                locked,
                userId,
                "EventJoined",
                "New event attendee",
                $"Someone joined your event '{locked.Title}'.",
                inner);
            await NotifyMilestoneAsync(locked, userId, "JoinMilestone", await events.CountParticipantsAsync(eventId, inner), inner);
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

    public async Task<JoinRequestListDto> ListJoinRequestsAsync(
        Guid eventId, bool includeDecided, CancellationToken ct = default)
    {
        var hostId = currentUser.UserId
            ?? throw new DomainRuleException("You must be signed in to review join requests.");

        // The list query is itself host-scoped, so an event the caller does not own
        // returns no rows rather than leaking that it exists. That is the right shape
        // for a list, but it cannot tell "your event is empty" from "not your event",
        // so the existence check is separate and deliberately host-blind: a non-host
        // gets 404, matching how the review queue refuses to confirm a private id.
        _ = await events.FindAsync(eventId, ct)
            ?? throw new NotFoundException("Event", eventId, eventId);

        var rows = await events.ListJoinRequestsAsync(eventId, hostId, includeDecided, ct);
        return new JoinRequestListDto(
            rows.Select(r => new JoinRequestDto(
                    new ParticipantDto(r.UserId, r.DisplayName, r.PhotoUrl),
                    r.Status, r.RequestedAt, r.DecidedAt))
                .ToList(),
            rows.Count(r => r.Status == JoinRequestStatus.Pending));
    }

    public async Task<JoinRequestListDto> ApproveJoinRequestAsync(
        Guid eventId, Guid userId, CancellationToken ct = default)
    {
        var hostId = currentUser.UserId
            ?? throw new DomainRuleException("You must be signed in to review join requests.");

        // A host approving their own request cannot happen through the UI - Join
        // refuses a host long before a request row exists - but the endpoint takes an
        // arbitrary user id, so the rule is stated rather than assumed.
        if (userId == hostId)
        {
            throw new DomainRuleException("You cannot approve your own join request.");
        }

        // Lock, count and decide in one transaction, exactly as Join does: two hosts'
        // approvals (or an approval racing a direct join on an event whose gate was
        // just turned off) cannot both take the last spot.
        await events.RunInTransactionAsync(async inner =>
        {
            var locked = await events.FindWithLockAsync(eventId, inner)
                ?? throw new NotFoundException("Event", eventId, eventId);

            if (locked.HostId != hostId)
            {
                // 404, not 403 - the convention every other host-only path in this
                // service follows (ChangeStatusAsync, UpdateHostedAsync). A caller who
                // is not the host is not shown that the event exists for someone else.
                throw new NotFoundException("Event", eventId, eventId);
            }

            var current = await events.CountParticipantsAsync(eventId, inner);
            // Same rule as the direct join: an unlimited event cannot fill, so an
            // approval is never refused for capacity. The host still gets the
            // existing EventFull copy when a real limit has been reached, and the
            // request stays Pending so it can be approved if a spot opens.
            if (locked.MaxParticipants is { } max && current >= max)
            {
                throw new EventFullException(eventId);
            }

            if (!await events.TryApproveJoinRequestAsync(eventId, userId, hostId, DateTimeOffset.UtcNow, inner))
            {
                throw new NotFoundException("Join request", userId, eventId);
            }

            // userId is the requester - the repository only ever decides the row
            // matching it, so there is no separate identity to carry back out.
            await NotifyJoinDecisionAsync(
                locked, userId, "JoinRequestApproved", "Join request approved",
                $"You're in — the host approved your request to join '{locked.Title}'.", inner);
        }, ct);

        return await ListJoinRequestsAsync(eventId, includeDecided: true, ct);
    }

    public async Task<JoinRequestListDto> RejectJoinRequestAsync(
        Guid eventId, Guid userId, CancellationToken ct = default)
    {
        var hostId = currentUser.UserId
            ?? throw new DomainRuleException("You must be signed in to review join requests.");

        if (userId == hostId)
        {
            throw new DomainRuleException("You cannot reject your own join request.");
        }

        await events.RunInTransactionAsync(async inner =>
        {
            var locked = await events.FindWithLockAsync(eventId, inner)
                ?? throw new NotFoundException("Event", eventId, eventId);

            if (locked.HostId != hostId)
            {
                throw new NotFoundException("Event", eventId, eventId);
            }

            if (!await events.TryRejectJoinRequestAsync(eventId, userId, hostId, DateTimeOffset.UtcNow, inner))
            {
                throw new NotFoundException("Join request", userId, eventId);
            }

            await NotifyJoinDecisionAsync(
                locked, userId, "JoinRequestRejected", "Join request declined",
                $"The host could not approve your request to join '{locked.Title}'.", inner);
        }, ct);

        return await ListJoinRequestsAsync(eventId, includeDecided: true, ct);
    }

    /// <summary>Tell a member what happened to their request. The mirror of
    /// <see cref="NotifyCreatorAsync"/>: that one writes to the host, this one to the
    /// requester, and both reuse the same notifications table and the same link shape
    /// so the bell routes to the event either way.</summary>
    private async Task NotifyJoinDecisionAsync(
        Event eventEntity, Guid userId, string type, string title, string message, CancellationToken ct)
    {
        await notifications.AddAsync(new SportMeet.Domain.Entities.Notification
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Title = title,
            Message = message,
            Type = type,
            Link = $"/events/{eventEntity.Id}",
            Read = false,
            CreatedAt = DateTimeOffset.UtcNow,
        }, ct);
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

    public async Task<IReadOnlyList<Guid>> ListMyPendingJoinRequestsAsync(CancellationToken ct = default)
    {
        // Mirrors ListMyJoinedAsync: no identity is an empty list, not an error, so
        // a browse page with no signed-in viewer renders no "Request submitted"
        // state rather than failing the feed.
        var userId = currentUser.UserId;
        if (userId is null)
        {
            return [];
        }

        return await events.ListPendingJoinRequestEventIdsAsync(userId.Value, ct);
    }

    /// <summary>Toggle the current viewer's interest in one event: adding when
    /// uninterested, removing when interested (the composite key already forbids a
    /// second row). The event row is locked while changing interest and recording its
    /// milestone so concurrent changes cannot skip a threshold. Returns the post-toggle state so the client settles its button
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

        var now = DateTimeOffset.UtcNow;
        var interested = false;
        await events.RunInTransactionAsync(async inner =>
        {
            var locked = await events.FindWithLockAsync(eventId, inner)
                ?? throw new NotFoundException("Event", eventId, eventId);

            if (await events.IsInterestedAsync(eventId, userId, inner))
            {
                await events.RemoveInterestAsync(eventId, userId, inner);
                return;
            }

            await events.AddInterestAsync(eventId, userId, now, inner);
            interested = true;
            await NotifyCreatorAsync(
                locked,
                userId,
                "EventInterested",
                "New event interest",
                $"Someone is interested in your event '{locked.Title}'.",
                inner);
            await NotifyMilestoneAsync(locked, userId, "InterestMilestone", await events.CountInterestedAsync(eventId, inner), inner);
        }, ct);
        return interested;
    }

    /// <summary>
    /// The automatic Interested on a first view. Unlike <see cref="ToggleInterestAsync"/>
    /// it notifies nobody: the host hearing "someone is interested" for every page view
    /// would make the notification meaningless. It is skipped where tapping Interested
    /// would not make sense either - your own event, one you have joined, a cancelled
    /// or unpublished one - and it never fails the page: if it cannot be written, the
    /// visitor simply sees the event without the mark.
    /// </summary>
    public async Task<bool> TryAutoInterestAsync(EventDetailDto detail, CancellationToken ct = default)
    {
        if (currentUser.UserId is not { } userId) return false;
        if (detail.IsInterested || detail.IsHost || detail.IsJoined || detail.IsCancelled) return false;
        if (!Enum.TryParse<EventStatus>(detail.Status, out var status) || !status.IsPublished()) return false;

        try
        {
            await events.AddAutoInterestAsync(detail.Id, userId, DateTimeOffset.UtcNow, ct);
            return true;
        }
        catch (Exception) when (!ct.IsCancellationRequested)
        {
            return false;
        }
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

    /// <summary>
    /// The host removes an event they have cancelled. Only a cancelled event: a live one
    /// has to be cancelled first, which is the step that tells the people who joined.
    /// Unlike cancel and reopen this is allowed after the start time, so a host can
    /// clear old cancelled events out of their list. A soft delete, like the admin's.
    /// </summary>
    public async Task DeleteCancelledAsync(Guid eventId, CancellationToken ct = default)
    {
        var userId = currentUser.UserId
            ?? throw new DomainRuleException("You must be signed in to manage an event.");

        if (await events.TryDeleteCancelledAsync(eventId, userId, DateTimeOffset.UtcNow, ct)) return;

        // Nothing matched: say why, without telling a stranger the event exists.
        var row = await events.FindAsync(eventId, ct)
            ?? throw new NotFoundException("Event", eventId, eventId);
        if (row.Event.HostId != userId) throw new NotFoundException("Event", eventId, eventId);
        throw new DomainRuleException("Cancel the event before deleting it.");
    }

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

        var now = DateTimeOffset.UtcNow;
        await events.RunInTransactionAsync(async inner =>
        {
            if (!await events.TrySetReviewStatusAsync(eventId, row.Event.Status, decision, now, inner))
            {
                throw new DomainRuleException("This event was just reviewed by someone else. Refresh and try again.");
            }

            if (decision == EventStatus.Published && row.Event.Visibility == EventVisibility.Public)
            {
                await notifications.AddAsync(new SportMeet.Domain.Entities.Notification
                {
                    Id = Guid.NewGuid(),
                    UserId = row.Event.HostId,
                    Title = "Event approved",
                    Message = $"Your event '{row.Event.Title}' has been approved!",
                    Type = "EventApproved",
                    Link = $"/events/{row.Event.Id}",
                    Read = false,
                    CreatedAt = now,
                }, inner);

                // The approval is the moment a Member's public event becomes visible,
                // so it is the moment its followers hear about it - the create call
                // could not announce it, because at that point it was PendingReview.
                // Inside the transaction for the reason on CreateAsync: an approval that
                // leaves an audience un-notified cannot be reconstructed later, and the
                // creator's own notification above already commits or rolls back with
                // the decision, so announcing to everyone else on the same terms is the
                // consistent choice rather than a new risk.
                //
                // The status is applied to the in-memory copy because
                // TrySetReviewStatusAsync is an ExecuteUpdateAsync - it writes the row
                // without touching the tracked entity, so row.Event still reads
                // PendingReview here. Passing that unchanged would fail the notifier's
                // eligibility check and announce nothing, silently, forever.
                //
                // Rejected events reach DecideAsync too but land on the other branch, so
                // a refusal announces nothing.
                row.Event.Status = decision;
                await (followers?.PublishAsync(row.Event, inner) ?? Task.CompletedTask);
            }
        }, ct);

        // Re-read through the detail path so the response is what every other reader
        // will now see - including the creator, whose copy just changed status.
        return await GetAsync(eventId, ct);
    }

    /// <summary>The gated branch of Join: record the request and tell the host.
    ///
    /// Runs inside the caller's transaction, so the request and the host's
    /// notification commit together - a host who is told about a request can always
    /// find it.
    ///
    /// Interest is left alone rather than cleared as the open path does. Joining
    /// supersedes interest because it takes the spot; a request has taken nothing
    /// yet, and a rejected member who is still interested has an honest reason to
    /// ask again.</summary>
    private async Task RequestJoinAsync(Event locked, Guid userId, CancellationToken ct)
    {
        var outcome = await events.AddJoinRequestAsync(locked.Id, userId, DateTimeOffset.UtcNow, ct);
        if (outcome != JoinRequestOutcome.Created)
        {
            // Already pending, already approved, or reopened after a rejection: the
            // host asked about this person before, so a second "someone wants in"
            // note would be noise. A reopened request is the exception worth telling
            // them about, and it reads differently from a first ask.
            if (outcome == JoinRequestOutcome.Reopened)
            {
                await NotifyCreatorAsync(
                    locked, userId, "JoinRequestReopened", "Join request reopened",
                    $"Someone has asked to join your event '{locked.Title}' again.", ct);
            }

            return;
        }

        await NotifyCreatorAsync(
            locked, userId, "JoinRequestSubmitted", "New join request",
            $"Someone wants to join your event '{locked.Title}'. Your approval is needed.", ct);
    }

    private async Task NotifyCreatorAsync(
        Event eventEntity,
        Guid actorId,
        string type,
        string title,
        string message,
        CancellationToken ct)
    {
        if (eventEntity.HostId == actorId) return;

        await notifications.AddAsync(new SportMeet.Domain.Entities.Notification
        {
            Id = Guid.NewGuid(),
            UserId = eventEntity.HostId,
            Title = title,
            Message = message,
            Type = type,
            Link = $"/events/{eventEntity.Id}",
            Read = false,
            CreatedAt = DateTimeOffset.UtcNow,
        }, ct);
    }

    private async Task NotifyMilestoneAsync(
        Event eventEntity,
        Guid actorId,
        string type,
        int count,
        CancellationToken ct)
    {
        int[] milestones = [5, 10, 25, 50];
        if (eventEntity.HostId == actorId || !milestones.Contains(count)) return;

        var label = type == "JoinMilestone" ? "joins" : "interests";
        await notifications.AddAsync(new SportMeet.Domain.Entities.Notification
        {
            Id = Guid.NewGuid(),
            UserId = eventEntity.HostId,
            Title = "Event milestone",
            Message = $"Your event '{eventEntity.Title}' has reached {count} {label}!",
            Type = type,
            Link = $"/events/{eventEntity.Id}",
            Read = false,
            CreatedAt = DateTimeOffset.UtcNow,
        }, ct);
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
        Description = e.Description,
        Tags = row.Tags,
        SportIcon = row.SportIcon,
        HostName = row.HostName,
        HostPhotoUrl = row.HostPhotoUrl,
        HostId = e.HostId,
        HostRole = row.HostRole.ToString(),
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
        RequiresJoinApproval = e.RequiresJoinApproval,
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
