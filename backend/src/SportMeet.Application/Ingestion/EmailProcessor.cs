using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SportMeet.Application.Common;

namespace SportMeet.Application.Ingestion;

/// <summary>
/// One poll cycle against <see cref="IEmailReader"/>, feeding
/// <see cref="IEmailIngestionService"/> — the pipeline already verified end-to-end
/// through the manual endpoint. Nothing here extracts, dedupes or creates events; it
/// decides *which* messages to hand over, *whose* drafts they become, and reports
/// what came back.
///
/// Three choices worth defending:
///
/// <para><b>Per-message try/catch.</b> Graph accepts mail from anyone, so one body that
/// trips a bug in the extractor must cost one message, not the cycle. Without this a
/// single malformed invite stalls ingestion until someone restarts the API.</para>
///
/// <para><b>Sender pre-filter before extraction.</b> It saves the extractor's work, but
/// that is not the reason — the reason is that <see cref="IngestionOptions.AllowedSenders"/>
/// narrows whose text reaches an LLM at all. It is a preference filter, not a trust
/// boundary: anyone who learns the address can mail it.</para>
///
/// <para><b>No event creation, ever.</b> This class holds no reference to
/// <c>IEventService</c>. Drafts are the only thing it can produce, which is "never
/// auto-publish" enforced by the type system rather than by discipline.</para>
/// </summary>
public sealed class EmailProcessor(
    IEmailReader reader,
    IEmailIngestionService ingestion,
    IMailboxOwnerResolver owners,
    ICurrentUser currentUser,
    IOptions<IngestionOptions> options,
    IOptions<GraphOptions> graphOptions,
    ILogger<EmailProcessor> logger) : IEmailProcessor

