using Microsoft.EntityFrameworkCore;
using Npgsql;
using SportMeet.Application.Events;
using SportMeet.Domain.Entities;
using SportMeet.Infrastructure.Persistence.Config;

namespace SportMeet.Infrastructure.Persistence;

/// <summary>
/// View tracking over <c>event_views</c>.
///
/// One insert and one count, and the interesting part is what happens when the
/// insert collides. The unique index on (event_id, user_id, viewed_on) is the rule
/// "one view per person per event per day", so colliding with it is the expected
/// outcome of a second page load on the same day - not an error. Catching that one
/// exception by its Postgres code (23505) rather than by a pre-flight query is what
/// keeps the rule free of a race: two refreshes that arrive together both attempt
/// the insert, Postgres accepts exactly one, and the loser continues as though it
/// had checked first. A check-then-insert would let both see "no row yet".
///
/// The count is a COUNT over the table rather than a stored column, matching how
/// participants and interests are already derived: it cannot drift from the rows,
/// and it is recomputable from them.
/// </summary>
public sealed class EventViewRepository(AppDbContext db) : IEventViewRepository
{
    /// <summary>Postgres' unique_violation. Matched on the code and not on the
    /// message, which is localized and formatted and therefore not something to
    /// compare against; the number is the wire contract.</summary>
    private const string UniqueViolation = "23505";

    public async Task<EventViewRecord> RecordViewAsync(
        Guid eventId, Guid? userId, DateTimeOffset viewedAt, CancellationToken ct = default)
    {
        // UtcDateTime converts first, so a value carrying a non-zero offset lands on
        // the right day: 00:30 on the 14th at +11:00 is 13:30 UTC on the 13th, and
        // taking the day off the offset value would record it as the 14th and hand
        // that person a second view inside the same UTC day.
        var viewedOn = DateOnly.FromDateTime(viewedAt.UtcDateTime);

        // Add + SaveChanges rather than ExecuteInsert: an insert that collides has to
        // be caught, and the exception surfaces from SaveChanges with the entity
        // already tracked. Detaching on collision keeps this context usable for the
        // rest of the request - the detail read that follows shares it, and a tracked
        // row nobody asked for would ride along into the next SaveChanges.
        db.EventViews.Add(new EventView
        {
            Id = Guid.NewGuid(),
            EventId = eventId,
            UserId = userId,
            ViewedAt = viewedAt,
            ViewedOn = viewedOn,
        });

        var isNew = true;
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            isNew = false;
            db.ChangeTracker.Clear();
        }

        return new EventViewRecord(await CountViewsAsync(eventId, ct), isNew);
    }

    public Task<int> CountViewsByUserAsync(Guid eventId, Guid userId, CancellationToken ct = default) =>
        db.EventViews.CountAsync(v => v.EventId == eventId && v.UserId == userId, ct);

    public async Task<int> CountViewsAsync(Guid eventId, CancellationToken ct = default)
        => await db.EventViews.CountAsync(v => v.EventId == eventId, ct);

    /// <summary>Walks to the underlying Postgres exception, which EF wraps - the
    /// outer exception is a DbUpdateException whose InnerException carries the
    /// constraint violation. Npgsql always populates SqlState for a constraint
    /// failure, so the code is present whenever there is one to identify.</summary>
    private static bool IsUniqueViolation(DbUpdateException ex) =>
        ex.InnerException is PostgresException { SqlState: UniqueViolation };
}
