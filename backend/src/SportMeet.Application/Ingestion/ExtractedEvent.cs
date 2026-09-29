namespace SportMeet.Application.Ingestion;

using SportMeet.Domain.Enums;

/// <summary>
/// One event proposal from a single email body.
///
/// Deliberately a *separate* type from <c>CreateEventDto</c> even though the fields
/// overlap, for two reasons. First, everything here is optional: a draft is
/// expected to be incomplete, and that is a valid result rather than a validation
/// failure — the validator only runs at approval. Second, it carries fields
/// CreateEventDto must never accept
/// (<see cref="Confidence"/>, <see cref="MissingFields"/>,
/// <see cref="Reasoning"/>) because those are model chatter, not event data.
///
/// The important absence: <c>host_id</c>, <c>sport_id</c> and status are not
/// representable here at all, so a prompt-injected email cannot ask for them and a
/// buggy extractor cannot invent them. Host comes from ICurrentUser at approval and
/// tags are normalized by TagNormalizer, both server-side.
/// </summary>
public sealed class ExtractedEvent
{
    public string? Title { get; init; }

    /// <summary>Free-text activity words as found, '#' and casing included.
    /// Normalized/deduped/capped by TagNormalizer at approval, never here — the
    /// extractor reports what the email said, the server decides what is stored.</summary>
    public List<string>? Tags { get; init; }

    public DateTimeOffset? StartAt { get; init; }
    public DateTimeOffset? EndAt { get; init; }

    /// <summary>IANA zone id, or null when the email did not say and the
    /// configured default applies. Never a guess.</summary>
    public string? Timezone { get; init; }

    public string? VenueName { get; init; }
    public string? Address { get; init; }

    /// <summary>Only ever populated when the email genuinely contained
    /// coordinates. Absent coordinates are reported via
    /// <see cref="MissingFields"/> so the reviewer supplies a real venue —
    /// see the plan's note that GeoSearchController is still a stub.</summary>
    public double? Latitude { get; init; }
    public double? Longitude { get; init; }

    public int? MaxParticipants { get; init; }
    public decimal? Cost { get; init; }
    public string? Description { get; init; }

    /// <summary>Null when the email never mentioned ability. Deliberately not inferred
    /// from a venue, a price or a tag: "social" is not evidence of "beginner", and a
    /// wrong level actively deters the wrong players, which is the one way this field
    /// does harm. The review form has no selector for it either, so a value here must
    /// come from the email or be cleared by the reviewer.</summary>
    public SkillLevel? SkillLevel { get; init; }

    // --- extraction metadata: kept with the proposal, dropped before CreateEventDto ---

    /// <summary>Model's self-reported 0-1. Advisory; nothing auto-approves on it.</summary>
    public decimal Confidence { get; init; }

    /// <summary>CreateEventDto field names the extractor could not determine.
    /// The review queue renders these as chips, so they gate the reviewer's effort.</summary>
    public IReadOnlyList<string> MissingFields { get; init; } = [];

    /// <summary>Short free-text justification, shown to the reviewer next to the
    /// original email body. Never fed back into anything.</summary>
    public string? Reasoning { get; init; }

    /// <summary>Which prompt/extractor produced this, for after-the-fact
    /// correlation. Written to ingested_emails.prompt_version/model.</summary>
    public string PromptVersion { get; init; } = "";
    public string Model { get; init; } = "";

    /// <summary>The model's response verbatim, for prompt debugging. Null for extractors
    /// whose output is already fully described by the fields above — i.e. the heuristic.
    /// Written to <c>ingested_emails.raw_extraction</c>; see that property for why the raw
    /// text is kept rather than only the projection.</summary>
    public string? RawExtraction { get; init; }

    /// <summary>Copy with the raw text attached. Needed because this type is a class with
    /// <c>init</c> setters, so <c>with</c>-style mutation is unavailable — and it stays a
    /// class on purpose: a record's value equality would let two proposals from different
    /// emails compare equal, which is meaningless here and would be tempting to rely on.</summary>
    public ExtractedEvent WithRawExtraction(string? raw) => new()
    {
        Title = Title,
        Tags = Tags,
        StartAt = StartAt,
        EndAt = EndAt,
        Timezone = Timezone,
        VenueName = VenueName,
        Address = Address,
        Latitude = Latitude,
        Longitude = Longitude,
        MaxParticipants = MaxParticipants,
        Cost = Cost,
        Description = Description,
        SkillLevel = SkillLevel,
        Confidence = Confidence,
        MissingFields = MissingFields,
        Reasoning = Reasoning,
        PromptVersion = PromptVersion,
        Model = Model,
        RawExtraction = raw,
    };
}
