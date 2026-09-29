using SportMeet.Domain.Enums;

namespace SportMeet.Domain.Entities;

/// <summary>
/// An extracted proposal awaiting human review. Lives in its own table rather
/// than as an <see cref="Event"/> with a draft status so that no read path over
/// events can ever surface unreviewed model output.
///
/// <see cref="Payload"/> is a JSON document shaped exactly like
/// <c>CreateEventDto</c> rather than one column per field. That is a deliberate
/// trade: it costs us queryability over the draft's contents (we never query
/// them — we render and validate them) and buys immunity from migrations every
/// time the extraction schema changes, which the plan expects to be the most
/// frequently-edited part of this feature.
/// </summary>
public class EventDraft
{
    public Guid Id { get; set; }

    /// <summary>The user who owns this draft. Set at creation — the authenticated
    /// user for manual/pasted extraction, or the mailbox owner for ingested mail —
    /// and immutable thereafter. Every read and every mutation is scoped to it, so
    /// one user never sees or acts on another's drafts (the privacy rule for this
    /// feature; <c>IEventDraftService</c> filters on it and the repository enforces
    /// it in SQL). FK to <c>users</c>; required.</summary>
    public Guid UserId { get; set; }
    public User User { get; set; } = null!;

    public Guid IngestedEmailId { get; set; }
    public IngestedEmail IngestedEmail { get; set; } = null!;


    /// <summary>JSON object with CreateEventDto's field names. Stored as-authored,
    /// including gaps: a draft is *expected* to be incomplete, and the fields it
    /// is missing are described by <see cref="MissingFields"/> rather than being
    /// defaulted here, because a guessed venue is worse than an absent one.</summary>
    public string Payload { get; set; } = "{}";

    /// <summary>Model's self-reported 0-1. Advisory only — nothing auto-approves
    /// on it, since a confident wrong answer is the failure mode of every LLM.</summary>
    public decimal Confidence { get; set; }

    /// <summary>What the extractor could not determine. Drives the review UI's
    /// chips, so it is the field that decides how much work approval is.</summary>
    public List<string> MissingFields { get; set; } = [];

    public DraftStatus Status { get; set; } = DraftStatus.Pending;

    /// <summary>Set on approval: the event created from this draft.</summary>
    public Guid? EventId { get; set; }

    /// <summary>Set when flagged against an existing event. Near-match detection
    /// *warns* rather than blocks, so this is informational and does not by itself
    /// stop a reviewer approving.</summary>
    public Guid? DuplicateOfEventId { get; set; }

    /// <summary>The reviewer, i.e. whoever's identity was configured when the
    /// approve/reject call arrived. Note this is *not* the event's host —
    /// EventService.CreateAsync assigns HostId from ICurrentUser independently, so
    /// the two only coincide while a single demo identity is in use.</summary>
    public Guid? ReviewedBy { get; set; }

    public DateTimeOffset? ReviewedAt { get; set; }
    public string? ReviewNote { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>True while the draft is still actionable. Approval checks this so
    /// a second click — or two reviewers — cannot create a second event.</summary>
    public bool IsPending => Status == DraftStatus.Pending;
}

