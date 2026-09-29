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
    public double? Latitude { get; init; }
    public double? Longitude { get; init; }
    public int? MaxParticipants { get; init; }
    public decimal? Cost { get; init; }
    public string? Description { get; init; }
}
