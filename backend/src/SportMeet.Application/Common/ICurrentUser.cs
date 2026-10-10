namespace SportMeet.Application.Common;

using SportMeet.Domain.Enums;

/// <summary>
/// The identity the current request acts as.
///
/// Milestone 1 has no authentication, but every relation the UI needs
/// (isHost / isJoined) is defined against an identity. Funnelling that through
/// this interface is what keeps the demo user out of the query code: when
/// Firebase lands, only the implementation changes and EventService is
/// untouched. The implementation itself lives in the Api layer because it reads
/// configuration.
/// </summary>
public interface ICurrentUser
{
    /// <summary>
    /// null means "anonymous, nobody is acting". Read endpoints must still work
    /// for null; write endpoints require a value.
    /// </summary>
    Guid? UserId { get; }

    /// <summary>True while the identity comes from configuration rather than a
    /// verified token. Lets logging and responses flag demo data honestly.</summary>
    bool IsDemo { get; }

    /// <summary>The acting user's role, resolved from their database row, so it is
    /// a fact about the person rather than about the token that carried them in,
    /// and a role change takes effect on the next request instead of when the token
    /// expires. <see cref="UserRole.Member"/> for an anonymous caller.
    ///
    /// Authorization reads this through <see cref="CurrentUserPermissions"/>, never
    /// by comparing roles directly.</summary>
    UserRole Role { get; }
}

/// <summary>
/// Permission checks for the acting user. An anonymous caller holds no permission,
/// whatever <see cref="ICurrentUser.Role"/> reports.
/// </summary>
public static class CurrentUserPermissions
{
    public static bool Has(this ICurrentUser user, Permission permission)
        => user.UserId is not null && user.Role.Has(permission);

    public static bool CanPublishPublicEvents(this ICurrentUser user) => user.Has(Permission.PublishPublicEvents);

    public static bool CanReviewEvents(this ICurrentUser user) => user.Has(Permission.ReviewEvents);

    public static bool CanManageUsers(this ICurrentUser user) => user.Has(Permission.ManageUsers);
}

/// <summary>A verified caller: the Firebase UID pulled from a validated ID token,
/// plus the profile claims the token carries. The mapping layer turns this into a
/// <c>users</c> row (creating it on first sight) and a stable internal user id.</summary>
public sealed record VerifiedIdentity(
    string AuthUid,
    string? Email,
    string? DisplayName,
    string? PhotoUrl);

/// <summary>
/// The seam between "a verified external identity" and "a row in <c>users</c>".
///
/// Exists so identity verification stays in Infrastructure (it talks to Google) while
/// the identity resolution <see cref="ICurrentUser"/> needs is expressed as an
/// Application abstraction. Firebase issues a fresh uid once per account; this maps
/// that to the internal Guid the whole domain keys on, creating the row on the first
/// authenticated request so no separate sign-up endpoint is required.
/// </summary>
public interface IUserStore
{
    /// <summary>Return the internal id for this Firebase identity, creating the
    /// <c>users</c> row if it does not exist yet. Best-effort on name/photo (the
    /// token is the source of truth for the uid, not for the display name).</summary>
    Task<Guid> ResolveUserIdAsync(VerifiedIdentity identity, CancellationToken ct = default);

    /// <summary>The internal id <em>and</em> role for this Firebase identity, in one
    /// round-trip. The identity the request acts as needs both, and asking for them
    /// through two members would read the same row twice per request; a caller that
    /// only needs the id keeps using <see cref="ResolveUserIdAsync"/>.</summary>
    Task<(Guid UserId, UserRole Role)> ResolveUserAsync(VerifiedIdentity identity, CancellationToken ct = default);

    /// <summary>The role stored on an existing user's row, or
    /// <see cref="UserRole.Member"/> when no such row exists. Used by the demo
    /// identity, which has no token to carry a role and must not invent one.</summary>
    Task<UserRole> FindRoleByIdAsync(Guid userId, CancellationToken ct = default);
}

/// <summary>
/// The seam between "an email address appeared in the ingestion pipeline" and "a
/// row in <c>users</c>". The mirror of <see cref="IUserStore"/> for identities
/// that arrive through the mailbox rather than through a bearer token: polled
/// invitations must be filed under the mailbox owner, and if that person already
/// signed in through Firebase the polled drafts land beside their own — same
/// email, same row.
///
/// Both methods upsert (an unknown address creates the row keyed on it), because
/// ingestion runs before that person has ever visited the site, and a draft is
/// never ownerless.
/// </summary>
public interface IMailboxOwnerResolver
{
    /// <summary>The configured owner for the polled mailbox address
    /// (<c>Ingestion:MailboxOwner</c>), or null when no owner is configured.</summary>
    Task<Guid?> ResolveMailboxOwnerAsync(string? mailboxAddress, CancellationToken ct = default);

    /// <summary>The user for an arbitrary address — today, the mailbox owner's own
    /// mailbox address as it appears in a message's To/Cc. Null for a blank
    /// address; upserts for anything that looks like one.</summary>
    Task<Guid?> ResolveUserByAddressAsync(
        string? address, string? displayName, CancellationToken ct = default);
}

