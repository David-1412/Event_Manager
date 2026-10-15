using SportMeet.Domain.Enums;

namespace SportMeet.Application.Events;

/// <summary>One row of the host's Join Requests list: the requester as the host
/// needs to see them, plus the decision record.
///
/// A record rather than the entity because the host list is a read model - it
/// carries the display name and photo the entity does not own, and it must not
/// drag a tracked User into a service that only projects it.</summary>
public sealed record JoinRequestRow(
    Guid UserId,
    string DisplayName,
    string? PhotoUrl,
    JoinRequestStatus Status,
    DateTimeOffset RequestedAt,
    DateTimeOffset? DecidedAt);

/// <summary>Why a request-to-join did not simply land as a new pending request.
///
/// An enum rather than an exception because most of these are not errors: a
/// member who taps Join twice, or who was already approved, gets a settled answer
/// and a 200, not a failure. Only
/// <see cref="AlreadyParticipant"/> needs the service to say something different,
/// and even that is a state the client can render.</summary>
public enum JoinRequestOutcome
{
    /// <summary>A fresh pending request was created.</summary>
    Created = 0,

    /// <summary>A pending request already existed. Nothing changed; the caller is
    /// still waiting on the host.</summary>
    AlreadyPending = 1,

    /// <summary>The caller had been rejected and has now asked again, so the row
    /// reopened. The host sees one current request, not a stale rejection.</summary>
    Reopened = 2,

    /// <summary>The caller had already been approved, so there is nothing to
    /// request. The service treats this as already-joined.</summary>
    AlreadyApproved = 3,
}
