using SportMeet.Application.Common;
using SportMeet.Application.Events;
using SportMeet.Application.Notifications;
using SportMeet.Domain.Entities;
using SportMeet.Domain.Enums;

namespace SportMeet.Import.Tests;

public sealed class AdminEventServiceTests
{
    private static readonly DateTimeOffset Start = new(2030, 5, 1, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Admin_can_update_event_and_pending_public_approval_notifies_host()
    {
        var eventEntity = Event(EventStatus.PendingReview);
        var repository = new FakeAdminEventRepository(eventEntity);
        var notifications = new FakeNotifications();
        var service = new AdminEventService(repository, new FakeCurrentUser(true), notifications);

        await service.UpdateAsync(eventEntity.Id, Request(EventStatus.Published));

        Assert.Equal("Updated title", eventEntity.Title);
        Assert.Equal(EventStatus.Published, eventEntity.Status);
        Assert.Single(notifications.Added);
        Assert.Equal(eventEntity.HostId, notifications.Added[0].UserId);
        Assert.Equal($"/events/{eventEntity.Id}", notifications.Added[0].Link);
    }

    [Fact]
    public async Task Admin_delete_retains_event_and_sets_deleted_timestamp()
    {
        var eventEntity = Event(EventStatus.Scheduled);
        var repository = new FakeAdminEventRepository(eventEntity);
        var service = new AdminEventService(repository, new FakeCurrentUser(true), new FakeNotifications());

        await service.DeleteAsync(eventEntity.Id);

        Assert.NotNull(eventEntity.DeletedAt);
        Assert.Equal(eventEntity.DeletedAt, eventEntity.UpdatedAt);
    }

    [Fact]
    public async Task Non_admin_cannot_list_or_mutate_admin_events()
    {
        var eventEntity = Event(EventStatus.Scheduled);
        var repository = new FakeAdminEventRepository(eventEntity);
        var service = new AdminEventService(repository, new FakeCurrentUser(false), new FakeNotifications());

        await Assert.ThrowsAsync<NotFoundException>(() =>
            service.ListAsync(new AdminEventQuery(null, null, null, 1, 20)));
        await Assert.ThrowsAsync<NotFoundException>(() =>
            service.UpdateAsync(eventEntity.Id, Request(EventStatus.Completed)));
        await Assert.ThrowsAsync<NotFoundException>(() => service.DeleteAsync(eventEntity.Id));
    }

    private static Event Event(EventStatus status)
    {
        var hostId = Guid.NewGuid();
        return new Event
        {
            Id = Guid.NewGuid(),
            HostId = hostId,
            Host = new User { Id = hostId, Name = "Event host" },
            Title = "Original title",
            VenueName = "Seddon Park",
            Timezone = "Australia/Melbourne",
            StartAt = Start,
            EndAt = Start.AddHours(2),
            MaxParticipants = 12,
            Status = status,
            Visibility = EventVisibility.Public,
        };
    }

    private static UpdateAdminEventRequest Request(EventStatus status) => new()
    {
        Title = "Updated title",
        Description = "Updated description",
        VenueName = "Updated venue",
        Latitude = -37.81,
        Longitude = 144.89,
        Timezone = "Australia/Melbourne",
        StartAt = Start,
        EndAt = Start.AddHours(2),
        MaxParticipants = 12,
        Status = status,
        Visibility = EventVisibility.Public,
    };

    private sealed class FakeCurrentUser(bool isAdmin) : ICurrentUser
    {
        public Guid? UserId { get; } = Guid.NewGuid();
        public bool IsDemo => false;
        public bool IsAdmin { get; } = isAdmin;
        public bool CanPublishPublicEvents => IsAdmin;
    }

    private sealed class FakeNotifications : INotificationRepository
    {
        public List<Notification> Added { get; } = [];

        public Task AddAsync(Notification notification, CancellationToken ct = default)
        {
            Added.Add(notification);
            return Task.CompletedTask;
        }

        public Task<(IReadOnlyList<NotificationDto> Items, int UnreadCount)> ListForUserAsync(
            Guid userId, int limit, CancellationToken ct = default) => throw new NotImplementedException();

        public Task<bool> MarkReadAsync(Guid id, Guid userId, CancellationToken ct = default)
            => throw new NotImplementedException();

        public Task MarkAllReadAsync(Guid userId, CancellationToken ct = default)
            => throw new NotImplementedException();
    }

    private sealed class FakeAdminEventRepository(Event entity) : IAdminEventRepository
    {
        public Task<(IReadOnlyList<AdminEventDto> Items, int TotalCount)> QueryAdminAsync(
            string? search,
            AdminEventTimeFrame? timeFrame,
            EventStatus? status,
            int page,
            int pageSize,
            DateTimeOffset now,
            CancellationToken ct = default)
            => Task.FromResult<(IReadOnlyList<AdminEventDto>, int)>(([], 0));

        public Task<Event?> FindForAdminAsync(Guid id, CancellationToken ct = default)
            => Task.FromResult<Event?>(entity.Id == id && entity.DeletedAt is null ? entity : null);

        public Task SaveChangesAsync(CancellationToken ct = default) => Task.CompletedTask;

        public Task ReplaceEventTagsAsync(Guid eventId, IReadOnlyList<string> names, DateTimeOffset now, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task SoftDeleteAsync(Event eventEntity, DateTimeOffset deletedAt, CancellationToken ct = default)
        {
            eventEntity.DeletedAt = deletedAt;
            eventEntity.UpdatedAt = deletedAt;
            return Task.CompletedTask;
        }

        public Task RunInTransactionAsync(Func<CancellationToken, Task> action, CancellationToken ct = default)
            => action(ct);
    }
}
