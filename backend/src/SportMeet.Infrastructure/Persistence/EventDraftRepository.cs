using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using SportMeet.Application.Ingestion;
using SportMeet.Domain.Entities;
using SportMeet.Domain.Enums;
using SportMeet.Infrastructure.Persistence.Config;

namespace SportMeet.Infrastructure.Persistence;

/// <summary>
/// Drafts are always read joined to their source message, so the join lives here
/// rather than in the service — which never sees an IQueryable.
/// </summary>
public sealed class EventDraftRepository(AppDbContext db) : IEventDraftRepository
{
    /// <summary>Title comparison for near-match detection. Ordinal would make
    /// "Run at the track" and "run at the track" two different events, which is
    /// exactly the duplicate a reviewer is trying to spot, so fold case.</summary>
    private static readonly StringComparison TitleComparison = StringComparison.OrdinalIgnoreCase;

    /// <summary>The status-filtered join, projecting into an anonymous type — the one
    /// shape EF can construct inside a Join. Anonymous types cannot appear in a
    /// signature and tuples cannot appear in an expression tree (CS8143), so this is
    /// the whole query rather than a composable fragment, and each caller converts to
    /// the public named-tuple shape after materialization.</summary>
    public async Task<List<(EventDraft Draft, IngestedEmail Email)>> QueryAsync(
        Guid userId, DraftStatus? status, int limit, CancellationToken ct = default)
    {
        // Created descending: the queue is worked newest-first, and an id
        // tiebreak keeps paging stable when one poll cycle shares a timestamp.
        // Scoped to the owner (privacy), and Deleted is always excluded — a soft
        // delete is invisible to every read path, not just the pending queue.
        var rows = await (from d in db.EventDrafts.AsNoTracking()
                          join e in db.IngestedEmails.AsNoTracking() on d.IngestedEmailId equals e.Id
                          where d.UserId == userId
                          where d.Status != DraftStatus.Deleted
                          where status == null || d.Status == status
                          orderby d.CreatedAt descending, d.Id descending
                          select new { d, e })
            .Take(limit)
            .ToListAsync(ct);

        return [.. rows.Select(x => (x.d, x.e))];
    }

    public async Task<(EventDraft Draft, IngestedEmail Email)?> FindAsync(
        Guid id, Guid userId, CancellationToken ct = default)
    {
        var row = await (from d in db.EventDrafts.AsNoTracking()
                         join e in db.IngestedEmails.AsNoTracking() on d.IngestedEmailId equals e.Id
                         where d.Id == id && d.UserId == userId && d.Status != DraftStatus.Deleted
                         select new { d, e })
            .FirstOrDefaultAsync(ct);

        return row is null ? null : (row.d, row.e);
    }

    public async Task AddAsync(EventDraft draft, CancellationToken ct = default)
    {
        db.EventDrafts.Add(draft);
        await db.SaveChangesAsync(ct);
    }

    public async Task SaveChangesAsync(CancellationToken ct = default) => await db.SaveChangesAsync(ct);

    public async Task<bool> EmailSeenAsync(string mailbox, string messageUid, CancellationToken ct = default)
        => await db.IngestedEmails
            .AnyAsync(x => x.Mailbox == mailbox && x.MessageUid == messageUid, ct);

    public async Task<IngestedEmail?> FindByEmailHashAsync(string bodyHash, CancellationToken ct = default)
        // Oldest first on purpose: the row that got there first is the one a later
        // copy should be reported against.
        => await db.IngestedEmails
            .AsNoTracking()
            .Where(x => x.BodyHash == bodyHash)
            .OrderBy(x => x.CreatedAt)
            .FirstOrDefaultAsync(ct);

    public async Task AddEmailAsync(IngestedEmail email, CancellationToken ct = default)
    {
        db.IngestedEmails.Add(email);
        await db.SaveChangesAsync(ct);
    }

