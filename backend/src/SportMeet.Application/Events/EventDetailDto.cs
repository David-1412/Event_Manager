using SportMeet.Domain.Enums;

namespace SportMeet.Application.Events;

/// <summary>A participant or host as the client renders them. Matches the
/// Participant union in frontend/src/types/events.ts; a null AvatarUrl renders
/// the initials avatar there, so it is a real state rather than a gap.</summary>
public sealed record ParticipantDto(Guid Id, string DisplayName, string? AvatarUrl);

/// <summary>
/// EventListItemDto plus the five fields only the detail page needs
/// (frontend/src/types/events.ts, EventDetail).
/// </summary>
public sealed class EventDetailDto : EventListItemDto
{
    // Description is not re-declared here: the browse preview needed it on the
    // list item, and the detail inherits it from there.
    public required ParticipantDto Host { get; init; }

    /// <summary>
    /// Computed against ICurrentUser, never hardcoded in the query. With no auth
    /// in Milestone 1 this is true only for events hosted by the configured
    /// Demo:AsUserId, which is what keeps the "You're hosting" card variant
    /// reachable in demo mode without the service assuming a user.
    /// </summary>
    public required bool IsHost { get; init; }

    /// <summary>True when ICurrentUser has a row in event_participants.</summary>
    public required bool IsJoined { get; init; }

    /// <summary>True when ICurrentUser has a row in event_interests. Independent
    /// of IsJoined: the join path clears interest, so in steady state a joiner is
    /// not also interested, but the two are computed from their own tables.</summary>
    public required bool IsInterested { get; init; }

    public required IReadOnlyList<ParticipantDto> Participants { get; init; }

    public DateTimeOffset? CancelledAt { get; init; }

    // RequiresJoinApproval is not re-declared here: it belongs on the list item, so
    // a browse card can say "approval needed" without fetching the event. The detail
    // inherits it, exactly as Description is inherited for the same reason.

    /// <summary>The viewer's own standing in the approval queue, or null when they
    /// never asked. The client renders "Request submitted" from
    /// <see cref="JoinRequestStatus.Pending"/> and hides the Join button for it.
    ///
    /// Serialized as the name, not the ordinal, by the nullable converter in
    /// Api.Serialization — the client compares against the string, so an ordinal
    /// here reads as "no request" and the pending state disappears without an error.
    ///
    /// Separate from <see cref="EventDetailDto.IsJoined"/> on purpose: approved is
    /// both IsJoined and Approved, but pending is neither, and collapsing them would
    /// lose the state the member is actually in.</summary>
    public JoinRequestStatus? MyJoinRequestStatus { get; init; }
}

/// <summary>One entry in the host's Join Requests list. Carries the requester as a
/// <see cref="ParticipantDto"/> so the panel renders the same avatar/name pair the
/// participant list already renders.
///
/// <c>Status</c> goes on the wire as its PascalCase name (see the converter in
/// Api.Serialization). The panel chooses the Approve/Reject buttons over a static
/// badge by comparing that name, so an ordinal here silently removes the host's
/// only way to answer a request.</summary>
public sealed record JoinRequestDto(
    ParticipantDto Requester,
    JoinRequestStatus Status,
    DateTimeOffset RequestedAt,
    DateTimeOffset? DecidedAt);

/// <summary>The host's queue for one event. <c>pendingCount</c> is sent alongside
/// rather than counted client-side because the list may be the decided-inclusive
/// view, where the two would disagree.</summary>
public sealed record JoinRequestListDto(
    IReadOnlyList<JoinRequestDto> Items,
    int PendingCount);

