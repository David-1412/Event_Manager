using Microsoft.EntityFrameworkCore;
using SportMeet.Application.Admin;
using SportMeet.Domain.Entities;
using SportMeet.Domain.Enums;
using SportMeet.Infrastructure.Persistence;

namespace SportMeet.Infrastructure.Persistence;

/// <summary>
/// EF implementation of <see cref="IUserAdminRepository"/>.
///
/// Reads are <c>AsNoTracking</c> everywhere: the table is rendered, never edited in
/// place, and a tracked user would collide with the instance the role update
/// attaches. The one write is a conditional <c>ExecuteUpdate</c> rather than
/// load-mutate-save, so the expected-role check and the UPDATE are a single
/// statement — the guard cannot be defeated by a second request reading the same
/// row between the read and the write.
/// </summary>
public sealed class UserAdminRepository(AppDbContext db) : IUserAdminRepository
{
    public async Task<List<AdminUserDto>> ListAsync(int limit, CancellationToken ct = default)
        => await db.Users.AsNoTracking()
            .OrderByDescending(u => u.CreatedAt)
            .ThenBy(u => u.Id)
            .Take(limit)
            .Select(u => new AdminUserDto(u.Id, u.Name, u.Email, u.Role, u.CreatedAt))
            .ToListAsync(ct);

    public async Task<int> CountAdminsAsync(CancellationToken ct = default)
        => await db.Users.AsNoTracking()
            .CountAsync(u => u.Role == UserRole.Admin, ct);

    public async Task<AdminUserDto?> FindAsync(Guid userId, CancellationToken ct = default)
        => await db.Users.AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => new AdminUserDto(u.Id, u.Name, u.Email, u.Role, u.CreatedAt))
            .FirstOrDefaultAsync(ct);

    public async Task<bool> TrySetRoleAsync(
        Guid userId, UserRole expectedFromRole, UserRole toRole, CancellationToken ct = default)
    {
        var affected = await db.Users
            .Where(u => u.Id == userId && u.Role == expectedFromRole)
            .ExecuteUpdateAsync(s => s.SetProperty(u => u.Role, toRole), ct);

        return affected == 1;
    }

    public async Task AddAuditAsync(
        Guid targetUserId,
        Guid? actorUserId,
        UserRole fromRole,
        UserRole toRole,
        RoleChangeAction action,
        DateTimeOffset at,
        CancellationToken ct = default)
    {
        db.UserRoleAudit.Add(new UserRoleAudit
        {
            Id = Guid.NewGuid(),
            TargetUserId = targetUserId,
            ActorUserId = actorUserId,
            FromRole = fromRole,
            ToRole = toRole,
            Action = action,
            CreatedAt = at,
        });

        await db.SaveChangesAsync(ct);
    }
}
