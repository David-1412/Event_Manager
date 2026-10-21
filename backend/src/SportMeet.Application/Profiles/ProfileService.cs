using SportMeet.Application.Common;
using SportMeet.Application.Events;
using SportMeet.Domain.Enums;

namespace SportMeet.Application.Profiles;

/// <summary>
/// The public profile use case: one summary, three event lists, one activity feed.
///
/// There is no authorization here, and that is the design. The profile is public, so
/// every method is safe to call for any id and the service's job is purely to shape
/// what the repository already restricted. The privacy rules live in
/// <see cref="IProfileRepository"/> (in SQL), not here, so a second caller cannot
/// reach the same rows through a path that forgot to filter.
///
/// The host-only surface - <c>GET /api/events/{id}/analytics</c> - is deliberately not
/// reachable from here in any form. A profile shows that a host is active; it never
/// shows what their events performed like.
/// </summary>
public sealed class ProfileService(
    IProfileRepository profiles,
    ICurrentUser? currentUser = null)
{
    /// <summary>Rows the Activity section shows. Twelve fills the section without
    /// turning a profile into a log dump; the spec asks for "recent activity" and this
    /// is that, newest first.</summary>
    public const int ActivityLimit = 12;

    /// <summary>The profile header plus its four numbers. Null when the id is unknown:
    /// the controller turns that into 404, because a 200 with an empty profile would
    /// assert that the id identifies somebody.</summary>
    public async Task<ProfileSummaryDto?> GetSummaryAsync(Guid userId, CancellationToken ct = default)
    {
        var subject = await profiles.FindSubjectAsync(userId, ct);
        if (subject is null)
        {
            return null;
        }

        var stats = await profiles.GetStatsAsync(userId, ct);
        var followersCount = await profiles.CountFollowersAsync(userId, ct);

        // A per-caller flag on an otherwise anonymous response, so it is read only when
        // there is a caller. Skipping it for an anonymous request is not an
        // optimisation: the answer must not be computed from an id nobody verified.
        var isFollowing = currentUser?.UserId is { } viewer && viewer != userId
            && await profiles.IsFollowingAsync(userId, viewer, ct);

        // OrganizationName has no column to read yet, so it is null rather than a
        // guess. The field exists on the contract so the client can render a club
        // layout the day the data does, without a second coordinated release.
        return new ProfileSummaryDto(
            subject.Id,
            subject.Name,
            subject.PhotoUrl,
            subject.Role,
            Bio: null,
            OrganizationName: null,
            ProfileKind: "Person",
            subject.CreatedAt,
            stats,
            followersCount,
            isFollowing);
    }

    /// <summary>Follow a profile. Idempotent: already following is reported as the
    /// settled state, not as a failure.</summary>
    public Task<ProfileFollowResultDto> FollowAsync(Guid userId, CancellationToken ct = default) =>
        SetFollowingAsync(userId, follow: true, ct);

    /// <summary>Unfollow a profile. Idempotent in the same direction - unfollowing
    /// someone you do not follow is already the requested state.</summary>
    public Task<ProfileFollowResultDto> UnfollowAsync(Guid userId, CancellationToken ct = default) =>
        SetFollowingAsync(userId, follow: false, ct);

    /// <summary>
    /// Both directions of the follow relation, which share everything except the write:
    /// the same three guards, the same 404 for an unknown subject, the same
    /// read-back-after-the-write so the response carries the database's count rather
    /// than an arithmetic guess.
    ///
    /// The guards are ordered deliberately. Sign-in is checked first because it is the
    /// caller's problem and costs nothing. Self-follow is next, before the subject
    /// lookup: asking whether a nonexistent user exists in order to be told you cannot
    /// follow yourself would be a stranger's existence confirmed for free. The subject
    /// lookup is last, and it is what makes following a made-up id a 404 rather than a
    /// row that the foreign key would reject with a 500.
    /// </summary>
    private async Task<ProfileFollowResultDto> SetFollowingAsync(
        Guid userId, bool follow, CancellationToken ct)
    {
        var viewer = currentUser?.UserId
            ?? throw new DomainRuleException("You must be signed in to follow someone.");

        if (viewer == userId)
        {
            throw new DomainRuleException("You cannot follow yourself.");
        }

        if (await profiles.FindSubjectAsync(userId, ct) is null)
        {
            throw new NotFoundException("User", userId, userId);
        }

        if (follow)
        {
            await profiles.AddFollowAsync(viewer, userId, DateTimeOffset.UtcNow, ct);
        }
        else
        {
            await profiles.RemoveFollowAsync(viewer, userId, ct);
        }

        // Both values read back rather than derived. `follow` is what the caller asked
        // for, not what happened: a duplicate insert and a no-op delete both leave the
        // caller in the state they intended, and a count of ±1 drifts permanently the
        // moment another device moves the real number.
        var isFollowing = await profiles.IsFollowingAsync(userId, viewer, ct);
        var followersCount = await profiles.CountFollowersAsync(userId, ct);
        return new ProfileFollowResultDto(isFollowing, followersCount);
    }

    /// <summary>Everything this host has ever publicly published, newest start first.
    /// Unknown id yields empty rather than null: a list section has a legitimate empty
    /// state, and "no such user" is the summary's question to answer.</summary>
    public async Task<IReadOnlyList<EventListItemDto>> GetHostedEventsAsync(
        Guid userId, CancellationToken ct = default)
    {
        var rows = await profiles.ListPublicHostedAsync(userId, ct);
        return rows.Select(ToListItem).ToList();
    }

    /// <summary>Upcoming events this host is running. Cancelled, Draft, PendingReview
    /// and Rejected are already gone - see IProfileRepository.</summary>
    public async Task<IReadOnlyList<EventListItemDto>> GetUpcomingEventsAsync(
        Guid userId, CancellationToken ct = default)
    {
        var rows = await profiles.ListPublicUpcomingAsync(userId, ct);
        return rows.Select(ToListItem).ToList();
    }

    /// <summary>Upcoming public events this user joined. Public-only is a privacy
    /// rule, not a preference - see IProfileRepository.ListPublicJoinedUpcomingAsync.</summary>
    public async Task<IReadOnlyList<EventListItemDto>> GetJoinedEventsAsync(
        Guid userId, CancellationToken ct = default)
    {
        var rows = await profiles.ListPublicJoinedUpcomingAsync(userId, ct);
        return rows.Select(ToListItem).ToList();
    }

    /// <summary>The activity feed, newest first.</summary>
    public async Task<IReadOnlyList<ProfileActivityDto>> GetActivityAsync(
        Guid userId, CancellationToken ct = default) =>
        await profiles.GetActivityAsync(userId, ActivityLimit, ct);

    /// <summary>Maps a feed row to the same card payload browse uses, so a profile's
    /// event tiles are the identical component with the identical fields - poster,
    /// title, date, joined and interested counts - rather than a second, drifting
    /// shape. Distance stays null: no origin is in play on a profile.</summary>
    private static EventListItemDto ToListItem(FeedRow row) => new()
    {
        Id = row.Event.Id,
        Title = row.Event.Title,
        Description = row.Event.Description,
        Tags = row.Tags,
        SportIcon = row.SportIcon,
        HostName = row.HostName,
        HostPhotoUrl = row.HostPhotoUrl,
        HostId = row.Event.HostId,
        HostRole = row.HostRole.ToString(),
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
        IsRecurring = row.Event.IsRecurring,
        SeriesId = row.Event.SeriesId,
        RecurrenceSummary = EventSeries.Summary(row.Event),
        RecurrenceFrequency = row.Event.IsRecurring ? row.Event.RecurrenceFrequency?.ToString().ToUpperInvariant() : null,
        DistanceKm = null,
    };
}
