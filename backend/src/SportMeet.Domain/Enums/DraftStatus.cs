namespace SportMeet.Domain.Enums;

/// <summary>
/// A draft's position in the review queue. Deliberately *not* a new
/// <see cref="EventStatus"/> member: EventStatus' vocabulary is fixed because a
/// CHECK constraint and a partial index reference its three values, and keeping
/// drafts in their own table with their own status is also what makes it
/// structurally impossible for unreviewed AI output to appear in
/// GET /api/events by a forgotten WHERE clause.
/// </summary>
public enum DraftStatus
{
    Pending,
    Approved,
    Rejected,
    Duplicate,

    /// <summary>Owned-draft lifecycle terminal state. A soft delete: the row and its
    /// source <see cref="SportMeet.Domain.Entities.IngestedEmail"/> survive for the
    /// audit trail, but the draft leaves the owner's queue and every queryable set.
    /// Distinct from Rejected, which is a review decision on the merits; Deleted is
    /// the owner discarding their own work.</summary>
    Deleted,
}

