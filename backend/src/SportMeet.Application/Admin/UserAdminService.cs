using Microsoft.Extensions.Logging;
using SportMeet.Application.Common;
using SportMeet.Domain.Enums;

namespace SportMeet.Application.Admin;

/// <summary>
/// User management for administrators. Both writes are audited, and both refuse
/// to run without a verified administrator behind them.
///
/// <see cref="RequireAdmin"/> answers <see cref="NotFoundException"/> rather than
/// a 403 for a caller who is merely signed in: the user list is a resource whose
/// <em>existence</em> is itself privileged, and a 403 confirms to a Member that
/// the endpoint is real and that they are not allowed — which is the same
/// reasoning the pending-review queue already follows. A Member is not told about
/// the admin surface at all.
/// </summary>
public sealed class UserAdminService(
    IUserAdminRepository users,
    ICurrentUser currentUser,
    ILogger<UserAdminService> logger) : IUserAdminService
{
    /// <summary>The table is small (one row per person who has ever signed in) and
    /// the admin page has no pagination yet, so one page is the whole list. Bounded
    /// rather than unbounded so a runaway count cannot take the request down.</summary>
    private const int ListLimit = 500;

    public async Task<AdminUserListDto> ListUsersAsync(CancellationToken ct = default)
    {
        RequireAdmin();

        var items = await users.ListAsync(ListLimit, ct);
        var adminCount = await users.CountAdminsAsync(ct);
        return new AdminUserListDto(items, adminCount, items.Count);
    }

    public Task<RoleChangeResultDto> PromoteAsync(Guid userId, CancellationToken ct = default)
        => SetRoleAsync(userId, UserRole.Admin, ct);

    public Task<RoleChangeResultDto> DemoteAsync(Guid userId, CancellationToken ct = default)
        => SetRoleAsync(userId, UserRole.Member, ct);

    public Task<RoleChangeResultDto> SetRoleAsync(
        Guid userId, UserRole role, CancellationToken ct = default)
    {
        if (!Enum.IsDefined(role))
        {
            throw new DomainRuleException("Choose a valid user role.");
        }

        return ChangeRoleAsync(userId, role, ct);
    }

    /// <summary>
    /// The single path both writes take, so the guard and the audit line cannot be
    /// applied to one and forgotten on the other.
    /// </summary>
    private async Task<RoleChangeResultDto> ChangeRoleAsync(
        Guid userId, UserRole target, CancellationToken ct)
    {
        var actorId = RequireAdmin();
        var actorIsAdmin = currentUser.IsAdmin;

        var target0 = await users.FindAsync(userId, ct)
            ?? throw new NotFoundException("User", userId, userId);

        // Asking for the role they already have is not an error worth a 422 — the
        // client's row is a snapshot, and the honest answer to a stale one is the
        // current state, not a refusal. No audit line is written, because nothing
        // changed.
        if (target0.Role == target)
        {
            return new RoleChangeResultDto(target0, await users.CountAdminsAsync(ct));
        }

        // The floor. Demoting the final Admin locks every account out of the admin
        // API, including the person doing the demoting, and there is no
        // self-service way back in — recovery would mean editing the database by
        // hand. Refusing is cheaper than that, and cheaper than the alternative
        // reading of "the last admin may demote themselves" which still leaves the
        // role permanently empty once they sign out.
        var isDemotion = (int)target < (int)target0.Role;
        if (isDemotion)
        {
            var admins = await users.CountAdminsAsync(ct);
            if (admins <= 1)
            {
                // Name the actor when they are trying to demote themselves, since
                // that is the common case and the bare rule reads like a bug
                // otherwise.
                var self = actorId == userId;
                throw new DomainRuleException(self
                    ? "You're the only Admin. Promote someone else first, then demote yourself."
                    : "This is the only Admin account. Promote someone else first.");
            }
        }

        var action = (int)target > (int)target0.Role ? RoleChangeAction.Promoted : RoleChangeAction.Demoted;

        // Checked above, written conditionally here: two admins acting on the same
        // row at once must not both succeed, and the loser's expected role no longer
        // matches so it writes nothing.
        if (!await users.TrySetRoleAsync(userId, target0.Role, target, ct))
        {
            throw new DomainRuleException("This user's role was just changed by someone else. Refresh and try again.");
        }

        var at = DateTimeOffset.UtcNow;
        await users.AddAuditAsync(userId, actorId, target0.Role, target, action, at, ct);

        // The trail is the point of the feature, so a successful role change with no
        // log line is worth shouting about even though the write itself succeeded.
        logger.LogWarning(
            "UserRoleChanged action={Action} targetUserId={TargetUserId} fromRole={FromRole} toRole={ToRole} actorUserId={ActorUserId} actorIsAdmin={ActorIsAdmin} at={At}",
            action, userId, target0.Role, target, actorId, actorIsAdmin, at);

        var updated = target0 with { Role = target, CreatedAt = target0.CreatedAt };
        var adminCount = await users.CountAdminsAsync(ct);
        return new RoleChangeResultDto(updated, adminCount);
    }

    /// <summary>Assert the caller is a verified administrator, returning their id for
    /// the audit line. Throws <see cref="NotFoundException"/> for anyone else — see
    /// the class remark for why that is not a 403.</summary>
    private Guid RequireAdmin()
    {
        var userId = currentUser.UserId
            ?? throw new DomainRuleException("You must be signed in to manage users.");

        if (!currentUser.IsAdmin)
        {
            throw new NotFoundException("User", userId, userId);
        }

        return userId;
    }
}
