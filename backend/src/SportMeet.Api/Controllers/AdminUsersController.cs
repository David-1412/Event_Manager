using Microsoft.AspNetCore.Mvc;
using SportMeet.Api.Common;
using SportMeet.Application.Admin;
using SportMeet.Domain.Enums;

namespace SportMeet.Api.Controllers;

/// <summary>
/// Administrator user management: list everyone and change roles. Everything here
/// is behind <c>[RequirePermission(Permission.ManageUsers)]</c> — a signed-in
/// Member or Creator gets 404, the same answer as a nonexistent resource, so the
/// admin surface is not discoverable from the outside.
///
/// Separate controller rather than a route on <c>EventsController</c> because the
/// authorization rule differs: events are largely public reads with per-row
/// ownership checks, and users are a wholly privileged read of every account.
/// Mixing them would mean one controller with two incompatible default policies.
///
/// The pending-event review queue deliberately stays where it is
/// (<c>GET /api/events/reviews/pending</c> plus <c>/approve</c> and
/// <c>/reject</c>) — it is guarded by <c>Permission.ReviewEvents</c> there, and the
/// admin pages read those same endpoints, so moving them would break the API
/// contract for no gain.
/// </summary>
[ApiController]
[Route("api/admin/users")]
[RequirePermission(Permission.ManageUsers)]
public class AdminUsersController(IUserAdminService users) : ControllerBase
{
    /// <summary>Every account, newest first, with the role and signup date the table
    /// renders. Also carries the true admin count so the client can decide whether a
    /// demote is offerable without paging through the list.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(AdminUserListDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<AdminUserListDto>> List(CancellationToken ct)
        => Ok(await users.ListUsersAsync(ct));

    /// <summary>Move an account to Member, Creator or Admin. Audited. Idempotent:
    /// asking for the role they already hold returns their current state and writes
    /// nothing. Refuses with 422 when it would take the last Admin away, since an
    /// empty admin table cannot be refilled through the API that requires an Admin,
    /// and with 422 for a missing or unknown role.</summary>
    [HttpPost("{id:guid}/role")]
    [ProducesResponseType(typeof(RoleChangeResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<RoleChangeResultDto>> ChangeRole(
        Guid id, [FromBody] ChangeRoleRequest request, CancellationToken ct)
        => Ok(await users.ChangeRoleAsync(id, request.Role, ct));
}
