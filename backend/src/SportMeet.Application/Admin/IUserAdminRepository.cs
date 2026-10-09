using SportMeet.Domain.Enums;

namespace SportMeet.Application.Admin;

/// <summary>
/// The persistence seam for administrator user management.
///
/// Separate from <c>IEventRepository</c> because it reads and writes a different
/// aggregate (people and their privileges, not events), and because the last-admin
/// guard needs a count and an update to be visible to each other — which is easier
/// to reason about when one repository owns both.
/// </summary>
public interface IUserAdminRepository
{
    /// <summary>Every user, newest account first, capped at <paramref name="limit"/>.
    /// No privacy filter: an administrator listing users is the one read in this
    /// system entitled to see all of them.</summary>
    Task<List<AdminUserDto>> ListAsync(int limit, CancellationToken ct = default);

    /// <summary>How many accounts hold <see cref="UserRole.Admin"/>. Counts the whole
    /// table, never a page, so the guard below cannot be fooled by a limit.</summary>
    Task<int> CountAdminsAsync(CancellationToken ct = default);

    /// <summary>The row for one account, or null when there is no such user.</summary>
    Task<AdminUserDto?> FindAsync(Guid userId, CancellationToken ct = default);

    /// <summary>
    /// Change a role <em>only if</em> it is still <paramref name="expectedFromRole"/>,
    /// returning whether the row was written. The expected-value test is what makes
    /// two admins demoting the same person at the same moment safe: the second one
    /// matches zero rows and the service turns that into a refresh-and-retry rather
    /// than a second audit line for a change that never happened.
    /// </summary>
    Task<bool> TrySetRoleAsync(
        Guid userId, UserRole expectedFromRole, UserRole toRole, CancellationToken ct = default);

    /// <summary>Append one audit line. Called only after a role change actually
    /// landed, so the trail never records an attempted-but-rejected change.</summary>
    Task AddAuditAsync(
        Guid targetUserId,
        Guid? actorUserId,
        UserRole fromRole,
        UserRole toRole,
        RoleChangeAction action,
        DateTimeOffset at,
        CancellationToken ct = default);
}
