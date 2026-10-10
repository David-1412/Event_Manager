using SportMeet.Domain.Enums;

namespace SportMeet.Application.Admin;

/// <summary>
/// Administrator user management: list people and change their role.
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

    /// <summary>Move an account to any role: Member, Creator or Admin, in either
    /// direction. Admin-only (<c>ManageUsers</c>); audited; refuses to take the last
    /// Admin down.</summary>
    Task<RoleChangeResultDto> ChangeRoleAsync(Guid userId, UserRole? role, CancellationToken ct = default);
}
