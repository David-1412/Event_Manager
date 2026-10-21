using Microsoft.AspNetCore.Mvc;
using SportMeet.Api.Common;
using SportMeet.Application.Activity;

namespace SportMeet.Api.Controllers;

/// <summary>
/// The Likes / New Followers / Comments tabs on the Messages page -
/// <c>GET /api/notifications/{likes|follows|comments}</c>.
///
/// Personal, so a verified identity is required: the demo fallback is refused, as on
/// messages, so no configuration can show one shared account's activity to visitors.
/// The items are read live from the rows that record each action; only "read" is
/// stored.
/// </summary>
[ApiController]
[Route("api/notifications")]
[RequireAuthenticatedUser]
public class ActivityController(ActivityService activity) : ControllerBase
{
    [HttpGet("likes")]
    [ProducesResponseType(typeof(IReadOnlyList<ActivityItemDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<ActivityItemDto>>> Likes(CancellationToken ct) =>
        Ok(await activity.ListAsync(ActivityKind.Likes, ct));

    [HttpGet("follows")]
    [ProducesResponseType(typeof(IReadOnlyList<ActivityItemDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<ActivityItemDto>>> Follows(CancellationToken ct) =>
        Ok(await activity.ListAsync(ActivityKind.Follows, ct));

    [HttpGet("comments")]
    [ProducesResponseType(typeof(IReadOnlyList<ActivityItemDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<ActivityItemDto>>> Comments(CancellationToken ct) =>
        Ok(await activity.ListAsync(ActivityKind.Comments, ct));

    /// <summary>Unread counts for the three tab badges.</summary>
    [HttpGet("activity-counts")]
    [ProducesResponseType(typeof(ActivityCountsDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<ActivityCountsDto>> Counts(CancellationToken ct) =>
        Ok(await activity.UnreadCountsAsync(ct));

    /// <summary>Mark items read by their keys (as returned in each item).</summary>
    [HttpPost("activity-read")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> MarkRead([FromBody] MarkActivityReadDto dto, CancellationToken ct)
    {
        await activity.MarkReadAsync(dto, ct);
        return NoContent();
    }
}
