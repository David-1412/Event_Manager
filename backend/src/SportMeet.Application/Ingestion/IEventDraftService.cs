using SportMeet.Application.Events;

namespace SportMeet.Application.Ingestion;

/// <summary>
/// The review queue: read drafts, approve one into a real event, or reject it.
///
/// Kept apart from <c>IEventService</c> rather than folded into it because the two
/// have different lifetimes and different trust levels — events are user-facing and
/// anonymous-readable, drafts are internal and contain unreviewed model output plus
/// raw personal data from someone's inbox.
/// </summary>
public interface IEventDraftService
{
    /// <summary>Queue, newest first. <paramref name="status"/> is a DraftStatus
    /// name; null means every status. An unrecognised status is a 422 rather than
    /// a silently-empty list, so a client typo is visible.</summary>
    Task<IReadOnlyList<EventDraftDto>> ListAsync(string? status = null, int limit = 50, CancellationToken ct = default);

    /// <summary><see cref="NotFoundException"/> when unknown.</summary>
    Task<EventDraftDto> GetAsync(Guid id, CancellationToken ct = default);

    Task<EventDraftDto> CreateManualAsync(CreateManualDraftDto dto, CancellationToken ct = default);

    /// <summary>Autosave the owner's in-progress edits to a *pending* draft. The
    /// payload is stored as-is (gaps allowed — that is what a draft is), and
    /// MissingFields is recomputed from it. Owner-scoped: unknown id, someone
    /// else's id, and a decided draft are 404/422 respectively.</summary>
    Task<EventDraftDto> UpdateAsync(Guid id, UpdateDraftDto dto, CancellationToken ct = default);

    /// <summary>
    /// Creates the event through <c>IEventService.CreateAsync</c> and closes the
    /// draft. Throws <see cref="SportMeet.Application.Common.NotFoundException"/>
    /// for an unknown or already-decided draft, and propagates whatever create
    /// throws (422 DomainRule, validation) unchanged — a half-approve is never
    /// possible, since the draft only moves out of pending once the event exists.
    /// </summary>
    Task<(EventDetailDto Event, EventDraftDto Draft)> ApproveAsync(
        Guid id, ApproveDraftDto dto, CancellationToken ct = default);

    Task<EventDraftDto> RejectAsync(Guid id, RejectDraftDto dto, CancellationToken ct = default);

    /// <summary>Soft-delete the owner's own draft (the "Draft -> Deleted" lifecycle
    /// edge). Owner-scoped like every other method — an unknown id and someone
    /// else's id both 404. An already-deleted or already-decided draft is a 422.
    /// Returns nothing: the contract is "gone from the owner's queue", and the row
    /// survives only for the audit trail.</summary>
    Task DeleteAsync(Guid id, CancellationToken ct = default);
}

