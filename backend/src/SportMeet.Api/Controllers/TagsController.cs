using Microsoft.AspNetCore.Mvc;
using SportMeet.Application.Events;

namespace SportMeet.Api.Controllers;

/// <summary>A popular tag plus how many visible events carry it, so the client can
/// show a count on the chip if it wants one.</summary>
public sealed record PopularTagDto(string Name, int Count);

/// <summary>
/// Tag vocabulary endpoints. Anonymous like the events endpoints, because
/// swr-fetcher.ts sends no Authorization header and both feed the public browse
/// and create screens.
/// </summary>
[ApiController]
[Route("api/tags")]
public class TagsController(IEventService events) : ControllerBase
{
    /// <summary>Home page's popular-tag strip. Empty array on a fresh database -
    /// the demo seed creates no tags - which the client renders as an empty state.
    /// Counts only visible events, so a chip never opens an empty filter.</summary>
    [HttpGet("popular")]
    [ProducesResponseType(typeof(IReadOnlyList<PopularTagDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<PopularTagDto>>> Popular([FromQuery] int limit = 12, CancellationToken ct = default)
    {
        var tags = await events.ListPopularTagsAsync(limit, ct);
        return Ok(tags.Select(t => new PopularTagDto(t.Name, t.Count)).ToList());
    }

    /// <summary>Create form's autocomplete: existing tags with a normalized prefix.
    /// Free entry is always allowed, so an empty result is not a dead end.</summary>
    [HttpGet("suggest")]
    [ProducesResponseType(typeof(IReadOnlyList<string>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<string>>> Suggest(
        [FromQuery] string? q,
        [FromQuery] int limit = 8,
        CancellationToken ct = default)
        => Ok(await events.SuggestTagsAsync(q, limit, ct));
}
