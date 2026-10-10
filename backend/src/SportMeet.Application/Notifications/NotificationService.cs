using SportMeet.Application.Common;

namespace SportMeet.Application.Notifications;

public sealed class NotificationService(
    INotificationRepository notifications,
    ICurrentUser currentUser) : INotificationService
{
    private const int ListLimit = 100;

    public async Task<NotificationListDto> ListAsync(CancellationToken ct = default)
    {
        var userId = RequireUser();
        var (items, unreadCount) = await notifications.ListForUserAsync(userId, ListLimit, ct);
        return new NotificationListDto(items, unreadCount);
    }

    public async Task MarkReadAsync(Guid id, CancellationToken ct = default)
    {
        var userId = RequireUser();
        if (!await notifications.MarkReadAsync(id, userId, ct))
        {
            throw new NotFoundException("Notification", id, id);
        }
    }

    public Task MarkAllReadAsync(CancellationToken ct = default)
        => notifications.MarkAllReadAsync(RequireUser(), ct);

    private Guid RequireUser() =>
        currentUser.UserId
        ?? throw new DomainRuleException("You must be signed in to view notifications.");
}
