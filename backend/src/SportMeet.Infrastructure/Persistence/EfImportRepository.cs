using Microsoft.EntityFrameworkCore;
using SportMeet.Application.Imports;
using SportMeet.Domain.Entities;

namespace SportMeet.Infrastructure.Persistence;

public sealed class EfImportRepository(AppDbContext db) : IImportRepository
{
    public async Task AddAsync(EventImport import, CancellationToken ct = default)
        => await db.EventImports.AddAsync(import, ct);

    public Task<EventImport?> FindAsync(Guid id, Guid userId, CancellationToken ct = default)
        => db.EventImports.FirstOrDefaultAsync(i => i.Id == id && i.UserId == userId, ct);

    public Task<int> CountSinceAsync(DateTimeOffset since, CancellationToken ct = default)
        => db.EventImports.CountAsync(i => i.CreatedAt >= since, ct);

    public Task ClearSourceTextBeforeAsync(DateTimeOffset cutoff, CancellationToken ct = default)
        => db.EventImports
            .Where(i => i.CreatedAt < cutoff && i.SourceText != null)
            .ExecuteUpdateAsync(s => s.SetProperty(i => i.SourceText, (string?)null), ct);

    public Task<bool> MetricExistsAsync(Guid eventId, CancellationToken ct = default)
        => db.EventPublishMetrics.AnyAsync(m => m.EventId == eventId, ct);

    public async Task AddMetricAsync(EventPublishMetric metric, CancellationToken ct = default)
        => await db.EventPublishMetrics.AddAsync(metric, ct);

    public Task SaveChangesAsync(CancellationToken ct = default) => db.SaveChangesAsync(ct);
}
