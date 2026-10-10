namespace SportMeet.Domain.Enums;

/// <summary>
/// What a caller may do beyond being a member.
///
///  - <see cref="Member"/> is everyone: create events, join events, and see
///    only their own unpublished (pending / rejected) events.
///  - <see cref="Moderator"/> may publish public events without review.
///  - <see cref="Admin"/> additionally manages users and works the
///    pending-review queue.
///
/// Persisted as its CLR name on <c>users.role</c>, matching every other status
/// column in this schema.
/// </summary>
public enum UserRole
{
    Member,
    Moderator,
    Admin,
}

/// <summary>The one rule the role answers today, so the comparison does not get
/// re-typed (and re-inverted) at each call site.</summary>
public static class UserRoleExtensions
{
    public static bool IsAdmin(this UserRole role) => role == UserRole.Admin;

    public static bool CanPublishPublicEvents(this UserRole role) =>
        role is UserRole.Moderator or UserRole.Admin;
}
