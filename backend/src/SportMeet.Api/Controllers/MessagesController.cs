using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using SportMeet.Api.Common;
using SportMeet.Application.Messaging;

namespace SportMeet.Api.Controllers;

/// <summary>
/// Direct messages - <c>/api/messages/...</c>.
///
/// Every route needs a verified Firebase identity: <see cref="RequireAuthenticatedUserAttribute"/>
/// refuses the demo fallback, not just anonymous callers, so no configuration can
/// make one shared identity's inbox readable by visitors. Conversations are only
/// ever resolved for the caller; someone else's id is a 404, indistinguishable from
/// one that does not exist.
/// </summary>
[ApiController]
[Route("api/messages")]
[RequireAuthenticatedUser]
public class MessagesController(MessagingService messaging) : ControllerBase
{
    /// <summary>The caller's inbox, most recent first.</summary>
    [HttpGet("conversations")]
    [ProducesResponseType(typeof(IReadOnlyList<ConversationDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<ConversationDto>>> Conversations(CancellationToken ct) =>
        Ok(await messaging.ListConversationsAsync(ct));

    /// <summary>Open the thread with someone (created on first use). Sends nothing.</summary>
    [HttpPost("conversations")]
    [EnableRateLimiting(WriteRateLimit.Messages)]
    [ProducesResponseType(typeof(ConversationDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<ConversationDto>> Start([FromBody] StartConversationDto dto, CancellationToken ct) =>
        Ok(await messaging.StartAsync(dto, ct));

    [HttpGet("conversations/{id:guid}")]
    [ProducesResponseType(typeof(ConversationDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ConversationDto>> Conversation(Guid id, CancellationToken ct) =>
        Ok(await messaging.GetConversationAsync(id, ct));

    /// <param name="before">The previous page's <c>before</c>, for older messages.</param>
    [HttpGet("conversations/{id:guid}/messages")]
    [ProducesResponseType(typeof(MessagePage), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<MessagePage>> Messages(Guid id, [FromQuery] long? before, CancellationToken ct) =>
        Ok(await messaging.ListMessagesAsync(id, before, ct));

    [HttpPost("conversations/{id:guid}/messages")]
    [EnableRateLimiting(WriteRateLimit.Messages)]
    [ProducesResponseType(typeof(MessageDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<MessageDto>> Send(Guid id, [FromBody] SendMessageDto dto, CancellationToken ct) =>
        Ok(await messaging.SendAsync(id, dto, ct));

    [HttpPost("conversations/{id:guid}/read")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Read(Guid id, CancellationToken ct)
    {
        await messaging.MarkReadAsync(id, ct);
        return NoContent();
    }

    /// <summary>Unread messages across the inbox, for the header badge.</summary>
    [HttpGet("unread-count")]
    [ProducesResponseType(typeof(UnreadCountDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<UnreadCountDto>> UnreadCount(CancellationToken ct) =>
        Ok(new UnreadCountDto(await messaging.UnreadCountAsync(ct)));
}

public sealed record UnreadCountDto(int Count);
