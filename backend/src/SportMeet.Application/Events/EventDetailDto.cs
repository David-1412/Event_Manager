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
    public string? Description { get; init; }
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
}
