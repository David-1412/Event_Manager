namespace SportMeet.Application.Notifications;

public sealed record NotificationDto(
    Guid Id,
    string Title,
    string Message,
    string Type,
    string? Link,
    bool Read,
    DateTimeOffset CreatedAt);

public sealed record NotificationListDto(
    IReadOnlyList<NotificationDto> Items,
    int UnreadCount);
