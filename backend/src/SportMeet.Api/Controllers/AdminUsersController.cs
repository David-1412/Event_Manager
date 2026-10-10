using Microsoft.AspNetCore.Mvc;
using SportMeet.Api.Common;
using SportMeet.Application.Admin;

namespace SportMeet.Api.Controllers;

/// <summary>
/// Administrator user management: list everyone and assign roles. Everything here
/// is behind <see cref="RequireAdminAttribute"/> — a signed-in Member gets 404,
/// the same answer as a nonexistent resource, so the admin surface is not
/// discoverable from the outside.
///
/// Separate controller rather than a route on <c>EventsController</c> because the
/// authorization rule differs: events are largely public reads with per-row
/// ownership checks, and users are a wholly privileged read of every account.
/// Mixing them would mean one controller with two incompatible default policies.
///
/// The pending-event review queue deliberately stays where it is
/// (<c>GET /api/events/reviews/pending</c> plus <c>/approve</c> and
/// <c>/reject</c>) — it already enforces Admin through the service layer and the
/// admin pages read those same endpoints, so moving them would break the API
/// contract for no gain.
/// </summary>
[ApiController]
[Route("api/admin/users")]
[RequireAdmin]
public class AdminUsersController(IUserAdminService users) : ControllerBase
{
    public sealed record SetRoleRequest(SportMeet.Domain.Enums.UserRole Role);

    /// <summary>Every account, newest first, with the role and signup date the table
    /// renders. Also carries the true admin count so the client can decide whether a
    /// demote is offerable without paging through the list.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(AdminUserListDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<AdminUserListDto>> List(CancellationToken ct)
        => Ok(await users.ListUsersAsync(ct));

    /// <summary>Give a Member the Admin role. Audited. Idempotent: promoting an
    /// existing Admin returns their current state and writes nothing.</summary>
    [HttpPost("{id:guid}/promote")]
    [ProducesResponseType(typeof(RoleChangeResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<RoleChangeResultDto>> Promote(Guid id, CancellationToken ct)
        => Ok(await users.PromoteAsync(id, ct));

    /// <summary>Reduce an Admin to Member. Audited. Refuses with 422 when this is
    /// the last Admin account, since an empty admin table cannot be refilled through
    /// the API that requires an Admin.</summary>
    [HttpPost("{id:guid}/demote")]
    [ProducesResponseType(typeof(RoleChangeResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<RoleChangeResultDto>> Demote(Guid id, CancellationToken ct)
        => Ok(await users.DemoteAsync(id, ct));

    /// <summary>Assign any supported role. The existing promote/demote routes
    /// remain available for older clients.</summary>
    [HttpPut("{id:guid}/role")]
    [ProducesResponseType(typeof(RoleChangeResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<RoleChangeResultDto>> SetRole(
        Guid id, [FromBody] SetRoleRequest request, CancellationToken ct)
        => Ok(await users.SetRoleAsync(id, request.Role, ct));
}
