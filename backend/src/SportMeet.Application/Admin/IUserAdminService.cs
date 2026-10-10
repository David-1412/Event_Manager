using SportMeet.Domain.Enums;

namespace SportMeet.Application.Admin;

/// <summary>
/// Administrator user management: list people and change roles.
///
/// The one rule with real consequences is the last-admin floor. Everything else
/// here is a read or a one-column write, but this rule is why the service exists
/// rather than the controller calling the repository directly — it is the
/// difference between an app with a way back in and an app whose admin table is
/// empty and unreachable through the API that guards it.
/// </summary>
public interface IUserAdminService
{
    /// <summary>All users for the admin table. Admin-only.</summary>
    Task<AdminUserListDto> ListUsersAsync(CancellationToken ct = default);

    /// <summary>Give a Member the Admin role. Admin-only; audited.</summary>
    Task<RoleChangeResultDto> PromoteAsync(Guid userId, CancellationToken ct = default);

    /// <summary>Reduce an Admin to Member. Admin-only; audited; refuses to take the
    /// last one down.</summary>
    Task<RoleChangeResultDto> DemoteAsync(Guid userId, CancellationToken ct = default);

    /// <summary>Set an account to any supported role. Admin-only; audited, and
    /// refuses to remove the last Admin.</summary>
    Task<RoleChangeResultDto> SetRoleAsync(Guid userId, UserRole role, CancellationToken ct = default);
}
