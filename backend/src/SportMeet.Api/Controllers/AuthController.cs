using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SportMeet.Api.Common;
using SportMeet.Application.Common;
using SportMeet.Domain.Enums;
using SportMeet.Infrastructure.Persistence;

namespace SportMeet.Api.Controllers;

/// <summary>The caller's own account, as the API understands it.</summary>
/// <param name="Id">The internal <c>users.id</c>.</param>
/// <param name="Role">The role the API will act on for this caller's next request.
/// Read from the row, not from the token, so a demotion shows up here immediately.</param>
/// <param name="Email">Address on the row, null when the token carried none.</param>
/// <param name="DisplayName">Name from the row, so the header can show what the API
/// calls this person rather than only what Firebase does.</param>
public sealed record CurrentUserDto(
    Guid Id,
    UserRole Role,
    string? Email,
    string? DisplayName);

/// <summary>
/// The one endpoint that tells a signed-in caller who the API thinks they are.
///
/// The client needs this for role-aware navigation and publishing. Firebase knows the
/// person, not their privilege in this app, and the role deliberately lives only on
/// the <c>users</c> row so that a demotion takes effect on the next request rather
/// than when the current token expires. That means it cannot be derived client-side
/// and has to be read back. It is also what decides whether the Admin nav item
/// renders at all — the requirement being that the admin surface is not merely
/// unreachable to a Member but invisible.
///
/// Only ever the caller's own record, and it requires a verified token: the demo
/// fallback is not an identity a client should be able to interrogate, which is the
/// same rule <see cref="RequireAuthenticatedUserAttribute"/> exists to enforce.
/// </summary>
[ApiController]
[Route("api/auth")]
[RequireAuthenticatedUser]
public class AuthController(
    ICurrentUser currentUser,
    IUserStore users,
    AppDbContext db) : ControllerBase
{
    /// <summary>The signed-in user's id and role. 401 without a verified token.</summary>
    [HttpGet("me")]
    [ProducesResponseType(typeof(CurrentUserDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<CurrentUserDto>> Me(CancellationToken ct)
    {
        var id = currentUser.UserId
            ?? throw new DomainRuleException("You must be signed in.");

        // The role comes from IUserStore, the same path ICurrentUser resolved it on,
        // so the number the client renders is the number the next request will be
        // authorised against. The profile columns are read here rather than through
        // the store because they are display-only and have no bearing on a decision.
        var role = await users.FindRoleByIdAsync(id, ct);

        var row = await db.Users.AsNoTracking()
            .Where(u => u.Id == id)
            .Select(u => new { u.Name, u.Email })
            .FirstOrDefaultAsync(ct);

        return Ok(new CurrentUserDto(id, role, row?.Email, row?.Name));
    }
}
