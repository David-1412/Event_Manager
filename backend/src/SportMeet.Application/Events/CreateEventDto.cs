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
}
