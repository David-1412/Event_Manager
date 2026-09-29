using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using SportMeet.Api.Common;
using SportMeet.Application.Common;
using SportMeet.Application.Events;
using SportMeet.Application.Ingestion;


namespace SportMeet.Api.Controllers;

/// <summary>
/// Drives the extraction pipeline directly, with no mailbox involved: the user
/// pastes an invitation and a Pending draft appears in *their* queue.
///
/// The draft this creates is owned by the authenticated user — Firebase ID token
/// verified, uid mapped to a users row — so persistence requires a verified
/// identity and an unverified caller gets 401. A dry run persists nothing, so it
/// stays open: it is the extractor-tuning surface, returns the same payload shape
/// as a stored draft, and leaks nothing but what the caller pasted in.
/// </summary>
[ApiController]
[Route("api/ingestion")]
public class IngestionController(
    IEmailIngestionService ingestion,
    IEventDraftService drafts,
    IEventExtractor extractor,
    ICurrentUser currentUser,
    IOptions<IngestionOptions> options) : ControllerBase

{
    private readonly IngestionOptions _options = options.Value;

    /// <summary>What the pipeline is currently configured as, so a container can be
    /// checked for "is ingestion actually on" without reading its config file — the
    /// same question that costs an hour when a deploy silently does nothing.</summary>
    [HttpGet("status")]
    [ProducesResponseType(typeof(IngestionStatusDto), StatusCodes.Status200OK)]
    public ActionResult<IngestionStatusDto> Status()
        => Ok(new IngestionStatusDto(
            _options.Enabled,
            _options.PollIntervalSeconds,
            _options.Mailboxes.Count,
            extractor.Model,
            extractor.PromptVersion,
            _options.DefaultTimezone,
            _options.RetentionDays));

    /// <summary>
    /// Run one piece of text through dedupe, extraction and draft creation. With
    /// <c>dryRun</c> nothing is persisted, which is the mode to use while tuning the
    /// extractor; without it, a real pending draft appears in the queue and can be
    /// approved end to end.
    /// </summary>
    [HttpPost("extract")]
    [ProducesResponseType(typeof(ExtractNowResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<ExtractNowResponse>> Extract(
        [FromBody] ExtractNowRequest request, CancellationToken ct)
    {
        var body = request.Body ?? string.Empty;
        if (string.IsNullOrWhiteSpace(body) && string.IsNullOrWhiteSpace(request.Subject))
            return this.Problem(
                detail: "Provide a body or a subject to extract from.",
                statusCode: StatusCodes.Status422UnprocessableEntity);

        if (request.DryRun)
        {
            var proposal = await extractor.ExtractAsync(
                request.Subject ?? string.Empty, request.FromAddr, body,
                DateTimeOffset.UtcNow, ct);
            if (proposal is null)
                return Ok(new ExtractNowResponse("no_event", null, [], null, null, null));

            return Ok(new ExtractNowResponse(
                "extracted",
                proposal.Confidence,
                proposal.MissingFields,
                ToPayload(proposal),
                DraftId: null,
                Detail: proposal.Reasoning));
        }

        // A synthetic uid per call. Deliberately not derived from the body: re-posting
        // the same text should create a second draft in dev so the dedupe tier can be
        // observed *not* firing, and the real uid only exists once IMAP does.
        //
        // Persisting files the draft under whoever is acting, and "acting" means a
        // verified Firebase identity: the demo fallback is exactly the shared identity
        // this feature's privacy rule forbids, so an unverified save is a 401 rather
        // than a draft in someone else's queue. (The service's RequireUser still
        // guards the no-identity-at-all case; this one guards the worse case.)
        if (currentUser.IsDemo)
            return Unauthorized(ProblemDetailsDefaults.Unauthorized());

        var owner = currentUser.UserId
            ?? throw new DomainRuleException("Sign-in is required to save a draft.");

        var result = await ingestion.IngestAsync(
            ownerUserId: owner,
            mailbox: "manual",

            messageUid: Guid.NewGuid().ToString("N"),
            messageId: $"<manual-{Guid.NewGuid():N}@local>",
            subject: request.Subject ?? string.Empty,
            fromAddr: request.FromAddr ?? "manual@local",
            sentAt: DateTimeOffset.UtcNow,
            bodyText: body,
            ct: ct);

        // Re-read the persisted draft rather than re-running the extractor: the
        // response should be what landed in the queue, including the timezone default
        // and duplicate flag the service applied, not a second opinion from the
        // extractor that could disagree with the stored payload.
        var stored = result.DraftId is { } draftId ? await drafts.GetAsync(draftId, ct) : null;
        return Ok(new ExtractNowResponse(
            result.Kind,
            stored?.Confidence,
            stored?.MissingFields ?? [],
            stored?.Payload,
            result.DraftId,
            result.Detail));
    }

    /// <summary>Extraction metadata dropped here so a dry-run response carries the
    /// same shape as a persisted draft's payload — one body shape for the caller to
    /// post straight back to approve, whichever path produced it. Timezone is filled
    /// from the configured default exactly as the service would, so what a dry run
    /// shows is what a real run would have stored.</summary>
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
}

/// <summary>The extractor's effective configuration. Nothing secret: credentials are
/// deliberately absent so this response can be logged or screenshotted freely.</summary>
public sealed record IngestionStatusDto(
    bool Enabled,
    int PollIntervalSeconds,
    int MailboxCount,
    string Model,
    string PromptVersion,
    string DefaultTimezone,
    int RetentionDays);

/// <summary>
/// Kind is one of created / no_event / duplicate / error, mirroring
/// <see cref="IngestResult"/>. Payload is a CreateEventDto in both modes, so the
/// same client code posts it to approve whether it came from a dry run or a stored
/// draft. Detail is the extractor's reasoning (dry run) or the dedupe/error note.
/// </summary>
public sealed record ExtractNowResponse(
    string Kind,
    decimal? Confidence,
    IReadOnlyList<string> MissingFields,
    CreateEventDto? Payload,
    Guid? DraftId,
    string? Detail);
