using SportMeet.Domain.Entities;

namespace SportMeet.Application.Events;

/// <summary>
/// The row shape the browse and detail queries return, expressed here so
/// Application never references EF Core. Infrastructure fills it from
/// v_event_feed; the derived ParticipantCount is a COUNT over
/// event_participants rather than a stored column, which is what removes the
/// lost-update race a counter would introduce (IMPLEMENTATION_PLAN.md §2).
/// </summary>
public sealed record FeedRow(
    Event Event,
    IReadOnlyList<string> Tags,
    string? SportIcon,
    string HostName,
    string? HostPhotoUrl,
    int ParticipantCount);

/// <summary>A tag plus how many currently-visible events carry it. Lives here
/// rather than in Domain because "visible" is a query concern, not a property of
/// a tag.</summary>
public sealed record PopularTag(string Name, int Count);

/// <summary>
/// Persistence seam for the read side. Shaped around the two shapes the API
/// serves rather than around DbSet pass-through, so the SQL view, the Haversine
/// translation and the derived count all stay an implementation detail.
/// </summary>
public interface IEventRepository
{
    /// <summary>Feed rows plus the total for the same filter, in one call so the
    /// page and the "N events" header cannot disagree mid-request.</summary>
    Task<(List<FeedRow> Rows, int TotalCount)> QueryAsync(EventQueryModel query, CancellationToken ct = default);

    Task<FeedRow?> FindAsync(Guid id, CancellationToken ct = default);

    Task<Sport?> FindSportBySlugAsync(string slug, CancellationToken ct = default);

    /// <summary>Existing tag rows for the given normalized names, keyed by name so
    /// the create path can attach what exists and insert only what is missing.
    /// Names must already have come from TagNormalizer.</summary>
    Task<Dictionary<string, Tag>> FindTagsByNamesAsync(IReadOnlyList<string> names, CancellationToken ct = default);

    /// <summary>Inserts missing tag rows and relinks the event to exactly the
    /// given names. Idempotent and order-insensitive: it computes a diff against
    /// the event's current tags rather than deleting and reinserting the world,
    /// so an unchanged tag keeps its id and its row is never churned.
    /// Names must already be normalized and within TagNormalizer.MaxTagsPerEvent.</summary>
    Task ReplaceEventTagsAsync(Guid eventId, IReadOnlyList<string> names, DateTimeOffset now, CancellationToken ct = default);

    /// <summary>Tags on currently visible events (scheduled, starting no earlier
    /// than now), most used first. Mirrors ApplyFilters' baseline so a chip on the
    /// home page can never open a filter that returns nothing.</summary>
    Task<List<PopularTag>> ListPopularTagsAsync(int limit, CancellationToken ct = default);

    /// <summary>Tag names beginning with <paramref name="prefix"/>, cheapest
    /// first. Autocomplete only - there is no curated list to suggest from, so a
    /// brand-new database offers nothing here and the client falls back to
    /// Enter-to-create.</summary>
    Task<List<string>> SuggestTagsAsync(string prefix, int limit, CancellationToken ct = default);

    Task AddAsync(Event eventEntity, CancellationToken ct = default);

    /// <summary>Ordered for a stable avatar stack across reloads.</summary>
    Task<List<User>> ListParticipantsAsync(Guid eventId, CancellationToken ct = default);

    Task<bool> IsParticipantAsync(Guid eventId, Guid userId, CancellationToken ct = default);

    /// <summary>Every event id the user currently participates in, for the
    /// "My events → Joined" list. Includes past events (a past event you joined
    /// is still one you joined), which browse's upcoming-only feed would drop.</summary>
    Task<List<Guid>> ListJoinedEventIdsAsync(Guid userId, CancellationToken ct = default);

    /// <summary>The derived count behind v_event_feed - what a join compares
    /// against MaxParticipants while the event row is locked.</summary>
    Task<int> CountParticipantsAsync(Guid eventId, CancellationToken ct = default);

    /// <summary>Runs <paramref name="action"/> inside a database transaction,
    /// committing on success and rolling back on any exception. Join wraps the
    /// lock + capacity check + insert in this so they are one atomic step.</summary>
    Task RunInTransactionAsync(Func<CancellationToken, Task> action, CancellationToken ct = default);

    /// <summary>The event row locked FOR UPDATE, so a join's capacity check and
    /// insert are one atomic step - two racing joins cannot both take the last
    /// spot (IMPLEMENTATION_PLAN.md §2). Null when the id is unknown.</summary>
    Task<Event?> FindWithLockAsync(Guid eventId, CancellationToken ct = default);

    /// <summary>Adds the participant row. Throws when the composite key already
    /// exists; callers check membership first, so that only happens on a genuine
    /// race and stays a 500 rather than a silent double count.</summary>
    Task AddParticipantAsync(Guid eventId, Guid userId, DateTimeOffset joinedAt, CancellationToken ct = default);

    /// <summary>Removes the participant row; no-op when absent. Returns true when
    /// the event row itself was cancelled by this call (the host left).</summary>
    Task<bool> RemoveParticipantAsync(Guid eventId, Guid userId, DateTimeOffset cancelledAt, CancellationToken ct = default);
}

