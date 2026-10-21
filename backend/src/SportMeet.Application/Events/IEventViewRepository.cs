using SportMeet.Domain.Entities;

namespace SportMeet.Application.Events;

/// <summary>What a host is allowed to see about one of their own events.
///
/// Three numbers, deliberately not one table: <see cref="Views"/> is the private
/// one and the other two already ship to everyone on the browse card. Returning
/// them together is what lets the host panel be one request rather than the panel
/// re-deriving public counts it could have read off the page.
///
/// <see cref="Views"/> counts page openings, deduplicated to one per signed-in
/// person per event per day, so it is a reach figure and not a hit count.
/// Serializes as <c>views</c> / <c>joinedCount</c> / <c>interestedCount</c>.</summary>
public sealed record EventAnalyticsDto(int Views, int JoinedCount, int InterestedCount);

/// <summary>
/// Persistence seam for event view tracking.
///
/// Separate from <see cref="IEventRepository"/> on purpose. That interface is the
/// browse and detail read path and is already large; view tracking shares nothing
/// with it - no view, no feed row, no DTO - and a second implementation seam keeps
/// the analytics path from being reachable by, or coupled to, a feed change.
/// </summary>
/// <param name="Count">The event's view total afterwards.</param>
/// <param name="IsNew">A row was inserted - false for a repeat view by the same
/// signed-in person on the same day.</param>
public sealed record EventViewRecord(int Count, bool IsNew);

public interface IEventViewRepository
{
    /// <summary>Record that <paramref name="userId"/> opened <paramref name="eventId"/>,
    /// returning the host's view count for the event afterwards.
    ///
    /// Returns the count rather than a "was it recorded" flag because the caller needs
    /// the number either way: a duplicate-today insert is not a failure, it is the
    /// rule working, and the page still has to show a view total.
    ///
    /// A null <paramref name="userId"/> is an anonymous opening. It is recorded and
    /// counted but deduplicates against nothing - see
    /// <see cref="EventView.UserId"/>.</summary>
    Task<EventViewRecord> RecordViewAsync(
        Guid eventId, Guid? userId, DateTimeOffset viewedAt, CancellationToken ct = default);

    /// <summary>How many view rows this signed-in user has for the event, across all
    /// days. One, straight after a view that was <see cref="EventViewRecord.IsNew"/>,
    /// means that view was their first ever.</summary>
    Task<int> CountViewsByUserAsync(Guid eventId, Guid userId, CancellationToken ct = default);

    /// <summary>Every recorded view of one event. Not host-scoped: authorization is
    /// the caller's, and this is only ever reached behind it.</summary>
    Task<int> CountViewsAsync(Guid eventId, CancellationToken ct = default);
}
