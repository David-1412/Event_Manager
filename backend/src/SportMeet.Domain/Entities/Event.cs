using SportMeet.Domain.Enums;

namespace SportMeet.Domain.Entities;

public class Event
{
    public Guid Id { get; set; }

    public Guid HostId { get; set; }
    public User Host { get; set; } = null!;

    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }

    /// <summary>
    /// Icon source only. Tags replaced the sport vocabulary as the browse filter
    /// and search dimension, so this is no longer required: an event with no
    /// sport simply renders no emoji. The sports table is kept (rather than
    /// dropped) purely so the seeded emoji survive, and the FK is kept so an
    /// event cannot point at a sport that has gone away.
    /// </summary>
    public int? SportId { get; set; }
    public Sport? Sport { get; set; }

    public string VenueName { get; set; } = string.Empty;
    public string? Address { get; set; }

    /// <summary>Optional event thumbnail. Holds the URL the upload endpoint hands
    /// back (served from <c>/uploads/...</c>), not the bytes. Null means the host
    /// uploaded no image, and every renderer (browse card, live preview, detail)
    /// falls back to the no-image layout rather than a placeholder.</summary>
    public string? ThumbnailUrl { get; set; }

    /// <summary>Google Place ID. Written only by the venue picker's real
    /// Places API path; unused in Milestone 1 because geo search is a stub.</summary>
    public string? PlaceId { get; set; }

    public double Lat { get; set; }
    public double Lng { get; set; }

    /// <summary>IANA zone, e.g. "Australia/Melbourne". The client sends its own
    /// resolved zone; stored so a Melbourne event still reads "6PM" for someone
    /// viewing from another zone.</summary>
    public string Timezone { get; set; } = "Australia/Melbourne";

    public DateTimeOffset StartAt { get; set; }
    public DateTimeOffset EndAt { get; set; }

    /// <summary>Capacity, or <c>null</c> for "no limit". The create form leaves
    /// Spots empty for an event that simply cannot fill up, which is the same
    /// shape <c>Cost</c> already uses for free: the absence of a value is the
    /// meaning, so no sentinel number is stored that every display would have to
    /// recognise. Where a limit exists it is bounded by
    /// <see cref="Persistence.Config.EventConfiguration.ParticipantsMin"/> and
    /// <c>ParticipantsMax</c>; a null skips the capacity check on join and on
    /// approval rather than being compared against a large number.</summary>
    public int? MaxParticipants { get; set; }
    /// <summary>Nullable: the create form no longer exposes a skill selector, so
    /// a user-created event has none and the client hides the badge entirely.
    /// The events_skill_level_check constraint needs no change - a CHECK that
    /// evaluates to NULL passes in Postgres, so a null skill_level is legal
    /// under "skill_level IN (...)" without the constraint being relaxed.</summary>
    public SkillLevel? SkillLevel { get; set; }

    /// <summary>null means free, which the client renders as "Free".</summary>
    public decimal? Cost { get; set; }

    public EventStatus Status { get; set; } = EventStatus.Scheduled;

    /// <summary>
    /// Discovery, not access: Public events appear in the browse feed, Private
    /// ones only ever surface through their direct link (which the host can
    /// share). Both remain readable through GET /api/events/{id} — hiding a
    /// private event there would break the link-sharing the flag exists for.
    /// </summary>
    public EventVisibility Visibility { get; set; } = EventVisibility.Public;

    /// <summary>Set when Status becomes Cancelled. Exposed to the client as
    /// EventDetail.cancelledAt. Not written in Milestone 1 (no cancel endpoint) —
    /// it exists now so the cancel milestone is a code change, not a migration.</summary>
    public DateTimeOffset? CancelledAt { get; set; }

    /// <summary>Why an administrator rejected this event, set by the review decision
    /// and cleared when the creator resubmits it (or when it is approved). Null means
    /// "no rejection on file", which is what lets the detail page show the reason to
    /// its creator without a second lookup. Read-only for everyone but the reviewer:
    /// the edit path never accepts it.</summary>
    public string? RejectionReason { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>When the host last edited the event's details, or null if never.
    /// Separate from <see cref="UpdatedAt"/>, which also moves on status changes
    /// (review decisions, cancel, reopen) that are not edits.</summary>
    public DateTimeOffset? EditedAt { get; set; }

    /// <summary>Soft delete. Exposed by the database as <c>is_deleted</c>.</summary>
    public DateTimeOffset? DeletedAt { get; set; }

    public ICollection<EventParticipant> Participants { get; set; } = new List<EventParticipant>();

    /// <summary>When true, joining is a request rather than an action: a member
    /// who taps Join gets a <see cref="EventJoinRequest"/> in
    /// <see cref="JoinRequestStatus.Pending"/> and no participant row, so the
    /// derived <c>current_participants</c> count does not move until the host
    /// decides.
    ///
    /// Defaults to false, and false is the whole backward-compatibility story:
    /// every event that predates this column and every event created without
    /// opting in keeps the immediate-join path through
    /// <see cref="Application.Events.EventService.JoinAsync"/> untouched.
    ///
    /// A pending request reserves no capacity, deliberately matching Interested.
    /// Reserving it would let unactioned requests fill an event and lock the host
    /// out of approving anyone - so capacity is enforced where the spot is
    /// actually taken, at approval.</summary>
    public bool RequiresJoinApproval { get; set; }

    /// <summary>
    /// Part of a repeating series. Every session of a series is its own event row
    /// (its own joins, capacity, reminders and Interested), linked by
    /// <see cref="SeriesId"/>; the rule below is copied onto each so any session can
    /// say how the series repeats.
    /// </summary>
    public bool IsRecurring { get; set; }

    /// <summary>The first session's id, shared by every session of the series (the
    /// first session's own SeriesId is its Id). Null for a one-off event.</summary>
    public Guid? SeriesId { get; set; }

    public RecurrenceFrequency? RecurrenceFrequency { get; set; }

    /// <summary>Weekdays for weekly/fortnightly series, comma-separated from Monday,
    /// e.g. "TUE" or "MON,WED". Null for daily and monthly.</summary>
    public string? RecurrenceDays { get; set; }

    /// <summary>The series' last day (inclusive), when it ends on a date.</summary>
    public DateTimeOffset? RecurrenceEndDate { get; set; }

    /// <summary>The number of sessions, when the series ends after a count.</summary>
    public int? RecurrenceCount { get; set; }

    /// <summary>When the "starts tomorrow" reminder batch was sent for this event,
    /// or null while it is owed.
    ///
    /// A timestamp rather than a boolean so the row keeps its own audit trail: the
    /// column answers "did this event get its reminder, and when" without a second
    /// table, and it reads as a fact about the event rather than a flag whose
    /// meaning lives in the worker that sets it.
    ///
    /// <see cref="Application.Events.EventReminderService"/> claims it with a
    /// conditional UPDATE (<c>WHERE ... IS NULL</c>) before writing any notification,
    /// which makes the write the lock: two API replicas running a cycle at the same
    /// moment cannot both send, because only one of them moves the column off null.
    ///
    /// Never reset once set - not even when the event is cancelled. A cancelled
    /// event is skipped by the reminder query, so there is nothing to undo, and a
    /// host who cancels then reopens the event keeps the reminder already sent
    /// rather than receiving it twice.</summary>
    public DateTimeOffset? TomorrowReminderSentAt { get; set; }

    /// <summary>The same marker for the "begins in 1 hour" reminder. Independent of
    /// <see cref="TomorrowReminderSentAt"/>: an event created inside the 24-hour
    /// window is simply never owed the tomorrow one, and the two reminders never
    /// gate each other.</summary>
    public DateTimeOffset? OneHourReminderSentAt { get; set; }

    /// <summary>Browse vocabulary. Normalized on the way in (TagNormalizer),
    /// capped at TagNormalizer.MaxTagsPerEvent.</summary>
    public ICollection<EventTag> EventTags { get; set; } = new List<EventTag>();

    /// <summary>
    /// The one invariant the aggregate owns. Kept as a method rather than left
    /// to validators so a caller that bypasses the DTO layer still cannot
    /// persist a reversed time range.
    /// </summary>
    public static bool HasValidTimeRange(DateTimeOffset startAt, DateTimeOffset endAt) => endAt > startAt;
}
