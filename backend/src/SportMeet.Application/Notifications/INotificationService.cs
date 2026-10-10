namespace SportMeet.Application.Notifications;

public interface INotificationService
{
    Task<NotificationListDto> ListAsync(CancellationToken ct = default);

    Task MarkReadAsync(Guid id, CancellationToken ct = default);

    Task MarkAllReadAsync(CancellationToken ct = default);
}
