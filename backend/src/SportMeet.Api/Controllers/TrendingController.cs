using Microsoft.AspNetCore.Mvc;
using SportMeet.Application.Trending;

namespace SportMeet.Api.Controllers;

/// <summary>
/// Monash Pulse - <c>GET /api/trending?window=today|3d|week</c>. Anonymous, like the
/// feeds it summarises: it lists only public events and visible posts, and names no
/// one.
/// </summary>
[ApiController]
[Route("api/trending")]
public class TrendingController(TrendingService trending) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<TrendingItemDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<IReadOnlyList<TrendingItemDto>>> Get(
        [FromQuery] string? window, CancellationToken ct) =>
        Ok(await trending.GetTopAsync(window, ct));
}
