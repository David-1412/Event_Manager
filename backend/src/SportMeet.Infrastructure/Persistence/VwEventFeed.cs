using SportMeet.Domain.Enums;

namespace SportMeet.Infrastructure.Persistence;

/// <summary>
/// Projection of sportsmeet.v_event_feed. Keyless and read-only by construction:
/// EF will not track it for updates, and the derived CurrentParticipants is a
/// COUNT computed by the view, so there is no counter for concurrent joins to
/// race on (IMPLEMENTATION_PLAN.md §2).
///
/// Carries the Event navigation rather than flat columns because the service
/// only ever maps event fields plus the three joined ones; Selecting into this
/// shape keeps the translate-to-SQL simple and avoids loading user rows twice.
/// </summary>
public class VwEventFeed
{
    public Guid Id { get; set; }
    public Guid HostId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int? SportId { get; set; }

    public string VenueName { get; set; } = string.Empty;
    public string? Address { get; set; }
    public string? PlaceId { get; set; }
    public double Lat { get; set; }
    public double Lng { get; set; }
    public string Timezone { get; set; } = string.Empty;
    public DateTimeOffset StartAt { get; set; }
    public DateTimeOffset EndAt { get; set; }
    public int MaxParticipants { get; set; }
    public SkillLevel? SkillLevel { get; set; }

    public decimal? Cost { get; set; }
    public EventStatus Status { get; set; }
    public DateTimeOffset? CancelledAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>The COUNT over event_participants that the view derives.</summary>
    public int CurrentParticipants { get; set; }

    /// <summary>Comma-joined normalized tag names, ordered by name so the same
    /// tag set always serialises identically. Split back into a list in
    /// EventRepository; the view aggregates because a browse page must stay one
    /// round-trip, and a second join would multiply the event rows.</summary>
    public string? Tags { get; set; }

    // Joined columns, named to keep them distinct from the event's own.
    public string? SportName { get; set; }
    public string? SportSlug { get; set; }
    public string? SportIcon { get; set; }
    public string HostName { get; set; } = string.Empty;
    public string? HostPhotoUrl { get; set; }
}

