using Microsoft.Extensions.Logging.Abstractions;
using SportMeet.Application.Admin;
using SportMeet.Application.Common;
using SportMeet.Domain.Enums;

namespace SportMeet.Import.Tests;

/// <summary>
/// Role management at the layer that owns the rules: who may change roles, every
/// transition between Member, Creator and Admin, the audit label each one gets,
/// and the last-Admin floor. The repository is an in-memory double.
/// </summary>
public class UserAdminServiceTests
{
    private static readonly Guid ActorId = Guid.NewGuid();
    private static readonly Guid TargetId = Guid.NewGuid();

    private sealed class Rig
    {
        public FakeUserRepo Repo = new();
        public UserRole ActorRole = UserRole.Admin;

        public UserAdminService Service => new(
            Repo, new FakeUser(ActorId, ActorRole), NullLogger<UserAdminService>.Instance);

        public Rig With(Guid id, UserRole role)
        {
            Repo.Users[id] = role;
            return this;
        }
    }

    private sealed class FakeUser(Guid? userId, UserRole role) : ICurrentUser
    {
        public Guid? UserId => userId;
        public bool IsDemo => false;
        public UserRole Role => role;
    }

    private sealed class FakeUserRepo : IUserAdminRepository
    {
        public readonly Dictionary<Guid, UserRole> Users = new();
        public readonly List<(Guid Target, UserRole From, UserRole To, RoleChangeAction Action)> Audit = new();

        private AdminUserDto Dto(Guid id) => new(id, "Someone", "someone@example.com", Users[id], DateTimeOffset.UnixEpoch);

        public Task<List<AdminUserDto>> ListAsync(int limit, CancellationToken ct = default)
            => Task.FromResult(Users.Keys.Select(Dto).ToList());

        public Task<int> CountAdminsAsync(CancellationToken ct = default)
            => Task.FromResult(Users.Values.Count(r => r == UserRole.Admin));

        public Task<AdminUserDto?> FindAsync(Guid userId, CancellationToken ct = default)
            => Task.FromResult(Users.ContainsKey(userId) ? Dto(userId) : null);

        public Task<bool> TrySetRoleAsync(Guid userId, UserRole expectedFromRole, UserRole toRole, CancellationToken ct = default)
        {
            if (!Users.TryGetValue(userId, out var current) || current != expectedFromRole)
            {
                return Task.FromResult(false);
            }

            Users[userId] = toRole;
            return Task.FromResult(true);
        }

        public Task AddAuditAsync(Guid targetUserId, Guid? actorUserId, UserRole fromRole, UserRole toRole,
            RoleChangeAction action, DateTimeOffset at, CancellationToken ct = default)
        {
            Audit.Add((targetUserId, fromRole, toRole, action));
            return Task.CompletedTask;
        }
    }

    // ---- who may manage users --------------------------------------------------

    [Theory]
    [InlineData(UserRole.Member)]
    [InlineData(UserRole.Creator)]
    public async Task Non_admins_cannot_list_users_or_change_roles(UserRole actorRole)
    {
        var rig = new Rig { ActorRole = actorRole }.With(ActorId, actorRole).With(TargetId, UserRole.Member);

        await Assert.ThrowsAsync<NotFoundException>(() => rig.Service.ListUsersAsync());
        await Assert.ThrowsAsync<NotFoundException>(() => rig.Service.ChangeRoleAsync(TargetId, UserRole.Creator));
        // Including promoting themselves.
        await Assert.ThrowsAsync<NotFoundException>(() => rig.Service.ChangeRoleAsync(ActorId, UserRole.Admin));

        Assert.Equal(UserRole.Member, rig.Repo.Users[TargetId]);
        Assert.Equal(actorRole, rig.Repo.Users[ActorId]);
        Assert.Empty(rig.Repo.Audit);
    }

    // ---- every transition --------------------------------------------------------

