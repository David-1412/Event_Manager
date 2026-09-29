using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SportMeet.Application.Common;
using SportMeet.Infrastructure.Persistence;

namespace SportMeet.Api.Controllers;

/// <summary>
/// Liveness plus reachability of the database. Not anonymous-only by policy -
/// it is simply not user data - and deliberately excluded from problem-details
/// shaping so an infrastructure monitor always gets a predictable payload.
/// </summary>
[ApiController]
[Route("healthz")]
[ApiExplorerSettings(GroupName = "ops")]
public class HealthController(AppDbContext db, ICurrentUser currentUser, ILogger<HealthController> logger) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> Get(CancellationToken ct)
    {
        try
        {
            await db.Database.CanConnectAsync(ct);
        }
        catch (Exception ex)
        {
            // A refused connection is a health result, not a fault to page on, so
            // it is logged at warning with the reason rather than surfacing a 500.
            logger.LogWarning(ex, "Health check could not reach the database.");
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { status = "unavailable", reason = "database" });
        }

        return Ok(new
        {
            status = "ok",
            // Surfacing demo identity here means a misconfigured deployment shows
            // up in the health payload instead of quietly serving demo relations.
            demoIdentity = currentUser.IsDemo,
        });
    }
}
