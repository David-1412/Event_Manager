using SportMeet.Domain.Entities;

namespace SportMeet.Application.Notifications;

public interface INotificationRepository
{
    Task<(IReadOnlyList<NotificationDto> Items, int UnreadCount)> ListForUserAsync(
        Guid userId, int limit, CancellationToken ct = default);

    Task<bool> MarkReadAsync(Guid id, Guid userId, CancellationToken ct = default);

    Task MarkAllReadAsync(Guid userId, CancellationToken ct = default);

    Task AddAsync(Notification notification, CancellationToken ct = default);
}
