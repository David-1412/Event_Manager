using Microsoft.AspNetCore.Mvc;
using SportMeet.Api.Common;
using SportMeet.Application.Events;
using SportMeet.Domain.Enums;

namespace SportMeet.Api.Controllers;

[ApiController]
[Route("api/admin/events")]
[RequireAdmin]
public sealed class AdminEventsController(IAdminEventService events) : ControllerBase
{
    /// <summary>Lists active events across all dates with optional search, time-frame,
    /// status, and pagination filters.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(AdminEventPage), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<AdminEventPage>> List(
        [FromQuery] string? q,
        [FromQuery] string? timeFrame,
        [FromQuery] EventStatus? status,
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        CancellationToken ct)
    {
        var frame = ParseTimeFrame(timeFrame);
        return Ok(await events.ListAsync(
            new AdminEventQuery(q, frame, status, page ?? 1, pageSize ?? 20), ct));
    }

    /// <summary>Updates an event's editable fields and tags.</summary>
    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(AdminEventDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<AdminEventDto>> Update(
        Guid id,
        [FromBody] UpdateAdminEventRequest request,
        CancellationToken ct)
        => Ok(await events.UpdateAsync(id, request, ct));

    /// <summary>Hides an event while retaining its record and participation history.</summary>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await events.DeleteAsync(id, ct);
        return NoContent();
    }

    private static AdminEventTimeFrame? ParseTimeFrame(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        if (Enum.TryParse<AdminEventTimeFrame>(value, true, out var result)) return result;
        throw new SportMeet.Application.Common.DomainRuleException(
            "Time frame must be Past, Current, or Future.");
    }
}
