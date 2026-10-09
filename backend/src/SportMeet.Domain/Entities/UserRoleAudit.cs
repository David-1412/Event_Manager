using SportMeet.Domain.Enums;

namespace SportMeet.Domain.Entities;

/// <summary>
/// One line of the privilege audit trail: an administrator changed someone's
/// <see cref="UserRole"/>.
///
/// This table exists because a role change is the one write in this system that
/// changes what another person is allowed to do, and the answer to "how did this
/// account become an Admin?" has to survive the people involved forgetting. The
/// <c>users</c> row can only ever show the current role, so without a trail here
/// a promotion is unexplainable after the fact — including a promotion the
/// database itself made by editing a row by hand, which is what an <see
/// cref="ActorUserId"/> of <c>null</c> is left to describe.
///
/// Append-only by construction: nothing in the application updates or deletes
/// these rows, and the actor is kept even when the actor's own account is later
/// demoted or deleted (hence <c>Restrict</c> on the FK rather than cascade — an
/// audit line must not vanish because someone dropped a user).
/// </summary>
public class UserRoleAudit
{
    public Guid Id { get; set; }

    /// <summary>The account whose role changed.</summary>
    public Guid TargetUserId { get; set; }
    public User TargetUser { get; set; } = null!;

    /// <summary>The administrator who made the change. Null only when no
    /// verified identity was attached to the request — which the endpoint's
    /// authorization filter should make unreachable, so a null here is itself
    /// worth noticing.</summary>
    public Guid? ActorUserId { get; set; }
    public User? ActorUser { get; set; }

    /// <summary>Role before the change, kept as text so the trail stays
    /// readable if the enum is ever renamed.</summary>
    public UserRole FromRole { get; set; }

    /// <summary>Role after the change.</summary>
    public UserRole ToRole { get; set; }

    public RoleChangeAction Action { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
