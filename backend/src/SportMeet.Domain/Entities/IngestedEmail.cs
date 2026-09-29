using SportMeet.Domain.Enums;

namespace SportMeet.Domain.Entities;

/// <summary>
/// Audit trail: one row per message the poller has seen, whether or not it
/// produced anything. The whole point of writing a row for *rejected* mail is that
/// "why was my invite ignored?" stays answerable instead of becoming a support
/// mystery, so <see cref="ExtractionStatus"/> is never left null on a processed
/// message.
///
/// Two independent dedupe keys because each survives what breaks the other:
/// <see cref="MessageUid"/> is unique per mailbox but is reassigned wholesale when
/// the mailbox is rebuilt (IMAP UIDVALIDITY changes), while
/// <see cref="MessageId"/> is the RFC 822 header and survives that, and
/// <see cref="BodyHash"/> still matches when the same invite is forwarded from a
/// different address with a different Message-Id.
/// </summary>
public class IngestedEmail
{
    public Guid Id { get; set; }

    /// <summary>Name of the configured mailbox this came from, not an address —
    /// addresses live in configuration and would be duplicated into every row.</summary>
    public string Mailbox { get; set; } = string.Empty;

    /// <summary>The user this message is filed under — the mailbox owner for polled
    /// mail, the authenticated user for pasted text. Nullable because a message can
    /// be recorded before (or without) a draft: a no-event or error row is still
    /// written for the audit trail, and the mailbox-owner mapping may not resolve to
    /// a user yet. When a draft is created it takes its owner from here.</summary>
    public Guid? OwnerUserId { get; set; }

    /// <summary>IMAP UID. Unique together with <see cref="Mailbox"/>.</summary>
    public string MessageUid { get; set; } = string.Empty;

    /// <summary>RFC 822 Message-Id, angle brackets preserved as received.</summary>
    public string MessageId { get; set; } = string.Empty;

    public DateTimeOffset? SentAt { get; set; }
    public string FromAddr { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;

    /// <summary>The stripped plain text actually handed to the extractor — i.e.
    /// exactly what influenced the output, so a bad draft is reproducible.
    /// <see cref="IngestionOptions.MaxBodyChars"/> caps how much is stored.</summary>
    public string BodyText { get; set; } = string.Empty;

    /// <summary>sha256 of <see cref="BodyText"/> in hex, lowercase.</summary>
    public string BodyHash { get; set; } = string.Empty;

    /// <summary>null while queued; set once the cycle that read it finished.</summary>
    public DateTimeOffset? ProcessedAt { get; set; }

    public ExtractionStatus ExtractionStatus { get; set; } = ExtractionStatus.Pending;

    /// <summary>Populated only when <see cref="ExtractionStatus"/> is Error, and
    /// only the message — a stack trace belongs in the log, not the row.</summary>
    public string? ExtractError { get; set; }

    /// <summary>The extractor's response exactly as received, before parsing.
    ///
    /// Only LLM-backed extraction writes it; a heuristic run leaves it null because its
    /// output is fully described by the payload and this row's columns, so filling it
    /// would duplicate them per message.
    ///
    /// The reason it exists is the failure modes that are invisible afterwards. A model
    /// that returns <c>"startAt": null</c> for every Monday email and a model that returns
    /// an unparsable timestamp produce the same missing field in the payload — only the
    /// raw text separates a prompt problem from a parsing bug, and that distinction is the
    /// whole of prompt iteration. Retention follows <c>body_text</c>: the raw text is the
    /// same class of personal content.
    ///
    /// Treated as untrusted on read. The review UI must render it escaped — it is model
    /// output echoing mailbox content that an author controls.</summary>
    public string? RawExtraction { get; set; }

    // Extraction provenance. Kept per row rather than global so a change of model
    // or prompt can be correlated with a change in acceptance rate afterwards.
    public string? Model { get; set; }
    public string? PromptVersion { get; set; }
    public int? LatencyMs { get; set; }
    public int? TokenUsage { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public ICollection<EventDraft> Drafts { get; set; } = new List<EventDraft>();
}
