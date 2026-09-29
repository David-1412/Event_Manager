using Microsoft.EntityFrameworkCore;
using SportMeet.Application.Common;
using SportMeet.Domain.Entities;
using SportMeet.Infrastructure.Persistence;

namespace SportMeet.Infrastructure.Identity;

/// <summary>
/// Maps a verified Firebase identity to a <c>users</c> row, creating it the first time
/// that identity appears. This is why there is no separate sign-up endpoint: the first
/// authenticated request carries a display name and (for email sign-in) an address, and
/// those are enough to seed the row the domain keys on.
///
/// Keyed on <c>auth_uid</c> (uniquely indexed). The best-effort fields refresh only
/// when present in the token, so a later Google sign-in that supplies a photo fills it
/// in without a nameless request ever blanking an existing value.
/// </summary>
public sealed class EfUserStore(AppDbContext db) : IUserStore
{
    public async Task<Guid> ResolveUserIdAsync(VerifiedIdentity identity, CancellationToken ct = default)
    {
        var existing = await db.Users.FirstOrDefaultAsync(u => u.AuthUid == identity.AuthUid, ct);
        if (existing is not null)
        {
            Touch(existing, identity);
            if (db.ChangeTracker.HasChanges()) await db.SaveChangesAsync(ct);
            return existing.Id;
        }

        var created = new User
        {
            Id = Guid.NewGuid(),
            AuthUid = identity.AuthUid,
            // The token's name, or the email's local part, or a neutral label. Never
            // blank: users.name is NOT NULL and the display name is user-facing.
            Name = identity.DisplayName is { Length: > 0 } n ? Truncate(n, 120)
                 : identity.Email is { Length: > 0 } e ? Truncate(e.Split('@')[0], 120)
                 : "Member",
            Email = string.IsNullOrWhiteSpace(identity.Email) ? null : Truncate(identity.Email, 320),
            PhotoUrl = string.IsNullOrWhiteSpace(identity.PhotoUrl) ? null : Truncate(identity.PhotoUrl, 2048),
        };
        db.Users.Add(created);
        await db.SaveChangesAsync(ct);
        return created.Id;
    }

    private static void Touch(User user, VerifiedIdentity identity)
    {
        if (identity.DisplayName is { Length: > 0 } name) user.Name = Truncate(name, 120);
        if (identity.Email is { Length: > 0 } email && string.IsNullOrEmpty(user.Email))
            user.Email = Truncate(email, 320);
        if (identity.PhotoUrl is { Length: > 0 } photo && string.IsNullOrEmpty(user.PhotoUrl))
            user.PhotoUrl = Truncate(photo, 2048);
    }

    private static string Truncate(string value, int max)
        => value.Length <= max ? value : value[..max];
}
