namespace SportMeet.Domain.Enums;

/// <summary>
/// What a caller may do beyond being a member. Kept to two values on purpose:
/// the review workflow needs exactly one privileged role, and a permission
/// system nobody has asked for is a maintenance cost rather than a feature.
///
///  - <see cref="Member"/> is everyone: create events, join events, and see
///    only their own unpublished (pending / rejected) events.
///  - <see cref="Admin"/> additionally publishes public events without review
///    and works the pending-review queue.
///
/// Persisted as its CLR name on <c>users.role</c>, matching every other status
/// column in this schema.
/// </summary>
public enum UserRole
{
    Member,
    Admin,
}

/// <summary>The one rule the role answers today, so the comparison does not get
/// re-typed (and re-inverted) at each call site.</summary>
public static class UserRoleExtensions
{
    public static bool IsAdmin(this UserRole role) => role == UserRole.Admin;
}
