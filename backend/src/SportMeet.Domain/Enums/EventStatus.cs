namespace SportMeet.Domain.Enums;

/// <summary>
/// Lifecycle of an event. The three original values stay exactly as they were:
/// <see cref="Scheduled"/> is what an event that is live in the browse feed is
/// called, and the CHECK constraint plus the partial index on (start_at) WHERE
/// status = 'scheduled' still refer to it.
///
/// The four review-workflow values split "published" from "written down":
///
///  - <see cref="Draft"/> is a saved-but-not-submitted event. Nothing writes it
///    yet; the value exists so the review vocabulary is complete and a later
///    save-for-later feature is a code change rather than a migration.
///  - <see cref="PendingReview"/> is a public event created by a regular user.
///    It is invisible to the browse feed and readable only by its creator and
///    an administrator until a decision is made.
///  - <see cref="Published"/> is the reviewed-and-approved twin of
///    <see cref="Scheduled"/>: both are visible, joinable events, which is why
///    every visibility test in the codebase asks "is it one of these two"
///    rather than comparing against a single value.
///  - <see cref="Rejected"/> stays with the creator - never publicly visible -
///    and can be edited and resubmitted.
///
/// Persisted as its CLR name, like every other status column here, so the
/// spellings in events_status_check are the ones that appear in SQL.
/// </summary>
public enum EventStatus
{
    Scheduled,
    Cancelled,
    Completed,
    Draft,
    PendingReview,
    Published,
    Rejected,
}

/// <summary>
/// Whether a status means "this event is live": visible in the browse feed and
/// joinable. Both <see cref="Scheduled"/> (every event written before the
/// review workflow, and every event an admin or a private creator makes today)
/// and <see cref="Published"/> (approved through review) qualify; Draft,
/// PendingReview and Rejected never do.
///
/// One place so a visibility rule cannot be applied to one live status and
/// forgotten on the other.
/// </summary>
public static class EventStatusExtensions
{
    public static bool IsPublished(this EventStatus status) =>
        status is EventStatus.Scheduled or EventStatus.Published;
}

