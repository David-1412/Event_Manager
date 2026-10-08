using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using SportMeet.Api.Common;
using SportMeet.Application.Imports;
using SportMeet.Domain.Enums;

namespace SportMeet.Api.Controllers;

/// <summary>Body of <c>POST /api/imports/text</c>.</summary>
public sealed record ImportTextRequest(string? Text);

/// <summary>
/// Paste-to-event import. Every action needs a verified sign-in: extraction spends money
/// (model plus geocoder), so it is never anonymous, and the import record belongs to
/// whoever made it.
///
/// <c>publish-metrics</c> lives here because the two halves of the measurement are one
/// feature: the import records what was proposed, and this records what was published and
/// how long it took.
/// </summary>
[ApiController]
[Route("api")]
[RequireAuthenticatedUser]
public class ImportsController(IImportService imports) : ControllerBase
{
    [HttpPost("imports/text")]
    [EnableRateLimiting(ImportRateLimit.PolicyName)]
    [ProducesResponseType(typeof(ImportDraftDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<ImportDraftDto>> ImportText(
        [FromBody] ImportTextRequest request, CancellationToken ct)
    {
        try
        {
            return Ok(await imports.ImportAsync(new ImportInput(ImportInputKind.Text, request.Text), ct));
        }
        catch (ImportException ex)
        {
            var problem = ProblemDetailsDefaults.Import(ex.Code, ex.Message);
            return new ObjectResult(problem) { StatusCode = problem.Status };
        }
    }

    /// <summary>Fire-and-forget from the client: a failure here must never block a
    /// publish, so the only job is to record and say 204.</summary>
    [HttpPost("publish-metrics")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> PublishMetrics([FromBody] PublishMetricsRequest request, CancellationToken ct)
    {
        await imports.RecordPublishAsync(request, ct);
        return NoContent();
    }
}
