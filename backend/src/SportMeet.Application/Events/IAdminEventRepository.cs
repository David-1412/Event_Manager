using SportMeet.Domain.Entities;
using SportMeet.Domain.Enums;

namespace SportMeet.Application.Events;

public interface IAdminEventRepository
{
    Task<(IReadOnlyList<AdminEventDto> Items, int TotalCount)> QueryAdminAsync(
        string? search,
        AdminEventTimeFrame? timeFrame,
        EventStatus? status,
        int page,
        int pageSize,
        DateTimeOffset now,
        CancellationToken ct = default);

    Task<Event?> FindForAdminAsync(Guid id, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
    Task ReplaceEventTagsAsync(Guid eventId, IReadOnlyList<string> names, DateTimeOffset now, CancellationToken ct = default);
    Task SoftDeleteAsync(Event eventEntity, DateTimeOffset deletedAt, CancellationToken ct = default);
    Task RunInTransactionAsync(Func<CancellationToken, Task> action, CancellationToken ct = default);

    /// <summary>See <see cref="IEventRepository.SetSeriesReviewStatusAsync"/>.</summary>
    Task<int> SetSeriesReviewStatusAsync(Guid seriesId, Guid exceptEventId, EventStatus expectedStatus, EventStatus status, DateTimeOffset now, CancellationToken ct = default)
        => Task.FromResult(0);
}
