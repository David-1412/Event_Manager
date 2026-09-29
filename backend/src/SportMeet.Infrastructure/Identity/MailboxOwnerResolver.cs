using Microsoft.EntityFrameworkCore;
using SportMeet.Application.Common;
using SportMeet.Domain.Entities;
using SportMeet.Infrastructure.Persistence;

namespace SportMeet.Infrastructure.Identity;

/// <summary>
/// Resolves the owner of the polled mailbox to a <c>users</c> row, creating it on
/// first sight.
///
/// This is the second half of "polled drafts are owned by the mailbox owner":
/// <see cref="Application.Ingestion.EmailProcessor"/> asks who owns the configured
/// mailbox (or whose address a message was addressed to), and this answers.
/// Resolution is case-insensitive on the address; matching prefers an existing
/// row's <c>email</c>, so a person who already signed in through Firebase gets
/// their polled drafts filed beside the ones they pasted. The created row is
/// keyed on <c>auth_uid</c> = the address, which no Firebase token can collide
/// with (Firebase uids are 28-character alphanumerics, never email-shaped).
///
/// An unresolvable address returns null rather than inventing an identity: the
/// caller decides the fallback (demo identity, then a per-mailbox placeholder),
/// so the policy stays in one place — <see cref="Application.Ingestion.EmailProcessor"/>.
/// </summary>
public sealed class MailboxOwnerResolver(AppDbContext db) : IMailboxOwnerResolver
{
    public async Task<Guid?> ResolveMailboxOwnerAsync(
        string? mailboxAddress, CancellationToken ct = default)
        => await ResolveByAddressAsync(mailboxAddress, null, ct);

    public async Task<Guid?> ResolveUserByAddressAsync(
        string? address, string? displayName, CancellationToken ct = default)
        => await ResolveByAddressAsync(address, displayName, ct);

    private async Task<Guid?> ResolveByAddressAsync(
        string? address, string? displayName, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(address)) return null;
        var normalized = address.Trim().ToLowerInvariant();

        var existing = await db.Users
            .FirstOrDefaultAsync(u => u.Email == normalized || u.AuthUid == normalized, ct);
        if (existing is not null) return existing.Id;

        var created = new User
        {
            Id = Guid.NewGuid(),
            AuthUid = Truncate(normalized, 128),
            Name = Truncate(
                string.IsNullOrWhiteSpace(displayName) ? normalized.Split('@')[0] : displayName!,
                120),
            Email = normalized.Contains('@') ? Truncate(normalized, 320) : null,
        };
        db.Users.Add(created);
        await db.SaveChangesAsync(ct);
        return created.Id;
    }

    private static string Truncate(string value, int max)
        => value.Length <= max ? value : value[..max];
}