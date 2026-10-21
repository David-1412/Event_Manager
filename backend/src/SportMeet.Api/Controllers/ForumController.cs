using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using SportMeet.Api.Common;
using SportMeet.Application.Forum;

namespace SportMeet.Api.Controllers;

/// <summary>
/// The campus forum - <c>/api/forum/...</c>.
///
/// Reads are anonymous, like <c>GET /api/events</c>: a reader deciding whether the
/// forum is worth joining should see it first. Writing needs an identity, which
/// <see cref="ForumService"/> takes from <c>ICurrentUser</c> - never from the body.
/// Anonymous posting is a display rule, not a missing identity: the poster is still
/// signed in and still recorded.
/// </summary>
[ApiController]
[Route("api/forum")]
public class ForumController(ForumService forum, ForumCommentService comments) : ControllerBase
{
    [HttpGet("categories")]
    [ProducesResponseType(typeof(IReadOnlyList<ForumCategoryDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<ForumCategoryDto>>> Categories(CancellationToken ct) =>
        Ok(await forum.GetCategoriesAsync(ct));

    /// <param name="category">A category slug; omit (or "all") for every category.</param>
    /// <param name="sort">"hot" (default) or "new".</param>
    /// <param name="cursor">The previous page's <c>nextCursor</c>.</param>
    /// <param name="q">Text to find in titles and bodies.</param>
    /// <param name="tab">"all" (default), "following" (named posts by people you follow)
    /// or "mine" (your own, anonymous included). The last two need a signed-in caller.</param>
    [HttpGet("posts")]
    [ProducesResponseType(typeof(ForumPostPage), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<ForumPostPage>> Posts(
        [FromQuery] string? category,
        [FromQuery] string? sort,
        [FromQuery] string? cursor,
        [FromQuery] int? limit,
        [FromQuery] string? q,
        [FromQuery] string? tab,
        CancellationToken ct) =>
        Ok(await forum.ListPostsAsync(category, sort, cursor, limit, q, tab, ct));

    [HttpGet("posts/{id:guid}")]
    [ProducesResponseType(typeof(ForumPostDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ForumPostDto>> Post(Guid id, CancellationToken ct) =>
        Ok(await forum.GetPostAsync(id, ct));

    /// <summary>
    /// Record that the post page was opened. The view total only ever rises (it is the
    /// Monash Pulse score), and a signed-in reader's first ever view of someone else's
    /// post marks them Interested, which they can turn off - that never lowers views.
    /// Rate-limited so a script cannot pump a post up the list.
    /// </summary>
    [HttpPost("posts/{id:guid}/view")]
    [EnableRateLimiting(WriteRateLimit.Views)]
    [ProducesResponseType(typeof(ForumViewResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<ForumViewResultDto>> View(Guid id, CancellationToken ct) =>
        Ok(await forum.RecordViewAsync(id, ct));

    /// <summary>Mark a post "Interested". Idempotent: returns the settled state.</summary>
    [HttpPut("posts/{id:guid}/interest")]
    [ProducesResponseType(typeof(ForumInterestResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ForumInterestResultDto>> MarkInterested(Guid id, CancellationToken ct) =>
        Ok(await forum.SetInterestedAsync(id, interested: true, ct));

    /// <summary>Clear "Interested". Idempotent in the same direction.</summary>
    [HttpDelete("posts/{id:guid}/interest")]
    [ProducesResponseType(typeof(ForumInterestResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ForumInterestResultDto>> ClearInterested(Guid id, CancellationToken ct) =>
        Ok(await forum.SetInterestedAsync(id, interested: false, ct));

    [HttpPost("posts")]
    [ProducesResponseType(typeof(ForumPostDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<ForumPostDto>> Create([FromBody] CreateForumPostDto dto, CancellationToken ct)
    {
        var post = await forum.CreatePostAsync(dto, ct);
        return CreatedAtAction(nameof(Post), new { id = post.Id }, post);
    }

    /// <summary>The post's comments, each with its replies (one level), oldest first.</summary>
    [HttpGet("posts/{id:guid}/comments")]
    [ProducesResponseType(typeof(IReadOnlyList<ForumCommentDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IReadOnlyList<ForumCommentDto>>> Comments(Guid id, CancellationToken ct) =>
        Ok(await comments.ListAsync(id, ct));

    /// <summary>Comment on a post, or reply to a comment (<c>parentCommentId</c>).</summary>
    [HttpPost("posts/{id:guid}/comments")]
    [EnableRateLimiting(WriteRateLimit.Comments)]
    [ProducesResponseType(typeof(ForumCommentDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<ForumCommentDto>> AddComment(
        Guid id, [FromBody] CreateForumCommentDto dto, CancellationToken ct)
    {
        var comment = await comments.CreateAsync(id, dto, ct);
        return StatusCode(StatusCodes.Status201Created, comment);
    }

    /// <summary>Delete your own comment (admins: any). Soft: replies keep their place.</summary>
    [HttpDelete("comments/{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> DeleteComment(Guid id, CancellationToken ct)
    {
        await comments.DeleteAsync(id, ct);
        return NoContent();
    }
}
