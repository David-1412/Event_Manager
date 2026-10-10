namespace SportMeet.Domain.Enums;

/// <summary>
/// Who a caller is in this app. Three tiers, each a superset of the one before:
///
///  - <see cref="Member"/> is everyone: create events, join events, and see only
///    their own unpublished (pending / rejected) events. A public event they
///    create waits for review.
///  - <see cref="Creator"/> is a trusted organiser: their own public events
///    publish straight away. No authority over anyone else's events or accounts.
///  - <see cref="Admin"/> additionally works the review queue and manages roles.
///
/// Code never asks "is this an Admin?" — it asks for the <see cref="Permission"/>
/// the action actually needs, through <see cref="UserRolePermissions"/>, so adding
/// a role means editing one table rather than auditing every call site.
///
/// Persisted as its CLR name on <c>users.role</c>, matching every other status
/// column in this schema; the ordinal is never stored, so the order here is free
/// to read lowest privilege first.
/// </summary>
public enum UserRole
{
    Member,
    Creator,
    Admin,
}

/// <summary>The capabilities a role can grant. Authorization checks name one of
/// these, never a role.</summary>
public enum Permission
{
    /// <summary>Public events this user creates publish immediately instead of
    /// waiting in PendingReview.</summary>
    PublishPublicEvents,

    /// <summary>Read the pending-review queue, read other people's unpublished
    /// events, and approve or reject them.</summary>
    ReviewEvents,

    /// <summary>List every account and change anyone's role.</summary>
    ManageUsers,
}

/// <summary>The role-to-permission table, in one place so the comparison does not
/// get re-typed (and re-inverted) at each call site.</summary>
public static class UserRolePermissions
{
    public static bool Has(this UserRole role, Permission permission) => permission switch
    {
        Permission.PublishPublicEvents => role is UserRole.Creator or UserRole.Admin,
        Permission.ReviewEvents => role is UserRole.Admin,
        Permission.ManageUsers => role is UserRole.Admin,
        _ => false,
    };

    public static bool CanPublishPublicEvents(this UserRole role) => role.Has(Permission.PublishPublicEvents);

    public static bool CanReviewEvents(this UserRole role) => role.Has(Permission.ReviewEvents);

    public static bool CanManageUsers(this UserRole role) => role.Has(Permission.ManageUsers);

    /// <summary>Position in the hierarchy, lowest first. Used only to label a role
    /// change as a promotion or a demotion in the audit trail.</summary>
    public static int Rank(this UserRole role) => role switch
    {
        UserRole.Member => 0,
        UserRole.Creator => 1,
        UserRole.Admin => 2,
        _ => 0,
    };
}
