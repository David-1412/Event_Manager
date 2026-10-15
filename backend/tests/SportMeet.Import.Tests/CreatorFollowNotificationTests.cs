using SportMeet.Application.Events;
using SportMeet.Application.Notifications;
using SportMeet.Application.Profiles;
using SportMeet.Domain.Entities;
using SportMeet.Domain.Enums;

namespace SportMeet.Import.Tests;

/// <summary>
/// The fan-out rule the whole feature is judged on: a follower hears about an event
/// when it becomes publicly visible, and never before.
///
/// Written against the notifier rather than the endpoints that trigger it, because the
/// publish paths (a creator's immediate publish, an admin's approval, a resubmission
/// that publishes on edit) all funnel here, and the rules that say which of them may
/// announce anything are decided in this one place. A test per endpoint would assert
/// the same rule three times and would still not cover a fourth call site added later.
/// </summary>
public sealed class CreatorFollowNotificationTests
{
    private static readonly Guid Creator = Guid.Parse("22222222-2222-4222-8222-0000000000b2");
    private static readonly Guid EventId = Guid.Parse("33333333-3333-4333-8333-0000000000c3");
    private static readonly Guid FollowerA = Guid.NewGuid();
    private static readonly Guid FollowerB = Guid.NewGuid();

    private static Event Published(
        EventStatus status = EventStatus.Scheduled,
        EventVisibility visibility = EventVisibility.Public) => new()
    {
        Id = EventId,
        HostId = Creator,
        Title = "Tennis Saturday",
        Status = status,
        Visibility = visibility,
        StartAt = DateTimeOffset.UtcNow.AddDays(3),
        EndAt = DateTimeOffset.UtcNow.AddDays(3).AddHours(2),
    };

    private static CreatorFollowNotifier Notifier(
        FakeNotifications notifications,
        IReadOnlyList<Guid>? followers = null,
        string? hostName = "David Pan") =>
        new(new FakeFollowers(followers ?? [FollowerA], hostName), notifications);

    [Fact]
    public async Task Every_follower_gets_one_notification_linking_to_the_event()
    {
        var notifications = new FakeNotifications();

        var sent = await Notifier(notifications, [FollowerA, FollowerB]).PublishAsync(Published());

        Assert.Equal(2, sent);
        Assert.Equal(2, notifications.Written.Count);
        Assert.All(notifications.Written, n =>
        {
            Assert.Equal("New Event", n.Title);
            Assert.Equal($"/events/{EventId}", n.Link);
            Assert.Equal(NotificationTypes.FollowedCreatorPublished, n.Type);
            Assert.False(n.Read);
            // The spec's sentence, pinned whole rather than with Contains, so a
            // message that drops the creator's name but keeps the quotes still fails.
            Assert.Equal("David Pan has created a new event: \"Tennis Saturday\"", n.Message);
        });
        Assert.Equal([FollowerA, FollowerB], notifications.Written.Select(n => n.UserId));
    }

    [Theory]
    [InlineData(EventStatus.Draft)]
    [InlineData(EventStatus.PendingReview)]
    [InlineData(EventStatus.Rejected)]
    [InlineData(EventStatus.Cancelled)]
    public async Task An_event_that_is_not_publicly_visible_announces_nothing(EventStatus status)
    {
        // PendingReview is the case that matters most: it is what a Member's public
        // event looks like at create time, visible to nobody but its creator, and it
        // would tell followers about an event they cannot open and that an admin may
        // still refuse.
        var notifications = new FakeNotifications();

        var sent = await Notifier(notifications).PublishAsync(Published(status: status));

        Assert.Equal(0, sent);
        Assert.Empty(notifications.Written);
    }

    [Fact]
    public async Task Private_event_announces_nothing()
    {
        // The rule most likely to be forgotten, because the status here is a perfectly
        // publishable one. A private event is undiscoverable by design, and a
        // notification is the loudest possible way to make it discoverable.
        var notifications = new FakeNotifications();

        var sent = await Notifier(notifications)
            .PublishAsync(Published(visibility: EventVisibility.Private));

        Assert.Equal(0, sent);
        Assert.Empty(notifications.Written);
    }

    [Theory]
    [InlineData(EventStatus.Scheduled)]
    [InlineData(EventStatus.Published)]
    public async Task Either_live_status_announces(EventStatus status)
    {
        // Two statuses mean "live" in this schema, and a rule handling only one
        // silently skips half the events: Scheduled covers everything written before
        // the review workflow and everything a Creator publishes directly, Published is
        // what approval produces.
        var notifications = new FakeNotifications();

        Assert.Equal(1, await Notifier(notifications).PublishAsync(Published(status: status)));
        Assert.Single(notifications.Written);
    }

    [Fact]
    public async Task A_host_with_no_followers_writes_nothing()
    {
        // The common case rather than an edge one, and the path where an empty batch or
        // a null message would surface.
        var notifications = new FakeNotifications();

        Assert.Equal(0, await Notifier(notifications, []).PublishAsync(Published()));
        Assert.Empty(notifications.Written);
    }

