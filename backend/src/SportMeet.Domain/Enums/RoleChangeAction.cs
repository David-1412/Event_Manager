namespace SportMeet.Domain.Enums;

/// <summary>
/// What a <see cref="SportMeet.Domain.Entities.UserRoleAudit"/> row records: one
/// administrator moving one account up or down the <see cref="UserRole"/> hierarchy.
///
/// Stored as the action taken rather than as a before/after role pair, because
/// the action answers direction without the reader having to infer it from two
/// columns. The prior role is still kept on the row for the audit trail
/// itself; this enum is just the axis the trail is read along.
///
/// Persisted as its CLR name on <c>user_role_audit.action</c>, matching every
/// other status column in this schema.
/// </summary>
public enum RoleChangeAction
{
    Promoted,
    Demoted,
}
