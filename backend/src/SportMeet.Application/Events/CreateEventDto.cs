using SportMeet.Domain.Enums;

namespace SportMeet.Application.Events;

/// <summary>
/// Byte-for-byte the body frontend/src/features/create/create-event-schema.ts
/// builds in toCreateEventPayload(): same names, same optionality. The frontend
/// therefore needs no change to POST here. host_id is deliberately absent - the
/// server assigns it (see ICurrentUser), because a client-supplied host would be
/// trivially spoofable once auth exists.
/// </summary>
public sealed class CreateEventDto
{
    public string? Title { get; init; }

    /// <summary>Free-text tags ("#tennis", "club"). Normalized, deduped and capped
    /// by TagNormalizer on the way in; rows are created implicitly on first use,
    /// so no curation step and no redeploy is needed to introduce a tag. Optional:
    /// an event with no tags is a valid event.</summary>
    public List<string>? Tags { get; init; }

    /// <summary>Optional. Null means the event has no skill level and the client
    /// hides the badge; the create form no longer offers a selector.</summary>
    public SkillLevel? SkillLevel { get; init; }

    public DateTimeOffset? StartAt { get; init; }
    public DateTimeOffset? EndAt { get; init; }
    public string? Timezone { get; init; }
    public string? VenueName { get; init; }
    public string? Address { get; init; }
    /// <summary>Optional. The URL an earlier POST to /api/events/thumbnail handed
    /// back; the create form uploads the picked image and forwards the URL here.
    /// Null means no image, and the browse/preview/detail renderers hide the
    /// thumbnail rather than showing a placeholder.</summary>
    public string? ThumbnailUrl { get; init; }
    public double? Latitude { get; init; }
    public double? Longitude { get; init; }
    public int? MaxParticipants { get; init; }
    public decimal? Cost { get; init; }
    public string? Description { get; init; }

    /// <summary>Discovery only: Public events appear in the browse feed, Private
    /// ones are reachable solely through the link the host shares. Null is
    /// Public — a client that predates the field must not accidentally create a
    /// hidden event, and the draft-approval path forwards whatever it was given.</summary>
    public EventVisibility? Visibility { get; init; }

    /// <summary>Opt-in moderation. Null is false: a client that predates this field
    /// must not find its event silently gating joins, so the absent case is the
    /// ordinary immediate-join behaviour rather than a judgment call.</summary>
    public bool? RequiresJoinApproval { get; init; }

    /// <summary>Optional. Present when the host turned on "Repeat event": the server
    /// expands it into one event per session (see RecurrenceRule), and the response
    /// is the first session. Null is a one-off event, so older clients are unaffected.</summary>
    public CreateRecurrenceDto? Recurrence { get; init; }
}

/// <summary>How a new event repeats. Exactly one of <see cref="EndDate"/> and
/// <see cref="Count"/>.</summary>
public sealed class CreateRecurrenceDto
{
    /// <summary>"DAILY", "WEEKLY", "BIWEEKLY" or "MONTHLY" (case-insensitive). A
    /// string rather than the enum because this API registers its enum converters
    /// one by one and the spec spells these upper-case.</summary>
    public string? Frequency { get; init; }

    /// <summary>Weekday codes for weekly and fortnightly series ("MON", "WED"). The
    /// first session's weekday is always included, so this may be empty.</summary>
    public List<string>? Days { get; init; }

    /// <summary>Last day of the series (inclusive), in the event's timezone.</summary>
    public DateOnly? EndDate { get; init; }

    /// <summary>Total number of sessions, the first included.</summary>
    public int? Count { get; init; }
}
