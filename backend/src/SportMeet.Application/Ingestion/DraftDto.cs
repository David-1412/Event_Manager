using SportMeet.Application.Events;

namespace SportMeet.Application.Ingestion;

/// <summary>
/// A row of the review queue. The email's subject/from/sent arrive with the draft
/// because a reviewer cannot judge a proposal without the source next to it — that
/// side-by-side comparison is the entire UI (§9), so splitting it into a second
/// request would just make the queue render twice.
///
/// <see cref="Payload"/> is the parsed <see cref="CreateEventDto"/> the create form
/// pre-fills from, so approve round-trips the exact shape the client already builds.
/// </summary>
public sealed class EventDraftDto
{
    public required Guid Id { get; init; }

    /// <summary>The owner. Always the caller for anything this DTO is returned for
    /// (every read is owner-scoped), but present so a client can confirm ownership
    /// and key per-user state on it.</summary>
    public required Guid UserId { get; init; }

    public required string Status { get; init; }

    public required CreateEventDto Payload { get; init; }
    public required decimal Confidence { get; init; }
    public IReadOnlyList<string> MissingFields { get; init; } = [];

    // Source message.
    public required string Subject { get; init; }
    public required string FromAddr { get; init; }
    public DateTimeOffset? SentAt { get; init; }
    public required string BodyText { get; init; }

    // Extraction provenance.
    public string? Model { get; init; }
    public string? PromptVersion { get; init; }
    public int? LatencyMs { get; init; }

    /// <summary>The model's response verbatim, when there was a model. Present so the
    /// reviewer can see the text a bad field came from instead of re-deriving it from the
    /// body — the difference between "the model was wrong" and "the email was vague", which
    /// is what tells the reviewer whether to fix this draft or report a prompt bug.
    /// Untrusted: the client must render it as text, never as HTML.</summary>
    public string? RawExtraction { get; init; }

    public DateTimeOffset CreatedAt { get; init; }

    // Review outcome.
    public Guid? EventId { get; init; }
    public Guid? DuplicateOfEventId { get; init; }
    public string? ReviewNote { get; init; }
    public DateTimeOffset? ReviewedAt { get; init; }
}

/// <summary>
/// Approve body. The reviewer submits a *corrected* <see cref="CreateEventDto"/>,
/// not an id-plus-tweaks, so this endpoint and POST /api/events accept the same
/// body and the create form is the single implementation of "what a valid event
/// looks like". The draft id only decides which draft gets closed.
/// </summary>
public sealed class ApproveDraftDto
{
    public required CreateEventDto Event { get; init; }

    /// <summary>Optional reviewer comment; not the event's host.</summary>
    public string? Note { get; init; }
}

/// <summary>Reject body. One line, mirroring the spec's rejection affordance.</summary>
public sealed class RejectDraftDto
{
    public string? Reason { get; init; }
}

/// <summary>
/// Autosave body. The reviewer's in-progress <see cref="CreateEventDto"/>, gaps
/// included: update stores what it is given and recomputes the missing-field
/// hints, while the full create validation only runs at approve. Deliberately a
/// separate DTO from ApproveDraftDto so "save" cannot grow a validation rule by
/// being wired to the same type as "publish".
/// </summary>
public sealed class UpdateDraftDto
{
    public required CreateEventDto Payload { get; init; }
}
