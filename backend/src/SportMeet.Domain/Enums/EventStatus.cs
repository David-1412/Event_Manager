namespace SportMeet.Domain.Enums;

/// <summary>
/// Only <see cref="Scheduled"/> is reachable in Milestone 1. Cancel and complete
/// are later milestones, but the CHECK constraint and the partial index on
/// (start_at) WHERE status = 'scheduled' already reference all three, so the
/// vocabulary is fixed now to avoid a data migration later.
/// </summary>
public enum EventStatus
{
    Scheduled,
    Cancelled,
    Completed,
}
