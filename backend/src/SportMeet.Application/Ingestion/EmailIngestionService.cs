using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using SportMeet.Application.Events;
using SportMeet.Domain.Entities;
using SportMeet.Domain.Enums;

namespace SportMeet.Application.Ingestion;
/// <summary>
/// Everything the plan's §4 pipeline does between "text in" and "row in
/// event_drafts", with the transport (IMAP) and the provider (LLM) both behind
/// interfaces - which is what lets step 2 of the plan's sequence run from an HTTP
/// endpoint on pasted text, before any mailbox exists.
///
/// The dedupe order is the plan's, and the distinction between tiers matters: a
/// repeat of the same (mailbox, uid) or an identical body hash is a genuine duplicate
/// and produces no draft at all, while a near-match only records
/// <see cref="EventDraft.DuplicateOfEventId"/> and leaves the decision to a human.
/// Blocking on a heuristic would reject legitimate recurring events, which are this
/// product's core case.
/// </summary>
public sealed class EmailIngestionService(
    IEventExtractor extractor,
    IEventDraftRepository drafts,
    IOptions<IngestionOptions> options) : IEmailIngestionService
{
    private readonly IngestionOptions _options = options.Value;

    private static readonly JsonSerializerOptions PayloadWrite = new()
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    public async Task<IngestResult> IngestAsync(
        Guid ownerUserId,
        string mailbox, string messageUid, string messageId, string subject,
        string fromAddr, DateTimeOffset? sentAt, string bodyText,
        string? icsData = null, CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        var body = Truncate((bodyText ?? string.Empty).Trim(), _options.MaxBodyChars);

        // Tier 1: same mailbox, same IMAP UID. The unique index is the real guard;
        // this check exists so the common case reads as "already seen" rather than
        // surfacing as a caught DbUpdateException.
        if (await drafts.EmailSeenAsync(mailbox, messageUid, ct))
            return IngestResult.Duplicate;

        var hash = Sha256Hex(body);

        // Tier 2: byte-identical body from a different mailbox - the forwarded
        // invite. Recorded for the audit trail, but no second proposal.
        var prior = await drafts.FindByEmailHashAsync(hash, ct);
        if (prior is not null && prior.Mailbox != mailbox)
        {
            var dupe = NewEmail(ownerUserId, mailbox, messageUid, messageId, subject, fromAddr, sentAt, body, hash, now);
            dupe.ExtractionStatus = ExtractionStatus.NoEvent;
            dupe.ProcessedAt = now;
            dupe.ExtractError = $"identical body already ingested as {prior.Id}";
            await drafts.AddEmailAsync(dupe, ct);
            return IngestResult.Duplicate;
        }

        var row = NewEmail(ownerUserId, mailbox, messageUid, messageId, subject, fromAddr, sentAt, body, hash, now);

        // Structured calendar data first: a VEVENT states SUMMARY/LOCATION/
        // DTSTART/DTEND/ORGANIZER as fields, so reading them is exact where prose
        // extraction is a guess. Only when there is no ICS (or it holds nothing a
        // reviewer could act on) does the message fall through to the text/LLM
        // extractor. The email body is still stored and still reaches the reviewer
        // beside the draft, so nothing is lost by preferring the invite.
        ExtractedEvent? extracted = IcsEventParser.TryParse(icsData, sentAt ?? now);
        if (extracted is not null)
        {
            row.Model = extracted.Model;
            row.PromptVersion = extracted.PromptVersion;
        }
        else try
        {
            var started = DateTimeOffset.UtcNow;
            // Anchored on the message's own arrival, falling back to now for the manual
            // endpoint where sentAt is synthesised. An extractor cannot be trusted to resolve
            // "tomorrow" against its own clock: the poller reads a week of unread mail in one
            // cycle, so every message after the first would resolve relative dates to the day
            // of the poll rather than the day it was mailed.
            extracted = await extractor.ExtractAsync(
                subject, fromAddr, body, sentAt ?? DateTimeOffset.UtcNow, ct);
            row.LatencyMs = (int)(DateTimeOffset.UtcNow - started).TotalMilliseconds;
            // Read from the proposal, not the injected extractor, because the two can
            // legitimately disagree: the LLM extractor reports a heuristic fallback on the
            // result it returns. Recording the configured model would stamp "gpt-4o-mini" on
            // a row whose values the regex produced, which is the one thing provenance must
            // never do. Null-conditional because a null result is the model's valid "not an
            // invitation" answer, and even that row needs its provenance.
            row.Model = extracted?.Model;
            row.PromptVersion = extracted?.PromptVersion;
            row.RawExtraction = extracted?.RawExtraction;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // One malformed message, or one provider outage, must not stall the cycle
            // or lose the fact that the mail was seen. Recorded, then the caller moves
            // on (plan §4: wrap each message in try/catch).
            row.ExtractionStatus = ExtractionStatus.Error;
            row.ExtractError = Truncate(ex.Message, 500);
            row.ProcessedAt = DateTimeOffset.UtcNow;
            await drafts.AddEmailAsync(row, ct);
            return IngestResult.Error(row.ExtractError);
        }

        row.ProcessedAt = DateTimeOffset.UtcNow;

        if (extracted is null)
        {
            row.ExtractionStatus = ExtractionStatus.NoEvent;
            await drafts.AddEmailAsync(row, ct);
            return IngestResult.NoEvent;
        }

        row.ExtractionStatus = ExtractionStatus.Extracted;
        await drafts.AddEmailAsync(row, ct);

        var duplicateEvent = await TryFindDuplicateAsync(extracted, ct);


        var draft = new EventDraft
        {
            Id = Guid.NewGuid(),
            UserId = ownerUserId,
            IngestedEmailId = row.Id,

            Payload = JsonSerializer.Serialize(ToPayload(extracted), PayloadWrite),
            Confidence = extracted.Confidence,
            MissingFields = [.. extracted.MissingFields],
            Status = DraftStatus.Pending,
            DuplicateOfEventId = duplicateEvent,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        await drafts.AddAsync(draft, ct);

        return IngestResult.Created(draft.Id, duplicateEvent is not null);
    }

    /// <summary>Only a *published* match is recorded as duplicate_of. A near-match
    /// against another pending draft is left to the queue's ordering: two drafts for
    /// the same social sit adjacent in a newest-first list, which a reviewer can see,
    /// whereas an id in a hidden column they never open cannot.</summary>
    private async Task<Guid?> TryFindDuplicateAsync(ExtractedEvent e, CancellationToken ct)
    {
        if (e.Title is not { Length: > 2 } title || e.StartAt is not { } start) return null;
        return await drafts.FindDuplicateEventAsync(title, start, ct);
    }

    /// <summary>Extraction metadata is dropped here, so what the reviewer posts back
    /// is exactly a CreateEventDto and nothing model-authored reaches EventService.
    /// The timezone default is applied here rather than in the extractor so it stays
    /// one configuration value instead of a constant duplicated across layers.</summary>
    private CreateEventDto ToPayload(ExtractedEvent e) => new()
    {
        Title = e.Title,
        Tags = e.Tags,
        StartAt = e.StartAt,
        EndAt = e.EndAt,
        Timezone = string.IsNullOrWhiteSpace(e.Timezone) ? _options.DefaultTimezone : e.Timezone,
        VenueName = e.VenueName,
        Address = e.Address,
        Latitude = e.Latitude,
        Longitude = e.Longitude,
        MaxParticipants = e.MaxParticipants,
        Cost = e.Cost,
        Description = e.Description,
        SkillLevel = e.SkillLevel,
    };

    private static IngestedEmail NewEmail(
        Guid ownerUserId,
        string mailbox, string uid, string messageId, string subject,
        string fromAddr, DateTimeOffset? sentAt, string body, string hash, DateTimeOffset now) => new()
    {
        Id = Guid.NewGuid(),
        Mailbox = mailbox,
        OwnerUserId = ownerUserId,
        MessageUid = uid,
        MessageId = messageId ?? string.Empty,
        Subject = Truncate(subject ?? string.Empty, 500),
        FromAddr = Truncate(fromAddr ?? string.Empty, 500),
        SentAt = sentAt,
        BodyText = body,
        BodyHash = hash,
        CreatedAt = now,
    };

    /// <summary>Lowercase hex, matching the column's CHECK constraint. 64 chars
    /// because the constraint says so, not because sha256 does.</summary>
    private static string Sha256Hex(string value)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private static string Truncate(string value, int max)
        => value.Length <= max ? value : value[..max];
}
