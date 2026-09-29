namespace SportMeet.Application.Ingestion;

/// <summary>What happened to one message. A result type rather than exceptions or
/// a bool because "no event", "duplicate" and "error" all need to be distinguishable
/// by the caller: the first two are normal and continue the cycle, the last is
/// logged and *also* continues the cycle, and a poller that conflates them either
/// stops on bad mail or silently swallows real failures.</summary>
public readonly record struct IngestResult(string Kind, Guid? DraftId = null, string? Detail = null)
{
    public static IngestResult Created(Guid draftId, bool flaggedDuplicate)
        => new("created", draftId, flaggedDuplicate ? "flagged as possible duplicate" : null);

    public static IngestResult NoEvent { get; } = new("no_event");
    public static IngestResult Duplicate { get; } = new("duplicate");
    public static IngestResult Error(string detail) => new("error", null, detail);

    public bool CreatedDraft => Kind == "created";
}

/// <summary>
/// One message through the pipeline: dedupe, extract, persist, propose.
///
/// Takes the message already fetched rather than fetching it itself, which keeps IMAP
/// out of this layer and makes the whole extraction/dedupe/draft path testable - and
/// drivable from an HTTP endpoint with pasted text, which is how the plan validates
/// prompt quality before any email plumbing exists (§7 step 2).
/// </summary>
public interface IEmailIngestionService
{
    /// <param name="ownerUserId">The user the resulting draft belongs to: the
    /// authenticated user for manual/pasted extraction, or the mailbox owner for
    /// ingested mail. Required — a draft is never ownerless, which is what keeps one
    /// user's queue private from another's.</param>
    /// <param name="icsData">Raw iCalendar payload when the message carried a
    /// calendar invite. Structured calendar data beats prose: the pipeline parses
    /// SUMMARY/LOCATION/DTSTART/DTEND/ORGANIZER from it and only falls back to
    /// text/LLM extraction when the parse yields nothing usable. Null for plain
    /// text (the manual paste path).</param>
    Task<IngestResult> IngestAsync(
        Guid ownerUserId,
        string mailbox,
        string messageUid,
        string messageId,
        string subject,
        string fromAddr,
        DateTimeOffset? sentAt,
        string bodyText,
        string? icsData = null,
        CancellationToken ct = default);
}

