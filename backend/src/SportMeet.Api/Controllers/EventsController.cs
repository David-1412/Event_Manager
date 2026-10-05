using Microsoft.AspNetCore.Mvc;
using SportMeet.Application.Common;
using SportMeet.Application.Events;
using SportMeet.Domain.Entities;

namespace SportMeet.Api.Controllers;

/// <summary>Body of `GET /api/events/me/joined` — the current viewer's joined
/// event ids, as the frontend's `JoinedEnvelope`. camelCase policy leaves
/// `eventIds` unchanged.</summary>
public sealed record JoinedIdsDto(System.Collections.Generic.IReadOnlyList<System.Guid> EventIds);

[ApiController]
[Route("api/events")]
public class EventsController(IEventService events) : ControllerBase
{
    /// <summary>
    /// Browse. Anonymous by design: swr-fetcher.ts sends no Authorization header,
    /// so an authentication requirement here would break the listing page.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(PagedResult<EventListItemDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<EventListItemDto>>> List(
        [FromQuery] string? q,
        [FromQuery] string? search,
        [FromQuery] string? tag,
        [FromQuery] string? tags,
        [FromQuery] string? date,
        [FromQuery] int? radius,
        [FromQuery] string? sort,
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        [FromQuery] double? lat,
        [FromQuery] double? lng,
        CancellationToken ct)
    {
        var model = new EventQueryModel
        {
            // The client's param is `q`; IMPLEMENTATION_PLAN.md §3 calls it
            // `search`. Both are accepted so neither side is wrong.
            Search = FirstOf(q, search),
            // Normalized so a bookmarked ?tag=Tennis behaves like ?tag=tennis
            // rather than silently returning nothing against lowercase rows.
            TagName = TagNormalizer.Normalize(FirstOf(tag, tags)),
            DateFilter = ParseDateFilter(date),

            RadiusKm = radius is > 0 ? radius : null,
            OriginLat = lat,
            OriginLng = lng,
            Sort = string.Equals(sort, "distance", StringComparison.OrdinalIgnoreCase)
                ? EventSort.Distance
                : EventSort.Date,
            Page = page is > 0 ? page.Value : 1,
            PageSize = ClampPageSize(pageSize),
        };

        return Ok(await events.ListAsync(model, ct));
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(EventDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<EventDetailDto>> Get(Guid id, CancellationToken ct)
        => Ok(await events.GetAsync(id, ct));

    /// <summary>
    /// The current viewer's joined event ids, for My events. Anonymous-tolerant:
    /// with no identity configured it returns an empty list rather than 401, so the
    /// page still renders the Interested set. Declared before the id route so the
    /// literal "me" is never parsed as a guid.
    /// </summary>
    [HttpGet("me/joined")]
    [ProducesResponseType(typeof(JoinedIdsDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<JoinedIdsDto>> MyJoined(CancellationToken ct)
        => Ok(new JoinedIdsDto(await events.ListMyJoinedAsync(ct)));

    /// <summary>All events created by the current host, regardless of status or date.</summary>
    [HttpGet("me/hosting")]
    [ProducesResponseType(typeof(IReadOnlyList<EventListItemDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<EventListItemDto>>> MyHosting(CancellationToken ct)
        => Ok(await events.ListMyHostedAsync(ct));

    /// <summary>
    /// Create. Returns 201 with both Location and a body: the plan asks for
    /// Location only, but create-event-view.tsx reads created.id from the
    /// response, and an empty 201 there routes the user to /events/undefined.
    /// </summary>
    [HttpPost]
    [ProducesResponseType(typeof(EventDetailDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<EventDetailDto>> Create([FromBody] CreateEventDto dto, CancellationToken ct)
    {
        var created = await events.CreateAsync(dto, ct);
        return CreatedAtAction(nameof(Get), new { id = created.Id }, created);
    }

    /// <summary>
    /// Join. Bodyless POST - the caller is ICurrentUser, matching what
    /// use-events.ts's postParticipants sends. 200 with the fresh detail: the
    /// client reads participantCount from the body to settle its optimistic
    /// count, and isJoined so a page refresh cannot flip it back to Join.
    /// </summary>
    [HttpPost("{id:guid}/participants")]
    [ProducesResponseType(typeof(EventDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<EventDetailDto>> Join(Guid id, CancellationToken ct)
        => Ok(await events.JoinAsync(id, ct));

    /// <summary>Leave. Idempotent for a user who is not a participant; if the
    /// leaver is the host, the event is cancelled (spec §3).</summary>
    [HttpDelete("{id:guid}/participants")]
    [ProducesResponseType(typeof(EventDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<EventDetailDto>> Leave(Guid id, CancellationToken ct)
        => Ok(await events.LeaveAsync(id, ct));

    [HttpPatch("{id:guid}/cancel")]
    [ProducesResponseType(typeof(EventDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<EventDetailDto>> Cancel(Guid id, CancellationToken ct)
        => Ok(await events.CancelAsync(id, ct));

    [HttpPatch("{id:guid}/reopen")]
    [ProducesResponseType(typeof(EventDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<EventDetailDto>> Reopen(Guid id, CancellationToken ct)
        => Ok(await events.ReopenAsync(id, ct));

    private static string? FirstOf(string? first, string? second) => TrimToNull(first) ?? TrimToNull(second);

    private static string? TrimToNull(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>
    /// The client sends today|week|any (frontend/src/lib/query.ts). Anything
    /// unrecognised is treated as "any" rather than 400: a stale bookmarked URL
    /// must not blank the listing page, and there is no user-facing fix for it.
    /// </summary>
    private static EventDateFilter ParseDateFilter(string? date) => date?.Trim().ToLowerInvariant() switch
    {
        "today" => EventDateFilter.Today,
        "week" => EventDateFilter.Week,
        _ => EventDateFilter.Any,
    };

    private static int ClampPageSize(int? pageSize) => pageSize switch
    {
        null => EventQueryModel.DefaultPageSize,
        < 1 => EventQueryModel.DefaultPageSize,
        > EventQueryModel.MaxPageSize => EventQueryModel.MaxPageSize,
        _ => pageSize.Value,
    };
}
