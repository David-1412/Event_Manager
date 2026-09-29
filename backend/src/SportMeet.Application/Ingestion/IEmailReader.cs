namespace SportMeet.Application.Ingestion;

/// <summary>
/// One message as the transport delivered it — headers plus already-flattened text.
///
/// The HTML-to-text conversion happens in the reader, so what lands here is the same
/// shape the manual <c>POST /api/ingestion/extract</c> path produces from pasted text.
/// That is the point: the extractor cannot tell a polled email from a pasted one, which
/// is what lets requirement 3 ("reuse the existing pipeline") hold without touching
/// extraction code at all.
/// </summary>
/// <param name="MessageId">
/// The transport's own stable id — Graph's <c>Message.Id</c>. Used as the poller's
/// "have I seen this" key, because unlike an IMAP UID it is not reassigned when the
/// mailbox is rebuilt.
/// </param>
public sealed record FetchedEmail(
    string MessageId,
    string Subject,
    string FromAddress,
    string FromName,
    DateTimeOffset? ReceivedAt,
    string BodyText,
    bool IsHtml,
    int AttachmentCount,
    IReadOnlyList<string> AttachmentNames,
    /// <summary>The first text/calendar (ICS) attachment content, when the message
    /// carried a calendar invite. Preferred by the pipeline over prose: a real
    /// VEVENT states SUMMARY/LOCATION/DTSTART/DTEND/ORGANIZER unambiguously, so
    /// parsing it beats any extraction from the email body. Null for plain mail.</summary>
    string? IcsContent = null);

/// <summary>
/// Read-only view of one mailbox, implemented per transport (Graph today, IMAP if a
/// non-Microsoft mailbox ever matters).
///
/// Deliberately narrow: fetch, and acknowledge. There is no delete, no send and no
/// move, because a poller that can destroy mail turns a credential leak or a logic bug
/// into data loss. Mark-processed lives on <see cref="IEmailProcessor"/>, which is where
/// the dedupe decision is actually made.
/// </summary>
public interface IEmailReader
{
    /// <summary>Human-readable transport label for logs ("graph").</summary>
    string Transport { get; }

    /// <summary>Unread messages, oldest first. Implementations must not mutate the
    /// mailbox: whether a message is worth acting on is the processor's call, and a
    /// reader that marks as it fetches loses mail whenever the processor throws.</summary>
    Task<IReadOnlyList<FetchedEmail>> FetchUnreadAsync(CancellationToken ct = default);
}
