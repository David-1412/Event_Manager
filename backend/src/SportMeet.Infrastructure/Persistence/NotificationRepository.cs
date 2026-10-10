using Microsoft.EntityFrameworkCore;
using SportMeet.Application.Notifications;
using SportMeet.Domain.Entities;

namespace SportMeet.Infrastructure.Persistence;

public sealed class NotificationRepository(AppDbContext db) : INotificationRepository
{
    public async Task<(IReadOnlyList<NotificationDto> Items, int UnreadCount)> ListForUserAsync(
        Guid userId, int limit, CancellationToken ct = default)
    {
        var unreadCount = await db.Notifications
            .CountAsync(x => x.UserId == userId && !x.Read, ct);

        var items = await db.Notifications.AsNoTracking()
            .Where(x => x.UserId == userId)
            .OrderByDescending(x => x.CreatedAt)
            .ThenByDescending(x => x.Id)
            .Take(limit)
            .Select(x => new NotificationDto(x.Id, x.Title, x.Message, x.Type, x.Link, x.Read, x.CreatedAt))
            .ToListAsync(ct);

        return (items, unreadCount);
    }

    public async Task<bool> MarkReadAsync(Guid id, Guid userId, CancellationToken ct = default)
    {
        var updated = await db.Notifications
            .Where(x => x.Id == id && x.UserId == userId && !x.Read)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.Read, true), ct);
        return updated == 1 || await db.Notifications.AnyAsync(x => x.Id == id && x.UserId == userId, ct);
    }

    public async Task MarkAllReadAsync(Guid userId, CancellationToken ct = default)
        => await db.Notifications
            .Where(x => x.UserId == userId && !x.Read)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.Read, true), ct);

    public async Task AddAsync(Notification notification, CancellationToken ct = default)
    {
        db.Notifications.Add(notification);
        await db.SaveChangesAsync(ct);
    }
}