{
    private readonly IngestionOptions _options = options.Value;
    private readonly GraphOptions _graph = graphOptions.Value;

    /// <summary>Stable owner id for a mailbox with no resolvable owner: derived
    /// from the mailbox name, so the same mailbox always files under the same
    /// identity and its drafts stay together — and as private as any other
    /// user's. A placeholder, never shared across mailboxes.</summary>
    private static Guid PlaceholderOwnerId(string mailbox)
    {
        var bytes = System.Security.Cryptography.SHA1.HashData(
            System.Text.Encoding.UTF8.GetBytes($"mailbox-owner:{mailbox}"));
        return new Guid(bytes.AsSpan(0, 16));
    }

    /// <summary>
    /// Who owns the drafts this cycle produces. In order: the configured mailbox
    /// owner (<c>Ingestion:MailboxOwner</c>, matched to a users row by address),
    /// then the mailbox owner's own address as it appears in the message
    /// (best-effort — Graph's app-only read exposes To/Cc, and an invite sent
    /// *to* the mailbox is addressed to its owner), then the configured demo
    /// identity (the development bypass), then the per-mailbox placeholder. A
    /// draft is never ownerless, because ownerlessness would mean an invisible
    /// draft.
    /// </summary>
    private async Task<Guid?> ResolveOwnerAsync(CancellationToken ct)
    {
        var owner = await owners.ResolveMailboxOwnerAsync(_graph.MailboxAddress, ct);
        if (owner is not null) return owner;

        if (currentUser.IsDemo && currentUser.UserId is { } demo) return demo;
        return currentUser.UserId;
    }

    public async Task<EmailProcessingResult> PollOnceAsync(CancellationToken ct = default)
    {
        var mailbox = MailboxName();
        var owner = await ResolveOwnerAsync(ct) ?? PlaceholderOwnerId(mailbox);

        IReadOnlyList<FetchedEmail> messages;
        try
        {
            messages = await reader.FetchUnreadAsync(ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw; // Shutdown during a Graph call — normal, the host is stopping.
        }
        catch (Exception ex)
        {
            // A fetch failure aborts the cycle and is logged loud, because this is the
            // failure mode where ingestion silently stops producing anything while the
            // API keeps reporting healthy.
            logger.LogError(ex, "Ingestion: fetch from {Transport} failed; cycle aborted", reader.Transport);
            throw;
        }

        if (messages.Count == 0) return EmailProcessingResult.Empty;

        int created = 0, duplicates = 0, noEvent = 0, failed = 0, skipped = 0;

        foreach (var message in messages)
        {
            ct.ThrowIfCancellationRequested();

            if (!SenderAllowed(message.FromAddress))
            {
                skipped++;
                logger.LogDebug(
                    "Ingestion: skipped {MessageId} from {Sender}: not in Ingestion:AllowedSenders",
                    message.MessageId, message.FromAddress);
                continue;
            }

            var outcome = await ProcessAsync(message, mailbox, owner, ct);
            switch (outcome.Kind)
            {
                case "created":
                    created++;
                    break;
                case "duplicate":
                    duplicates++;
                    break;
                case "no_event":
                    noEvent++;
                    break;
                default:
                    failed++;
                    break;
            }
        }

        return new EmailProcessingResult(
            messages.Count, created, duplicates, noEvent, failed, skipped);
    }

    /// <summary>One message, isolated. Logging happens here rather than in the loop so
    /// each branch states its own reason, and the counters above stay a plain tally.</summary>
    private async Task<IngestResult> ProcessAsync(
        FetchedEmail message, string mailbox, Guid owner, CancellationToken ct)
    {
        try
        {
            // Owner resolution ran once for the cycle (ResolveOwnerAsync): the
            // configured mailbox owner, the demo identity, or the per-mailbox
            // placeholder — never ownerless, and never auto-published: the only
            // thing this can produce is a Pending draft in that owner's queue.
            var result = await ingestion.IngestAsync(
                ownerUserId: owner,
                mailbox: mailbox,
                messageUid: message.MessageId,

                messageId: message.MessageId,
                subject: message.Subject,
                fromAddr: message.FromAddress,
                sentAt: message.ReceivedAt,
                bodyText: message.BodyText,
                icsData: message.IcsContent,
                ct: ct);

            switch (result.Kind)
            {
                case "created":
                    // Draft id plus the source message id in one line: the reviewer's next
                    // question is "which email became this draft", and body_hash is painful
                    // to reconstruct after the fact.
                    logger.LogInformation(
                        "Ingestion: draft {DraftId} from {MessageId} ({Subject}){Flag}",
                        result.DraftId, message.MessageId, Truncate(message.Subject, 80),
                        result.Detail is null ? "" : $" [{result.Detail}]");
                    break;
                case "duplicate":
                    logger.LogInformation(
                        "Ingestion: {MessageId} already processed; skipped", message.MessageId);
                    break;
                case "no_event":
                    // Information, not Debug. "Why was my invite ignored?" is the question
                    // this feature will actually be asked, and the row is written either way.
                    logger.LogInformation(
                        "Ingestion: no event found in {MessageId} ({Subject})",
                        message.MessageId, Truncate(message.Subject, 80));
                    break;
                default:
                    logger.LogWarning(
                        "Ingestion: extractor reported failure for {MessageId}: {Detail}",
                        message.MessageId, result.Detail);
                    break;
            }

            return result;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // The message is deliberately left unacknowledged. Dedupe keys on the Graph
            // message id, so next cycle hits tier-1 and reports "duplicate": a transient
            // DB blip can neither lose an invite nor double-create a draft.
            logger.LogError(ex, "Ingestion: failed to process {MessageId}", message.MessageId);
            return IngestResult.Error(ex.Message);
        }
    }

    /// <summary>Case-insensitive exact match on the address. No domain wildcards: an
    /// entry like "contoso.com" would read as a pattern to a reviewer and silently
    /// become a whole-domain allow-list, which is not what a pre-filter should mean.</summary>
    private bool SenderAllowed(string fromAddress)
    {
        if (_options.AllowedSenders.Count == 0) return true;

        var addr = (fromAddress ?? string.Empty).Trim();
        return _options.AllowedSenders.Any(s =>
            string.Equals(s.Trim(), addr, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>The configured mailbox's name, used as the tier-1 dedupe partition.
    /// Falls back to the transport name — never the address, which would embed an
    /// identifier in every row and invalidate every key if the address changed.</summary>
    private string MailboxName()
    {
        var configured = _options.Mailboxes.FirstOrDefault(m => !string.IsNullOrWhiteSpace(m.Name));
        return string.IsNullOrWhiteSpace(configured?.Name) ? reader.Transport : configured!.Name;
    }

    private static string Truncate(string? value, int max)
    {
        if (string.IsNullOrEmpty(value)) return "";
        return value.Length <= max ? value : value[..max] + "…";
    }
}