    [Theory]
    [InlineData(UserRole.Member, UserRole.Creator, RoleChangeAction.Promoted)]
    [InlineData(UserRole.Creator, UserRole.Member, RoleChangeAction.Demoted)]
    [InlineData(UserRole.Member, UserRole.Admin, RoleChangeAction.Promoted)]
    [InlineData(UserRole.Admin, UserRole.Member, RoleChangeAction.Demoted)]
    [InlineData(UserRole.Creator, UserRole.Admin, RoleChangeAction.Promoted)]
    [InlineData(UserRole.Admin, UserRole.Creator, RoleChangeAction.Demoted)]
    public async Task Admin_can_move_a_user_between_any_two_roles(
        UserRole from, UserRole to, RoleChangeAction expectedAction)
    {
        var rig = new Rig().With(ActorId, UserRole.Admin).With(TargetId, from);

        var result = await rig.Service.ChangeRoleAsync(TargetId, to);

        Assert.Equal(to, result.User.Role);
        Assert.Equal(to, rig.Repo.Users[TargetId]);
        Assert.Equal((TargetId, from, to, expectedAction), Assert.Single(rig.Repo.Audit));
        Assert.Equal(rig.Repo.Users.Values.Count(r => r == UserRole.Admin), result.AdminCount);
    }

    [Fact]
    public async Task Asking_for_the_current_role_writes_nothing()
    {
        var rig = new Rig().With(ActorId, UserRole.Admin).With(TargetId, UserRole.Creator);

        var result = await rig.Service.ChangeRoleAsync(TargetId, UserRole.Creator);

        Assert.Equal(UserRole.Creator, result.User.Role);
        Assert.Empty(rig.Repo.Audit);
    }

    [Theory]
    [InlineData(null)]
    [InlineData((UserRole)7)]
    public async Task Missing_or_unknown_role_is_refused(UserRole? role)
    {
        var rig = new Rig().With(ActorId, UserRole.Admin).With(TargetId, UserRole.Member);

        await Assert.ThrowsAsync<DomainRuleException>(() => rig.Service.ChangeRoleAsync(TargetId, role));
        Assert.Equal(UserRole.Member, rig.Repo.Users[TargetId]);
    }

    // ---- the last-Admin floor -------------------------------------------------------

    [Theory]
    [InlineData(UserRole.Member)]
    [InlineData(UserRole.Creator)]
    public async Task The_last_Admin_cannot_be_moved_to_any_other_role(UserRole to)
    {
        var rig = new Rig().With(ActorId, UserRole.Admin).With(TargetId, UserRole.Creator);

        var ex = await Assert.ThrowsAsync<DomainRuleException>(() => rig.Service.ChangeRoleAsync(ActorId, to));

        Assert.Contains("only Admin", ex.Message);
        Assert.Equal(UserRole.Admin, rig.Repo.Users[ActorId]);
        Assert.Empty(rig.Repo.Audit);
    }

    [Fact]
    public async Task An_Admin_can_step_down_while_another_Admin_remains()
    {
        var rig = new Rig().With(ActorId, UserRole.Admin).With(TargetId, UserRole.Admin);

        var result = await rig.Service.ChangeRoleAsync(ActorId, UserRole.Creator);

        Assert.Equal(UserRole.Creator, rig.Repo.Users[ActorId]);
        Assert.Equal(1, result.AdminCount);
    }
}

/// <summary>The wire form of <c>POST /api/admin/users/{id}/role</c>, read with the
/// same converters Program.cs registers on the MVC JSON options.</summary>
public class ChangeRoleRequestJsonTests
{
    private static ChangeRoleRequest? Read(string json)
    {
        var options = new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web);
        SportMeet.Api.Serialization.JsonOptionsConfiguration.ConfigureEnumConverters(options);
        return System.Text.Json.JsonSerializer.Deserialize<ChangeRoleRequest>(json, options);
    }

    [Theory]
    [InlineData("""{"role":"Creator"}""", UserRole.Creator)]
    [InlineData("""{"role":"admin"}""", UserRole.Admin)]
    [InlineData("""{"role":"Member"}""", UserRole.Member)]
    public void Role_names_bind(string json, UserRole expected)
        => Assert.Equal(expected, Read(json)!.Role);

    [Theory]
    [InlineData("""{"role":"Owner"}""")]
    [InlineData("""{"role":2}""")]
    [InlineData("""{"role":"2"}""")]
    [InlineData("""{}""")]
    public void Anything_else_binds_to_null_so_the_service_refuses_it(string json)
        => Assert.Null(Read(json)!.Role);
}
