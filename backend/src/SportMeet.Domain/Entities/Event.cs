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

    public int MaxParticipants { get; set; }
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

    public ICollection<EventParticipant> Participants { get; set; } = new List<EventParticipant>();

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
