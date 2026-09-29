using SportMeet.Domain.Entities;
using SportMeet.Domain.Enums;

namespace SportMeet.Application.Ingestion;

/// <summary>
/// Persistence seam for the review queue.
///
/// Join rows with their ingested email in the query rather than lazy-loading:
/// the queue always renders both, and N+1 over a 50-row queue is 51 round trips
/// for data the page cannot display without.
/// </summary>
public interface IEventDraftRepository
{
    /// <summary>Newest first. Null status = all. Never includes
    /// <see cref="DraftStatus.Deleted"/>: a deleted draft leaves every queryable set.
    /// <paramref name="userId"/> scopes the result to one owner — pass the current
    /// user; there is no "all users" query by design (privacy).</summary>
    Task<List<(EventDraft Draft, IngestedEmail Email)>> QueryAsync(
        Guid userId, DraftStatus? status, int limit, CancellationToken ct = default);

    /// <summary>Returns the draft only when it belongs to <paramref name="userId"/>.
    /// Null for an unknown id *or* one owned by someone else — the service maps both
    /// to 404, so a draft's existence is never leaked across owners.</summary>
    Task<(EventDraft Draft, IngestedEmail Email)?> FindAsync(
        Guid id, Guid userId, CancellationToken ct = default);


    Task AddAsync(EventDraft draft, CancellationToken ct = default);

    /// <summary>Flushes staged changes within the caller's transaction. Approval
    /// needs this rather than AddAsync because it mutates a locked entity in
    /// place, and the event insert that CreateAsync staged must commit in the same
    /// unit — a separate SaveChanges would have committed it on its own.</summary>
    Task SaveChangesAsync(CancellationToken ct = default);

    /// <summary>True when a message with this (mailbox, uid) is already recorded.
    /// The polling idempotency check — backed by a unique index, so a false here
    /// racing with another cycle still loses safely at insert time.</summary>
    Task<bool> EmailSeenAsync(string mailbox, string messageUid, CancellationToken ct = default);

    /// <summary>Any recorded message whose body hashes to this. Second dedupe tier:
    /// catches the same invite forwarded from a different address.</summary>
    Task<IngestedEmail?> FindByEmailHashAsync(string bodyHash, CancellationToken ct = default);

    /// <summary>Open drafts whose (title, start_at) collide with a candidate, for
    /// the warn-don't-block near-match. <paramref name="excludeId"/> skips the
    /// draft being compared against itself.</summary>
    Task<List<EventDraft>> FindNearMatchDraftsAsync(
        Guid userId, string title, DateTimeOffset startAt, Guid? excludeId, CancellationToken ct = default);


    /// <summary>Existing scheduled events at the same title+start, the stronger of
    /// the two duplicate signals — an already-published match is worth flagging.</summary>
    Task<Guid?> FindDuplicateEventAsync(string title, DateTimeOffset startAt, CancellationToken ct = default);

    Task AddEmailAsync(IngestedEmail email, CancellationToken ct = default);

    /// <summary>Locks the draft FOR UPDATE and returns it, scoped to
    /// <paramref name="userId"/> so an approve/reject/delete cannot act across
    /// owners. Null when unknown or owned by someone else.</summary>
    Task<EventDraft?> FindWithLockAsync(Guid id, Guid userId, CancellationToken ct = default);


    Task RunInTransactionAsync(Func<CancellationToken, Task> action, CancellationToken ct = default);
}
