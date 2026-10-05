using System.Text.Json;
using System.Security.Cryptography;
using System.Text;
using SportMeet.Application.Common;
using SportMeet.Application.Events;
using SportMeet.Domain.Entities;
using SportMeet.Domain.Enums;

namespace SportMeet.Application.Ingestion;

/// <summary>
/// The review queue. Approve is the only interesting method, and its one rule is
/// that it never creates an event itself: it hands the reviewer's corrected payload
/// to <see cref="IEventService.CreateAsync"/>, so validation, tag normalization and
/// host assignment happen in exactly one place (§1 constraint 2 of the plan).
///
/// That reuse has a consequence worth stating: the event's host is the *approving*
/// identity, not the email's sender. <c>User.Email</c> is nullable and matching is
/// best-effort, so defaulting to a sender we could not verify would silently
/// attribute someone's event to the wrong person. Host-matching is a later change
/// and is the reviewer's to make, not an implicit fallback here.
/// </summary>
public sealed class EventDraftService(
    IEventDraftRepository drafts,
    IEventService events,
    ICurrentUser currentUser) : IEventDraftService
{
    private static readonly JsonSerializerOptions PayloadRead = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private static readonly JsonSerializerOptions PayloadWrite = new()
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>The create-form field names a saved payload answers, in the order
    /// the review UI lists them. Anything else the extractor flagged (latitude,
    /// longitude, the heuristic's "pastEvent" note) is derived or advisory and is
    /// dropped when the owner saves — a field the form cannot edit must not stay
    /// "missing" after the reviewer has filled everything it can.</summary>
    private static readonly string[] EditableFields =
    [
        "title", "description", "startAt", "endAt", "timezone", "venueName",
        "address", "maxParticipants", "cost", "tags", "skillLevel",
    ];

    /// <summary>The acting user, required for every draft operation. Drafts are
    /// private and owner-scoped, so an anonymous caller has nothing to list, open or
    /// act on; failing here is honest (401) rather than returning someone else's
    /// queue or an empty one that looks like "nothing to review".</summary>
    private Guid RequireUser() =>
        currentUser.UserId
        ?? throw new DomainRuleException("Sign-in is required to view or manage drafts.");

    public async Task<IReadOnlyList<EventDraftDto>> ListAsync(
        string? status = null, int limit = 50, CancellationToken ct = default)
    {
        var userId = RequireUser();

        // An unrecognised status is a client bug. Returning an empty list would
        // look like "nothing to review" on a page whose whole job is to say
        // whether there is work, which is the worse failure.
        DraftStatus? parsed = null;
        if (!string.IsNullOrWhiteSpace(status))
        {
            if (!Enum.TryParse<DraftStatus>(status, ignoreCase: true, out var s))
                throw new DomainRuleException(
                    $"Unknown draft status '{status}'. Expected one of: {string.Join(", ", Enum.GetNames<DraftStatus>())}.");
            parsed = s;
        }

        // Clamp rather than reject: limit only sizes a page, and a large value is
        // a mistake rather than something worth a 422 on an internal endpoint.
        var rows = await drafts.QueryAsync(userId, parsed, Math.Clamp(limit, 1, 200), ct);
        return rows.Select(r => Map(r.Draft, r.Email)).ToList();
    }

    public async Task<EventDraftDto> GetAsync(Guid id, CancellationToken ct = default)
    {
        var row = await drafts.FindAsync(id, RequireUser(), ct)
            ?? throw new NotFoundException("draft", id);
        return Map(row.Draft, row.Email);
    }

    public async Task<EventDraftDto> CreateManualAsync(CreateManualDraftDto dto, CancellationToken ct = default)
    {
        var userId = RequireUser();
        if (dto.Payload is null)
            throw new DomainRuleException("A draft payload is required");

        var now = DateTimeOffset.UtcNow;
        var payload = ToUtc(dto.Payload);
        var body = (dto.SourceBody ?? payload.Description ?? "Manual event draft").Trim();
        if (body.Length > 12_000) body = body[..12_000];
        var id = Guid.NewGuid();
        var emailId = Guid.NewGuid();
        var messageUid = Guid.NewGuid().ToString("N");
        var email = new IngestedEmail
        {
            Id = emailId,
            Mailbox = "manual-form",
            OwnerUserId = userId,
            MessageUid = messageUid,
            MessageId = $"<manual-{messageUid}@local>",
            SentAt = now,
            FromAddr = "manual@local",
            Subject = Truncate(dto.SourceSubject?.Trim() is { Length: > 0 } subject
                ? subject
                : payload.Title ?? "Manual event draft", 500),
            BodyText = body,
            BodyHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(body))).ToLowerInvariant(),
            ProcessedAt = now,
            ExtractionStatus = ExtractionStatus.Extracted,
            Model = "manual",
            PromptVersion = "manual-form-v1",
            CreatedAt = now,
        };
        var draft = new EventDraft
        {
            Id = id,
            UserId = userId,
            IngestedEmailId = emailId,
            Payload = JsonSerializer.Serialize(payload, PayloadWrite),
            Confidence = 1m,
            MissingFields = [.. ComputeMissingFields(payload)],
            Status = DraftStatus.Pending,
            CreatedAt = now,
        };

        await drafts.RunInTransactionAsync(async inner =>
        {
            await drafts.AddEmailAsync(email, inner);
            await drafts.AddAsync(draft, inner);
        }, ct);

        return await GetAsync(id, ct);
    }

    /// <summary>
    /// Autosave. The owner's in-progress edit of their own pending draft: the
    /// payload is stored as submitted — gaps and all — because the whole premise
    /// of a draft is that it is incomplete, and rejecting a half-finished edit
    /// with a 422 would fight the very workflow the review form exists for.
    /// Validation belongs to approve, which runs the full CreateEventDto rules.
    ///
    /// <see cref="EventDraft.MissingFields"/> is recomputed from the saved payload
    /// so the queue's "Needs: …" chips track what the owner has actually filled
    /// rather than what the extractor originally failed to read. Confidence is
    /// left alone: it describes the extraction, not the edit, and rewriting it on
    /// save would tell the next reviewer something that never happened.
    ///
    /// Only Pending drafts are editable, and only by their owner — an approved or
    /// rejected draft is a recorded decision, and a foreign id is a 404 exactly as
    /// on every other read, so ownership is never leaked.
    /// </summary>
    public async Task<EventDraftDto> UpdateAsync(
        Guid id, UpdateDraftDto dto, CancellationToken ct = default)
    {
        var userId = RequireUser();

        if (dto.Payload is null)
            throw new DomainRuleException("A draft payload is required");

        await drafts.RunInTransactionAsync(async inner =>
        {
            var locked = await drafts.FindWithLockAsync(id, userId, inner)
                ?? throw new NotFoundException("draft", id);

            if (locked.Status != DraftStatus.Pending)
                throw new DomainRuleException(
                    $"A {Text(locked.Status)} draft cannot be edited; only pending drafts are.");

            // Times normalise to UTC instants like approve does — the payload the
            // reviewer later posts to approve should be the shape that lands in the
            // database, not a mix of offsets saved at different times.
            var payload = ToUtc(dto.Payload);
            locked.Payload = JsonSerializer.Serialize(payload, PayloadWrite);
            locked.MissingFields = [.. ComputeMissingFields(payload)];

            await drafts.SaveChangesAsync(inner);
        }, ct);

        var row = await drafts.FindAsync(id, userId, ct)
            ?? throw new NotFoundException("draft", id);
        return Map(row.Draft, row.Email);
    }

    /// <summary>Which editable fields the saved payload leaves empty. Derived from
    /// the payload rather than remembered from extraction, so saving is what moves
    /// a field out of "missing".</summary>
    private static IEnumerable<string> ComputeMissingFields(CreateEventDto payload)
    {
        var missing = new List<string>();
        foreach (var field in EditableFields)
        {
            var filled = field switch
            {
                "title" => !string.IsNullOrWhiteSpace(payload.Title),
                "description" => !string.IsNullOrWhiteSpace(payload.Description),
                "startAt" => payload.StartAt is not null,
                "endAt" => payload.EndAt is not null,
                "timezone" => !string.IsNullOrWhiteSpace(payload.Timezone),
                "venueName" => !string.IsNullOrWhiteSpace(payload.VenueName),
                "address" => !string.IsNullOrWhiteSpace(payload.Address),
                "maxParticipants" => payload.MaxParticipants is not null,
                "cost" => payload.Cost is not null,
                "tags" => payload.Tags is { Count: > 0 },
                "skillLevel" => payload.SkillLevel is not null,
                _ => true,
            };
            if (!filled) missing.Add(field);
        }
        return missing;
    }


    public async Task<(EventDetailDto Event, EventDraftDto Draft)> ApproveAsync(
        Guid id, ApproveDraftDto dto, CancellationToken ct = default)
    {
        if (dto.Event is null)
            throw new DomainRuleException("An event payload is required");

        // Npgsql rejects a DateTimeOffset with a non-zero offset on the way into
        // timestamptz, and the reviewer's payload arrives as JSON where "+10:00" is a
        // perfectly valid instant. Converting is lossless - the instant is unchanged,
        // only its representation - and Event.Timezone is what carries the wall clock.
        // Done here rather than in EventService so the shared create path is not
        // touched by this feature.
        var userId = RequireUser();
        var payload = ToUtc(dto.Event);

        // Lock the draft before deciding anything, so two reviewers clicking
        // approve together cannot both pass the pending check. Without FOR UPDATE
        // this is a check-then-act race that produces two events.
        Guid? createdId = null;
        await drafts.RunInTransactionAsync(async inner =>
        {
            var locked = await drafts.FindWithLockAsync(id, userId, inner)
                ?? throw new NotFoundException("draft", id);


            if (!locked.IsPending)
                throw new DomainRuleException(
                    $"This draft was already {Text(locked.Status)} and cannot be approved again.");

            // Created *inside* the same transaction as the status flip: an event
            // whose draft still says pending, or a closed draft with no event, are
            // both states this method should be unable to produce.
            var created = await events.CreateAsync(payload, inner);

            locked.Status = DraftStatus.Approved;
            locked.EventId = created.Id;
            locked.ReviewedBy = currentUser.UserId;
            locked.ReviewedAt = DateTimeOffset.UtcNow;
            locked.ReviewNote = string.IsNullOrWhiteSpace(dto.Note) ? null : dto.Note.Trim();

            createdId = created.Id;
            await drafts.SaveChangesAsync(inner);
        }, ct);

        var row = await drafts.FindAsync(id, userId, ct) ?? throw new NotFoundException("draft", id);
        return (await events.GetAsync(createdId!.Value, ct), Map(row.Draft, row.Email));
    }

    public async Task<EventDraftDto> RejectAsync(Guid id, RejectDraftDto dto, CancellationToken ct = default)
    {
        var userId = RequireUser();
        await drafts.RunInTransactionAsync(async inner =>
        {
            var locked = await drafts.FindWithLockAsync(id, userId, inner)
                ?? throw new NotFoundException("draft", id);

            if (!locked.IsPending)
                throw new DomainRuleException($"This draft was already {Text(locked.Status)}.");

            locked.Status = DraftStatus.Rejected;
            locked.ReviewedBy = currentUser.UserId;
            locked.ReviewedAt = DateTimeOffset.UtcNow;
            locked.ReviewNote = string.IsNullOrWhiteSpace(dto.Reason) ? null : dto.Reason.Trim();

            await drafts.SaveChangesAsync(inner);
        }, ct);

        var row = await drafts.FindAsync(id, userId, ct) ?? throw new NotFoundException("draft", id);
        return Map(row.Draft, row.Email);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var userId = RequireUser();
        await drafts.RunInTransactionAsync(async inner =>
        {
            var locked = await drafts.FindWithLockAsync(id, userId, inner)
                ?? throw new NotFoundException("draft", id);

            // Soft delete is terminal, so re-deleting is a no-op the owner never
            // sees (it already left their list). Approved and Rejected stay
            // undeletable: an approved draft is the audit record of an event, and a
            // rejected one is a review decision — neither is the owner's to erase.
            if (locked.Status is DraftStatus.Deleted)
                throw new DomainRuleException("This draft has already been deleted.");
            if (locked.Status is DraftStatus.Approved or DraftStatus.Rejected)
                throw new DomainRuleException(
                    $"A {Text(locked.Status)} draft cannot be deleted; it is part of the audit trail.");

            locked.Status = DraftStatus.Deleted;
            locked.ReviewedBy = currentUser.UserId;
            locked.ReviewedAt = DateTimeOffset.UtcNow;

            await drafts.SaveChangesAsync(inner);
        }, ct);
    }


    /// <summary>Same instant, zero offset. Npgsql only accepts UTC-offset
    /// <see cref="DateTimeOffset"/> for timestamptz, so a payload from any source that
    /// serialized with an offset would otherwise fail deep inside parameter binding
    /// with an error the reviewer has no way to act on.</summary>
    private static CreateEventDto ToUtc(CreateEventDto e) => new()
    {
        Title = e.Title,
        Tags = e.Tags,
        SkillLevel = e.SkillLevel,
        StartAt = e.StartAt?.ToUniversalTime(),
        EndAt = e.EndAt?.ToUniversalTime(),
        Timezone = e.Timezone,
        VenueName = e.VenueName,
        Address = e.Address,
        Latitude = e.Latitude,
        Longitude = e.Longitude,
        MaxParticipants = e.MaxParticipants,
        Cost = e.Cost,
        Description = e.Description,
    };

    private static EventDraftDto Map(EventDraft d, IngestedEmail e) => new()
    {
        Id = d.Id,
        UserId = d.UserId,
        Status = Text(d.Status),
        Payload = ReadPayload(d.Payload),
        Confidence = d.Confidence,
        MissingFields = d.MissingFields,
        Subject = e.Subject,
        FromAddr = e.FromAddr,
        SentAt = e.SentAt,
        BodyText = e.BodyText,
        Model = e.Model,
        PromptVersion = e.PromptVersion,
        LatencyMs = e.LatencyMs,
        RawExtraction = e.RawExtraction,
        CreatedAt = d.CreatedAt,
        EventId = d.EventId,
        DuplicateOfEventId = d.DuplicateOfEventId,
        ReviewNote = d.ReviewNote,
        ReviewedAt = d.ReviewedAt,
    };

    private static string Truncate(string value, int max)
        => value.Length <= max ? value : value[..max];

    /// <summary>Rejects anything unparseable rather than substituting an empty
    /// payload: a reviewer shown a blank form could approve it and create a blank
    /// event, which is worse than a 422 they can report.</summary>
    private static CreateEventDto ReadPayload(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<CreateEventDto>(json, PayloadRead)
                ?? throw new JsonException("payload was null");
        }
        catch (JsonException ex)
        {
            throw new DomainRuleException($"This draft's stored payload is not valid ({ex.Message}).");
        }
    }

    /// <summary>camelCase, matching every other string enum on the wire
    /// (events' status/skillLevel) so the client keeps one convention.</summary>
    private static string Text(DraftStatus status)
        => status switch
        {
            DraftStatus.Pending => "pending",
            DraftStatus.Approved => "approved",
            DraftStatus.Rejected => "rejected",
            DraftStatus.Duplicate => "duplicate",
            DraftStatus.Deleted => "deleted",

            _ => status.ToString().ToLowerInvariant(),
        };
}
