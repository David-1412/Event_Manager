using SportMeet.Domain.Entities;

namespace SportMeet.Application.Imports;

public interface IImportRepository
{
    Task AddAsync(EventImport import, CancellationToken ct = default);

    /// <summary>Only the owner's import. Null for an unknown id or someone else's, so
    /// whether an import exists is never leaked across users.</summary>
    Task<EventImport?> FindAsync(Guid id, Guid userId, CancellationToken ct = default);

    /// <summary>Imports created at or after <paramref name="since"/>, across all users.
    /// Backs the global daily ceiling.</summary>
    Task<int> CountSinceAsync(DateTimeOffset since, CancellationToken ct = default);

    /// <summary>Blanks the pasted text on imports older than <paramref name="cutoff"/>.
    /// The structured columns stay: they are what the accuracy measurement uses, and the
    /// text is someone's personal correspondence.</summary>
    Task ClearSourceTextBeforeAsync(DateTimeOffset cutoff, CancellationToken ct = default);

    Task<bool> MetricExistsAsync(Guid eventId, CancellationToken ct = default);
    Task AddMetricAsync(EventPublishMetric metric, CancellationToken ct = default);

    Task SaveChangesAsync(CancellationToken ct = default);
}
