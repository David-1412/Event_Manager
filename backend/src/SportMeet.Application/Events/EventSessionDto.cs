namespace SportMeet.Application.Events;

/// <summary>One session of a recurring series, for the card's session drawer and the
/// detail page's "Select session date" picker. Each session is its own event, so
/// <see cref="Id"/> is the event to open, join or mark Interested.</summary>
/// <param name="JoinedCount">People going to this session.</param>
/// <param name="MaxParticipants">Null for no limit.</param>
/// <param name="IsJoined">The caller is going to this session.</param>
public sealed record EventSessionDto(
    Guid Id,
    DateTimeOffset StartAt,
    DateTimeOffset EndAt,
    int JoinedCount,
    int? MaxParticipants,
    bool IsCancelled,
    bool IsJoined);
