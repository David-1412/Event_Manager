using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Query;
using SportMeet.Application.Events;
using SportMeet.Domain.Enums;
using SportMeet.Infrastructure.Persistence;
using System.Linq.Expressions;

namespace SportMeet.Infrastructure.Background;

/// <summary>
/// The reminder worker's persistence, over the events table.
///
/// Two queries and nothing else, because the worker's correctness rests on both
/// being narrow:
///
///  - The read is bounded on all four axes at once - live status, start inside the
///    look-ahead horizon, the sentinel still null, and a row limit - so it can neither
///    pick up a cancelled event nor resend one already sent.
///  - The claim is an <see cref="ExecuteUpdateAsync{T}"/> with the sentinel in its
///    WHERE clause. That is what makes the duplicate guard hold across processes:
///    Postgres serialises the two UPDATEs against the row, the second one re-evaluates
///    the predicate against the committed value, matches zero rows, and the loser
///    returns false without ever writing a notification. A read-check-write would
///    have a window between the read and the write in which both workers see null.
///
/// It also bypasses the change tracker on purpose. A tracked entity could be saved by
/// a later SaveChanges from anywhere in the same scope; the sentinel must move exactly
/// once, at the moment it is decided.
/// </summary>
public sealed class EventReminderRepository(AppDbContext db) : IEventReminderRepository
{
    public async Task<List<ReminderCandidate>> ListDueAsync(
        ReminderKind kind,
        DateTimeOffset now,
        int limit,
        CancellationToken ct = default)
    {
        // The window is a look-ahead, not a catch-up: "start falls between now and the
        // moment this reminder belongs". That ordering is the whole point. Written the
        // other way (start <= the mark) the query would only ever find events whose
        // moment had already passed - it would deliver late reminders and never send one
        // in advance, which is the opposite of the feature.
        var windowClosesAt = now + LeadFor(kind);

        var rows = await db.Events.AsNoTracking()
            .Where(e => e.Status == EventStatus.Scheduled || e.Status == EventStatus.Published)
            // Not started yet, and inside the horizon for this reminder.
            .Where(e => e.StartAt > now && e.StartAt <= windowClosesAt)
            .Where(SentinelIsNull(kind))
            .OrderBy(e => e.StartAt)
            .Take(limit)
            .Select(e => new
            {
                e.Id,
                e.Title,
                e.StartAt,
                e.Timezone,
                // Membership, not requests. Approval writes a participant row in the
                // same transaction as the decision, so this table is the single source
                // of truth for "approved": a pending or rejected requester has no row
                // here and is therefore excluded by construction rather than by a
                // status comparison this query could get wrong.
                Participants = e.Participants.Select(p => p.UserId).ToList(),
            })
            .ToListAsync(ct);

        return rows
            // An event nobody has joined yet has no one to tell; skipping it here keeps
            // the service from claiming an event it will send nothing for, which would
            // silently mark it reminded.
            .Where(x => x.Participants.Count > 0)
            .Select(x => new ReminderCandidate(
                x.Id, x.Title, x.StartAt, x.Timezone, x.Participants))
            .ToList();
    }

    public async Task<bool> TryClaimAsync(
        Guid eventId,
        ReminderKind kind,
        DateTimeOffset sentAt,
        CancellationToken ct = default)
    {
        var updated = await db.Events
            .Where(e => e.Id == eventId)
            .Where(EventIsLiveAndClaimable(kind))
            .ExecuteUpdateAsync(SetSentinel(kind, sentAt), ct);

        return updated == 1;
    }

    // The horizon `now` is measured against, and the same number the service
    // documents. Derived from the kind rather than passed by the caller, so the read
    // window and the reminder's meaning cannot drift apart.
    private static TimeSpan LeadFor(ReminderKind kind) => kind == ReminderKind.Tomorrow
        ? EventReminderService.TomorrowLead
        : EventReminderService.OneHourLead;

    // The sentinel predicates and the update target are written out per kind rather
    // than reflected over the property name, so the column each reminder touches is
    // readable in one place and a new kind fails to compile here instead of writing
    // the wrong column at runtime.

    private static Expression<Func<Domain.Entities.Event, bool>>
        SentinelIsNull(ReminderKind kind) => kind switch
    {
        ReminderKind.Tomorrow => e => e.TomorrowReminderSentAt == null,
        _ => e => e.OneHourReminderSentAt == null,
    };

    private static Expression<Func<Domain.Entities.Event, bool>>
        EventIsLiveAndClaimable(ReminderKind kind) => kind switch
    {
        // Re-checked at claim time: the event may have been cancelled between the read
        // and the write, and a cancelled event must not end up marked as reminded.
        ReminderKind.Tomorrow => e =>
            (e.Status == EventStatus.Scheduled || e.Status == EventStatus.Published)
            && e.TomorrowReminderSentAt == null,
        _ => e =>
            (e.Status == EventStatus.Scheduled || e.Status == EventStatus.Published)
            && e.OneHourReminderSentAt == null,
    };

    private static Expression<Func<SetPropertyCalls<Domain.Entities.Event>, SetPropertyCalls<Domain.Entities.Event>>>
        SetSentinel(ReminderKind kind, DateTimeOffset sentAt) => kind switch
    {
        ReminderKind.Tomorrow => b => b.SetProperty(e => e.TomorrowReminderSentAt, sentAt),
        _ => b => b.SetProperty(e => e.OneHourReminderSentAt, sentAt),
    };
}
