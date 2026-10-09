using SportMeet.Domain.Enums;

namespace SportMeet.Application.Events;

/// <summary>
/// Field-for-field mirror of EventListItem in frontend/src/types/events.ts.
/// The frontend does a structural type check, so an extra property is tolerated
/// but a missing or renamed one breaks rendering; none are added here.
///
/// Casing is decided entirely by the global camelCase JSON policy: Tags -> tags,
/// SkillLevel -> skillLevel. Values are NOT cased by the API - tags arrive
/// already lowercased by TagNormalizer and SkillLevel as the PascalCase enum name
/// or null, because spec §12 states the client lower-cases skill only for CSS
/// class lookup.
///
/// Not sealed: EventDetailDto extends it, since the detail payload is exactly
/// these fields plus five.
/// </summary>
public class EventListItemDto
{
    public required Guid Id { get; init; }
    public required string Title { get; init; }

    /// <summary>Normalized lowercase tag names, replacing the old `sport` slug.
    /// Empty rather than null when the event has none, so the client never has to
    /// branch on it. Alphabetical - the view aggregates with ORDER BY t.name.</summary>
    public required IReadOnlyList<string> Tags { get; init; }

    /// <summary>Emoji from the sports seed (spec §12), or null when the event has
    /// no sport; the client then hides the glyph. No longer implies anything about
    /// what the event *is* - it is decoration the tag vocabulary replaced.</summary>
    public string? SportIcon { get; init; }

    /// <summary>The host's display name, from the same view row the card renders.
    /// Null-safe by convention rather than by type: the feed view joins users with an
    /// inner join, so a row always has one.
    ///
    /// Added for the admin review queue, which cannot do its job without it — an
    /// administrator approving a stranger's public event has to be able to see whose
    /// event it is. The browse cards simply do not render it.</summary>
    public string? HostName { get; init; }

    /// <summary>Host photo URL; null renders the initials avatar.</summary>
    public string? HostPhotoUrl { get; init; }

    /// <summary>Null when the host never chose one; the client hides the badge
    /// rather than inventing a level.</summary>
    public SkillLevel? SkillLevel { get; init; }

    public required DateTimeOffset StartAt { get; init; }
    public required DateTimeOffset EndAt { get; init; }
    public required string Timezone { get; init; }
    public required string VenueName { get; init; }
    public string? Address { get; init; }

    /// <summary>Host-uploaded thumbnail URL, or null when the event has no image.
    /// Serializes as `thumbnailUrl`; the client hides the image area on null.</summary>
    public string? ThumbnailUrl { get; init; }

    public required double Latitude { get; init; }
    public required double Longitude { get; init; }


    /// <summary>Serializes as a JSON number. "cost":"15.00" would break the
    /// client's currency formatter, so there is an integration test for this.</summary>
    public decimal? Cost { get; init; }

    public required int MaxParticipants { get; init; }

    /// <summary>Joined participant count, derived by the feed view's COUNT over
    /// event_participants (never a stored counter). Serializes as `joinedCount`.</summary>
    public required int JoinedCount { get; init; }

    /// <summary>Interested count, derived by the feed view's COUNT over
    /// event_interests. Independent of capacity — interest reserves no spot.
    /// Serializes as `interestedCount`.</summary>
    public required int InterestedCount { get; init; }

    public required string Status { get; init; }
    public required bool IsCancelled { get; init; }

    /// <summary>True when the status makes the event live - Scheduled or Published,
    /// i.e. visible in the browse feed and joinable. The review workflow's Pending
    /// Review / Rejected / Draft are false, which is what lets the client render a
    /// "Pending review" badge (and hide Join) from one flag instead of matching on
    /// the status string. Serializes as `isPublished`.</summary>
    public required bool IsPublished { get; init; }

    /// <summary>"Public" or "Private". Private means the event is absent from
    /// <c>GET /api/events</c> and reachable only by its direct link, so the detail
    /// page renders a copyable URL whenever this is Private.</summary>
    public required string Visibility { get; init; }

    /// <summary>null when no origin coordinates were supplied or the sort is not
    /// distance-based; the client then hides the distance row.</summary>
    public double? DistanceKm { get; init; }
}
