using SportMeet.Domain.Enums;

namespace SportMeet.Domain.Entities;

/// <summary>
/// One attempt to turn user-provided content into an event, and what became of it.
///
/// This row is the extraction-quality record. It holds what the extractor proposed
/// (<see cref="ExtractedData"/>), what the user finally published
/// (<see cref="FinalData"/>), and per field whether they kept, changed, filled or
/// cleared it (<see cref="FieldOutcomes"/>) - which is the measurement of real-world
/// accuracy, as opposed to a golden-set score.
///
/// Nothing here depends on the input being text: the extractor's answer is the same
/// <c>CreateEventDto</c> shape whatever produced it, and <see cref="SourceText"/> is just
/// the human-readable form of the input where one exists.
/// </summary>
public class EventImport
{
    public Guid Id { get; set; }

    /// <summary>The importing user. Every read is scoped to it.</summary>
    public Guid UserId { get; set; }

    public ImportInputKind InputKind { get; set; }
    public ImportStatus Status { get; set; }

    /// <summary>What the extractor was given, capped. Personal correspondence can end up
    /// here, so it is purged after the retention window; the structured columns below
    /// are what outlive it.</summary>
    public string? SourceText { get; set; }

    /// <summary>CreateEventDto JSON as shown to the user, coordinates included. Null for
    /// <see cref="ImportStatus.NoEvent"/>.</summary>
    public string? ExtractedData { get; set; }

    /// <summary>Check-this flags raised for the user, as JSON.</summary>
    public string? Flags { get; set; }

    /// <summary>Geocoder verdict (status, location type, needs-confirm), as JSON.</summary>
    public string? Geocode { get; set; }

    public decimal Confidence { get; set; }
    public List<string> MissingFields { get; set; } = [];
    public string Model { get; set; } = string.Empty;
    public string PromptVersion { get; set; } = string.Empty;
    public int LatencyMs { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    // --- filled in when (if) the user publishes ---------------------------------

    public Guid? PublishedEventId { get; set; }
    public DateTimeOffset? PublishedAt { get; set; }

    /// <summary>The published event's values in the same shape as
    /// <see cref="ExtractedData"/>, read back from the stored event rather than taken
    /// from the client, so the figure cannot be skewed by a caller.</summary>
    public string? FinalData { get; set; }

    /// <summary>JSON object: field name to kept / changed / filled / cleared.</summary>
    public string? FieldOutcomes { get; set; }
}

/// <summary>
/// How long it took to publish an event, and which route the user took. One row per
/// published event, imported or not: the success metric is the median of
/// <see cref="DurationMs"/>, and it only means something against the manual baseline.
/// </summary>
public class EventPublishMetric
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }

    /// <summary>The event this measures. Unique: a retried report cannot double-count.</summary>
    public Guid EventId { get; set; }

    /// <summary>"manual", "import" or "draft".</summary>
    public string Path { get; set; } = "manual";

    /// <summary>Milliseconds from opening Create Event to the publish succeeding.</summary>
    public int DurationMs { get; set; }

    public Guid? ImportId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