    [Fact]
    public async Task A_self_follow_row_announces_nothing_to_the_creator()
    {
        // The database CHECK should make this unrepresentable; the query excludes it
        // anyway, and the fake honours that exclusion so a notifier that fanned out to
        // every id it was handed would be caught here.
        var notifications = new FakeNotifications();

        await Notifier(notifications, [Creator, FollowerA]).PublishAsync(Published());

        Assert.DoesNotContain(notifications.Written, n => n.UserId == Creator);
        Assert.Contains(notifications.Written, n => n.UserId == FollowerA);
    }

    /// <summary>Records what would have been sent. The real repository writes one row
    /// per call inside the caller's transaction; the test needs only the rows, so that
    /// is all this keeps.</summary>
    private sealed class FakeNotifications : INotificationRepository
    {
        public List<Notification> Written { get; } = [];

        public Task AddAsync(Notification notification, CancellationToken ct = default)
        {
            Written.Add(notification);
            return Task.CompletedTask;
        }

        public Task<(IReadOnlyList<NotificationDto> Items, int UnreadCount)> ListForUserAsync(
            Guid userId, int limit, CancellationToken ct = default) =>
            Task.FromResult(EmptyNotificationList);

        private static (IReadOnlyList<NotificationDto> Items, int UnreadCount) EmptyNotificationList =>
            ([], 0);

        public Task<bool> MarkReadAsync(Guid id, Guid userId, CancellationToken ct = default) =>
            Task.FromResult(true);

        public Task MarkAllReadAsync(Guid userId, CancellationToken ct = default) =>
            Task.CompletedTask;
    }

    /// <summary>Reproduces the shape of the production query: follower ids only for an
    /// event that is public and live, and never the host's own id. Both halves are the
    /// repository's job in production and are restated here so the notifier is tested
    /// against a repository that behaves rather than one that agrees.
    ///
    /// The limit of a fake is worth naming: it proves the notifier asks correctly, not
    /// that EF translates the real predicate. That half needs a database.</summary>
    private sealed class FakeFollowers(
        IReadOnlyList<Guid> followers,
        string? hostName = null) : IProfileRepository
    {
        public Task<(IReadOnlyList<Guid> FollowerIds, string? HostName)> ListFollowersToNotifyAsync(
            Event published, CancellationToken ct = default)
        {
            if (published.Visibility != EventVisibility.Public
                || !published.Status.IsPublished())
            {
                return Task.FromResult(EmptyNotify);
            }

            var ids = followers.Where(id => id != published.HostId).ToList();
            return Task.FromResult(((IReadOnlyList<Guid>)ids, hostName));
        }

        /// <summary>Named because a tuple-typed argument to Task.FromResult does not
        /// infer, and the cast on the list is what makes the second return agree with
        /// the interface's element type.</summary>
        private static (IReadOnlyList<Guid> FollowerIds, string? HostName) EmptyNotify =>
            ([], null);

        public Task<ProfileSubject?> FindSubjectAsync(Guid userId, CancellationToken ct = default) =>
            Task.FromResult<ProfileSubject?>(null);

        public Task<ProfileStatsDto> GetStatsAsync(Guid userId, CancellationToken ct = default) =>
            Task.FromResult(new ProfileStatsDto(0, 0, 0, 0));

        public Task<List<FeedRow>> ListPublicHostedAsync(Guid userId, CancellationToken ct = default) =>
            Task.FromResult(new List<FeedRow>());

        public Task<List<FeedRow>> ListPublicUpcomingAsync(Guid userId, CancellationToken ct = default) =>
            Task.FromResult(new List<FeedRow>());

        public Task<List<FeedRow>> ListPublicJoinedUpcomingAsync(
            Guid userId, CancellationToken ct = default) =>
            Task.FromResult(new List<FeedRow>());

        public Task<List<ProfileActivityDto>> GetActivityAsync(
            Guid userId, int limit, CancellationToken ct = default) =>
            Task.FromResult(new List<ProfileActivityDto>());

        public Task<int> CountFollowersAsync(Guid userId, CancellationToken ct = default) =>
            Task.FromResult(followers.Count);

        public Task<bool> IsFollowingAsync(Guid userId, Guid followerId, CancellationToken ct = default) =>
            Task.FromResult(false);

        public Task<bool> AddFollowAsync(
            Guid followerUserId, Guid followedUserId, DateTimeOffset createdAt,
            CancellationToken ct = default) => Task.FromResult(true);

        public Task<bool> RemoveFollowAsync(
            Guid followerUserId, Guid followedUserId, CancellationToken ct = default) =>
            Task.FromResult(true);

        public Task<List<Guid>> ListFollowersAsync(Guid followedUserId, CancellationToken ct = default) =>
            Task.FromResult(followers.ToList());
    }
}
