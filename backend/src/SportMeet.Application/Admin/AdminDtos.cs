using SportMeet.Domain.Enums;

namespace SportMeet.Application.Admin;

/// <summary>One row of the admin user table: enough to identify a person and act
/// on their role, and nothing else. Deliberately excludes photo, auth uid and any
/// event data — a user-management list has no business reading those, and every
/// field it does read is one an administrator has a reason to see.</summary>
/// <param name="Id">The internal <c>users.id</c>, which is what role changes key on.</param>
/// <param name="Name">Display name, shown beside the address so a blank email is still identifiable.</param>
/// <param name="Email">Null for accounts that arrived without an email claim.</param>
/// <param name="Role">Current role — the value the badges render.</param>
/// <param name="CreatedAt">When the row was created, i.e. when they first signed in.</param>
public sealed record AdminUserDto(
    Guid Id,
    string Name,
    string? Email,
    UserRole Role,
    DateTimeOffset CreatedAt);

/// <summary>The page of users the admin table renders.</summary>
/// <param name="Items">Users, newest role changes aside — ordered by creation.</param>
/// <param name="AdminCount">How many of <em>all</em> users are Admin, not just how
/// many appear on this page. The client needs the true total to decide whether a
/// demote is even offerable, and paging must not be able to understate it.</param>
/// <param name="TotalCount">Total users in the database.</param>
public sealed record AdminUserListDto(
    IReadOnlyList<AdminUserDto> Items,
    int AdminCount,
    int TotalCount);

/// <summary>Body of <c>POST /api/admin/users/{id}/role</c>.</summary>
/// <param name="Role">The role to move the account to. Nullable so a missing or
/// unrecognised value reaches the service as a clear 422 rather than silently
/// binding to the enum's default (Member).</param>
public sealed record ChangeRoleRequest(UserRole? Role);

/// <summary>The result of a role change, returned so the client can update the row
/// it has in place rather than refetching the whole table.</summary>
/// <param name="User">The user as they now are.</param>
/// <param name="AdminCount">Admins remaining after the change — lets the UI warn as
/// the last-admin floor approaches without a second request.</param>
public sealed record RoleChangeResultDto(AdminUserDto User, int AdminCount);
