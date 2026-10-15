using SportMeet.Application.Events;

namespace SportMeet.Application.Profiles;

/// <summary>
/// Persistence seam for the public profile read side.
///
/// Every method takes the subject's id and returns only what is publicly disclosable.
/// The visibility rules are enforced here, in SQL, rather than in the service: a filter
/// expressed as a WHERE clause cannot be forgotten by a future caller the way a
/// <c>.Where()</c> in one service method can, and the same reasoning already governs the
/// join-request methods on <see cref="IEventRepository"/>.
///
/// Nothing here is scoped to the caller. That is what makes these safe to sit behind an
/// anonymous endpoint, and it is also why there is no overload taking a viewer - if one
/// ever appears, it needs a privacy review, not just a new query.
/// </summary>
public interface IProfileRepository
{
    /// <summary>The profile subject, or null when no such user exists (the endpoint
    /// answers 404, and must not answer 200 with an empty profile - an empty profile
    /// for a made-up id is a claim that the id means something).</summary>
    Task<ProfileSubject?> FindSubjectAsync(Guid userId, CancellationToken ct = default);

    /// <summary>All four Overview numbers in one round-trip. Four separate count
    /// queries would be the obvious implementation and the wrong one: the profile page
    /// asks for all four on first paint, so they belong in a single pass.</summary>
    Task<ProfileStatsDto> GetStatsAsync(Guid userId, CancellationToken ct = default);

    /// <summary>Publicly visible events hosted by the user, newest start first.
    /// Applies the same fail-closed status exclusion as browse, so a Draft,
    /// PendingReview or Rejected event never appears on someone's public profile.</summary>
    Task<List<FeedRow>> ListPublicHostedAsync(Guid userId, CancellationToken ct = default);

    /// <summary>Hosted events that start from now on, cancelled and unpublished
    /// included out. Ordered soonest first - this is the "come to their events" list.</summary>
    Task<List<FeedRow>> ListPublicUpcomingAsync(Guid userId, CancellationToken ct = default);

    /// <summary>Upcoming events the user has joined, restricted to Public events.
    /// Private events are excluded in SQL: the join row would confirm the existence of
    /// an event that is not discoverable, which is precisely the leak the spec calls
    /// out as unacceptable.</summary>
    Task<List<FeedRow>> ListPublicJoinedUpcomingAsync(Guid userId, CancellationToken ct = default);

    /// <summary>The activity feed: created / hosted / joined / interested merged,
    /// newest first, capped at <paramref name="limit"/>.
    ///
    /// Built as a UNION over the four sources rather than four queries merged in
    /// memory, so the limit applies in the database and an account with thousands of
    /// joins does not pull them all to serve twelve rows.</summary>
    Task<List<ProfileActivityDto>> GetActivityAsync(
        Guid userId, int limit, CancellationToken ct = default);

    /// <summary>How many people follow this user. Public, and independent of who is
    /// asking.</summary>
    Task<int> CountFollowersAsync(Guid userId, CancellationToken ct = default);

    /// <summary>Whether <paramref name="followerId"/> follows <paramref name="userId"/>.
    ///
    /// The one method on this interface that takes a caller, and the exception proves
    /// the rule stated above: it exists only to fill a per-caller flag on a response
    /// whose rest is public, it cannot widen what is disclosed (the answer is already
    /// known to the person asking), and it must never be handed an id the caller
    /// supplied - pass the authenticated identity or this becomes a way to test whether
    /// a stranger follows someone.</summary>
    Task<bool> IsFollowingAsync(Guid userId, Guid followerId, CancellationToken ct = default);

    /// <summary>Record a follow. Returns false when the edge already existed, which the
    /// service treats as success - following twice is not an error, it is a retry.
    ///
    /// Implemented as insert-and-catch rather than exists-then-insert: the latter has
    /// both taps pass the read and one then fail on the primary key, so the constraint
    /// has to be handled either way and handling it as the normal case is what makes
    /// the endpoint idempotent.</summary>
    Task<bool> AddFollowAsync(
        Guid followerUserId, Guid followedUserId, DateTimeOffset createdAt,
        CancellationToken ct = default);

    /// <summary>Remove a follow. Returns false when there was nothing to remove, which
    /// is likewise success.</summary>
    Task<bool> RemoveFollowAsync(
        Guid followerUserId, Guid followedUserId, CancellationToken ct = default);

    /// <summary>The ids whose follow notifications this user receives.
    ///
    /// Not exposed through any endpoint: a follower list is a person's social graph and
    /// nothing in the product asks to display one. It exists for the fan-out, which is
    /// the only consumer that needs the identities rather than the total.</summary>
    Task<List<Guid>> ListFollowersAsync(Guid followedUserId, CancellationToken ct = default);

    /// <summary>The follower ids to notify about one event, in one round-trip and with
    /// the subject's display name attached because the message names them.
    ///
    /// The eligibility rules are applied here rather than handed back for the caller to
    /// apply: a query that returns follower ids <em>only when the event qualifies</em>
    /// cannot be called into producing notifications for a draft, where a boolean the
    /// caller is expected to have checked can. Returns an empty pair for a Private or
    /// unpublished event, and for a subject with no followers.</summary>
    Task<(IReadOnlyList<Guid> FollowerIds, string? HostName)> ListFollowersToNotifyAsync(
        Domain.Entities.Event published, CancellationToken ct = default);
}

/// <summary>The user's own public fields, kept separate from
/// <see cref="ProfileSummaryDto"/> so the stats have their own query and the two cannot
/// be confused for one row. Notably absent: Email and AuthUid.</summary>
/// <param name="Id">The profile subject's internal id.</param>
/// <param name="Name">Display name.</param>
/// <param name="PhotoUrl">Avatar URL, or null for the initials avatar.</param>
/// <param name="Role">The UserRole name.</param>
/// <param name="CreatedAt">Account creation, rendered as "member since".</param>
public sealed record ProfileSubject(
    Guid Id,
    string Name,
    string? PhotoUrl,
    string Role,
    DateTimeOffset CreatedAt);
