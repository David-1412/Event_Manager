using Microsoft.AspNetCore.Mvc;
using SportMeet.Application.Events;
using SportMeet.Application.Profiles;

namespace SportMeet.Api.Controllers;

/// <summary>
/// Public host / organization profiles - <c>GET /api/profiles/{userId}/...</c>.
/// </summary>
/// <remarks>
/// <para>
/// Anonymous by design, matching <c>GET /api/events</c>: discovering who runs an event
/// is the thing the browse flow is trying to do, and a caller who has not signed in is
/// exactly the person deciding whether to trust a stranger's tennis social. The
/// frontend's swr-fetcher sends no Authorization header, so an authentication
/// requirement here would break the page outright.
/// </para>
/// <para>
/// Because there is no caller to authorize, there is nothing here to gate, and that
/// makes the response types the entire privacy boundary. <see cref="ProfileSummaryDto"/>
/// and its neighbours carry no email, no auth uid, no analytics and no admin state, and
/// the repository filters unpublished and private events in SQL. The host-only surface -
/// <c>GET /api/events/{id}/analytics</c> - is not reachable from any route on this
/// controller, in any shape.
/// </para>
/// <para>
/// Unknown id is 404 rather than an empty profile. A 200 for a fabricated guid would
/// assert that the guid identifies a person, and the client would render a blank profile
/// that reads as "someone with no activity" instead of "no such someone".
/// </para>
/// </remarks>
[ApiController]
[Route("api/profiles")]
public class ProfilesController(ProfileService profiles) : ControllerBase
{
    /// <summary>The profile header and its four Overview numbers, in one call so the
    /// page's first paint is a single round-trip.</summary>
    [HttpGet("{userId:guid}")]
    [ProducesResponseType(typeof(ProfileSummaryDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ProfileSummaryDto>> Get(Guid userId, CancellationToken ct)
    {
        var summary = await profiles.GetSummaryAsync(userId, ct);
        return summary is null ? NotFound() : Ok(summary);
    }

    /// <summary>Every publicly visible event this user has hosted, newest start first.
    /// Draft, PendingReview and Rejected are absent - a profile is a public surface and
    /// those are not public events.</summary>
    [HttpGet("{userId:guid}/events")]
    [ProducesResponseType(typeof(IReadOnlyList<EventListItemDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<EventListItemDto>>> HostedEvents(Guid userId, CancellationToken ct) =>
        Ok(await profiles.GetHostedEventsAsync(userId, ct));

    /// <summary>Upcoming events this user is running. Soonest first.</summary>
    [HttpGet("{userId:guid}/upcoming")]
    [ProducesResponseType(typeof(IReadOnlyList<EventListItemDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<EventListItemDto>>> UpcomingEvents(Guid userId, CancellationToken ct) =>
        Ok(await profiles.GetUpcomingEventsAsync(userId, ct));

    /// <summary>Upcoming events this user has joined, restricted to Public events.
    /// Private participation is excluded in SQL, not filtered here.</summary>
    [HttpGet("{userId:guid}/joined")]
    [ProducesResponseType(typeof(IReadOnlyList<EventListItemDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<EventListItemDto>>> JoinedEvents(Guid userId, CancellationToken ct) =>
        Ok(await profiles.GetJoinedEventsAsync(userId, ct));

    /// <summary>The recent-activity feed: created / hosted / joined / interested, newest
    /// first, capped server-side.</summary>
    [HttpGet("{userId:guid}/activity")]
    [ProducesResponseType(typeof(IReadOnlyList<ProfileActivityDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<ProfileActivityDto>>> Activity(
        Guid userId, CancellationToken ct) =>
        Ok(await profiles.GetActivityAsync(userId, ct));

    /// <summary>
    /// Follow a creator, and start receiving a notification whenever one of their
    /// events becomes publicly visible.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The only endpoints on this controller that are not anonymous, and the reason the
    /// rest of it can stay that way: following is an act by a specific person, so it
    /// needs a verified one. Identity comes from <c>ICurrentUser</c> like every other
    /// write in the app - never from the body - which is what keeps
    /// <c>isFollowing</c> on the summary from becoming a way to ask whether a stranger
    /// follows someone.
    /// </para>
    /// <para>
    /// Idempotent. Following someone you already follow is a success carrying the
    /// settled state, not a 409: a retried tap after a dropped response must converge,
    /// and the duplicate rule is a database constraint rather than an API error. The
    /// same reasoning makes the self-follow case a 400 with a readable message rather
    /// than a CHECK-constraint violation surfacing as a 500.
    /// </para>
    /// </remarks>
    [HttpPost("{userId:guid}/follow")]
    [ProducesResponseType(typeof(ProfileFollowResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ProfileFollowResultDto>> Follow(Guid userId, CancellationToken ct) =>
        Ok(await profiles.FollowAsync(userId, ct));

    /// <summary>Unfollow. Idempotent in the same direction as the follow: removing a
    /// follow that is not there is already the requested state, so a retried tap
    /// converges.</summary>
    [HttpDelete("{userId:guid}/follow")]
    [ProducesResponseType(typeof(ProfileFollowResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ProfileFollowResultDto>> Unfollow(Guid userId, CancellationToken ct) =>
        Ok(await profiles.UnfollowAsync(userId, ct));
}
