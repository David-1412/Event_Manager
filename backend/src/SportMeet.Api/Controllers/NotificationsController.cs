using Microsoft.AspNetCore.Mvc;
using SportMeet.Api.Common;
using SportMeet.Application.Notifications;

namespace SportMeet.Api.Controllers;

[ApiController]
[Route("api/notifications")]
[RequireAuthenticatedUser]
public sealed class NotificationsController(INotificationService notifications) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(NotificationListDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<NotificationListDto>> List(CancellationToken ct)
        => Ok(await notifications.ListAsync(ct));

    [HttpPatch("read")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> MarkAllRead(CancellationToken ct)
    {
        await notifications.MarkAllReadAsync(ct);
        return NoContent();
    }

    [HttpPatch("{id:guid}/read")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> MarkRead(Guid id, CancellationToken ct)
    {
        await notifications.MarkReadAsync(id, ct);
        return NoContent();
    }
}
