using Microsoft.AspNetCore.Mvc;
using SportMeet.Api.Common;
using SportMeet.Application.Ingestion;

namespace SportMeet.Api.Controllers;

/// <summary>Body of POST /api/ingestion/preview and /run.</summary>
public sealed record ExtractNowRequest(
    string? Subject,
    string? FromAddr,
    string? Body,
    /// <summary>Skip persistence and return the proposal only. Lets prompt work
    /// happen without filling the queue with throwaway drafts.</summary>
    bool DryRun = false);

/// <summary>
/// The human review queue for AI-proposed events — **the caller's own drafts
/// only**. Every operation is scoped to the authenticated identity: Firebase ID
/// tokens are verified by the bearer handler and mapped to a <c>users</c> row,
/// and a request without a usable identity gets 401 rather than the demo
/// identity's queue. There is no shared/global queue anywhere behind this
/// controller; list, read, update, approve, reject and delete all filter on the
/// owner in SQL.
/// </summary>
[ApiController]
[Route("api/event-drafts")]
[RequireAuthenticatedUser]
public class EventDraftsController(IEventDraftService drafts) : ControllerBase
{
    /// <summary>The queue. Defaults to pending, since that is the only subset a
    /// reviewer acts on; pass a status to look at history.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<EventDraftDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<IReadOnlyList<EventDraftDto>>> List(
        [FromQuery] string? status = "pending",
        [FromQuery] int limit = 50,
        CancellationToken ct = default)
        => Ok(await drafts.ListAsync(status, limit, ct));

    /// <summary>Draft plus the original message body, for the side-by-side review.
    /// 404 + code "NotFound" for an unknown id, like the events endpoints.</summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(EventDraftDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<EventDraftDto>> Get(Guid id, CancellationToken ct)
        => Ok(await drafts.GetAsync(id, ct));

    /// <summary>
    /// Autosave the reviewer's in-progress edits (the create form's PUT target).
    /// Gaps are allowed — a draft is expected to be incomplete — and the saved
    /// payload's missing fields are recomputed so the queue's hints follow the
    /// edit. Returns the updated draft so the client can refresh its row without
    /// a second request.
    /// </summary>
    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(EventDraftDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<EventDraftDto>> Update(
        Guid id, [FromBody] UpdateDraftDto dto, CancellationToken ct)
        => Ok(await drafts.UpdateAsync(id, dto, ct));

    /// <summary>
    /// Approve. The body is a corrected <see cref="SportMeet.Application.Events.CreateEventDto"/>,
    /// i.e. the same shape POST /api/events takes, so the create form is the single
    /// implementation of event validation and approve inherits its 422s unchanged.
    /// Returns 201 with the real <c>EventDetailDto</c> so the reviewer can navigate
    /// straight to /events/{id}.
    /// </summary>
    [HttpPost("{id:guid}/approve")]
    [ProducesResponseType(typeof(SportMeet.Application.Events.EventDetailDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<SportMeet.Application.Events.EventDetailDto>> Approve(
        Guid id, [FromBody] ApproveDraftDto dto, CancellationToken ct)
    {
        var (created, _) = await drafts.ApproveAsync(id, dto, ct);
        // 201 with a body rather than a bare 204: a client that reads the id out of an
        // empty response navigates to /events/undefined.
        //
        // CreatedAtAction would resolve against *this* controller and emit a Location
        // of /api/event-drafts/{draft id} - a URL that returns a draft, not the event
        // this 201 promises. Constructed directly because EventsController is
        // attribute-routed without a route name to resolve.
        return Created($"/api/events/{created.Id}", created);
    }

    [HttpPost("{id:guid}/reject")]
    [ProducesResponseType(typeof(EventDraftDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<EventDraftDto>> Reject(
        Guid id, [FromBody] RejectDraftDto dto, CancellationToken ct)
        => Ok(await drafts.RejectAsync(id, dto, ct));

    /// <summary>Soft-delete the reviewer's own draft (Draft -&gt; Deleted). 204 on
    /// success; the owning user only — an unknown or foreign id is 404, so a draft's
    /// existence is never leaked across owners.</summary>
    [HttpPost("{id:guid}/delete")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await drafts.DeleteAsync(id, ct);
        return NoContent();
    }
}