    public async Task<EventDraft?> FindWithLockAsync(Guid id, Guid userId, CancellationToken ct = default)
        // Same raw-SQL FOR UPDATE as EventRepository.FindWithLockAsync — no EF
        // equivalent, real DbParameter, and only meaningful inside
        // RunInTransactionAsync. Scoped to the owner in the same statement that takes
        // the lock, so a cross-owner approve is refused before the row is even read.
        => await db.EventDrafts
            .FromSqlRaw(
                $"SELECT * FROM {SportConfiguration.Schema}.event_drafts WHERE id = @id AND user_id = @userId FOR UPDATE",
                new NpgsqlParameter<Guid>("@id", id),
                new NpgsqlParameter<Guid>("@userId", userId))
            .FirstOrDefaultAsync(ct);


    public async Task RunInTransactionAsync(Func<CancellationToken, Task> action, CancellationToken ct = default)
    {
        // EnableRetryOnFailure rejects a manual transaction outside its own
        // execution strategy (see EventRepository's version of this).
        var strategy = db.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            await using IDbContextTransaction transaction = await db.Database.BeginTransactionAsync(ct);
            await action(ct);
            await transaction.CommitAsync(ct);
        });
    }

    public async Task<List<EventDraft>> FindNearMatchDraftsAsync(
        Guid userId, string title, DateTimeOffset startAt, Guid? excludeId, CancellationToken ct = default)
    {
        var pending = await db.EventDrafts
            .AsNoTracking()
            .Where(x => x.UserId == userId)
            .Where(x => x.Status == DraftStatus.Pending)
            .Where(x => excludeId == null || x.Id != excludeId)
            .ToListAsync(ct);

        // The payload comparison runs client-side because title/start_at live
        // inside a jsonb document, not in columns — the trade the jsonb payload
        // makes (§2 of the plan). Scoped to pending drafts, which is a small set.
        // Fuzzy title matching is deliberately absent: false positives train
        // reviewers to ignore the warning, which is worse than missing one.
        return pending
            .Where(x => SameStart(x, startAt) && TitlesMatch(TitleOf(x), title))
            .ToList();
    }

    public async Task<Guid?> FindDuplicateEventAsync(string title, DateTimeOffset startAt, CancellationToken ct = default)
        => await db.Events
            .AsNoTracking()
            .Where(x => x.Status == EventStatus.Scheduled)
            // Compared in the instant domain, so a timezone difference between the
            // draft and the event does not hide a real duplicate.
            .Where(x => x.StartAt == startAt)
            .Where(x => x.Title.ToLower() == title.ToLower())
            .Select(x => (Guid?)x.Id)
            .FirstOrDefaultAsync(ct);

    /// <summary>The payload is jsonb text, so these parse rather than read a
    /// column. Tolerant by design: a malformed payload must not throw inside an
    /// advisory duplicate check — it simply means that draft cannot match.</summary>
    private static string? TitleOf(EventDraft draft)
    {
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(draft.Payload);
            return doc.RootElement.TryGetProperty("title", out var t) ? t.GetString() : null;
        }
        catch (System.Text.Json.JsonException)
        {
            return null;
        }
    }

    private static DateTimeOffset? StartOf(EventDraft draft)
    {
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(draft.Payload);
            return doc.RootElement.TryGetProperty("startAt", out var s)
                   && s.ValueKind == System.Text.Json.JsonValueKind.String
                   && DateTimeOffset.TryParse(s.GetString(), out var parsed)
                ? parsed
                : null;
        }
        catch (System.Text.Json.JsonException)
        {
            return null;
        }
    }

    private static bool TitlesMatch(string? a, string b)
        => !string.IsNullOrWhiteSpace(a)
           && string.Equals(a.Trim(), b.Trim(), TitleComparison);

    /// <summary>One-minute tolerance: two forwards of the same invite can carry a
    /// start a minute apart through rounding, and that pair is exactly what the
    /// warning exists to catch.</summary>
    private static bool SameStart(EventDraft draft, DateTimeOffset startAt)
    {
        var other = StartOf(draft);
        return other is { } o && System.Math.Abs((o - startAt).TotalMinutes) <= 1;
    }
}
