using SportMeet.Application.Common;
using SportMeet.Application.Events;
using SportMeet.Application.Notifications;
using SportMeet.Domain.Entities;
using SportMeet.Domain.Enums;

namespace SportMeet.Import.Tests;

/// <summary>The join-request approval workflow, held to the two things it must not
/// break: an open event behaves exactly as before, and a gated event cannot be
/// entered without the host's decision.</summary>
public sealed class JoinRequestApprovalTests
{
    private static readonly DateTimeOffset Start = new(2030, 5, 1, 10, 0, 0, TimeSpan.Zero);

    // ---- Open events are untouched ------------------------------------------

    [Fact]
    public async Task Open_event_joins_immediately_and_records_no_request()
    {
        var host = Guid.NewGuid();
        var member = Guid.NewGuid();
        var entity = Event(host, requiresApproval: false);
        var repo = new FakeJoinRepo(entity);
        var notifications = new FakeNotifications();
        var service = new EventService(repo, new FakeUser(member), notifications);

        await service.JoinAsync(entity.Id);

        Assert.Equal(1, repo.ParticipantCount);
        Assert.Empty(repo.Requests);
        Assert.Contains(notifications.Added, n => n.Type == "EventJoined");
    }

    // ---- Gated events --------------------------------------------------------

    [Fact]
    public async Task Gated_event_creates_pending_request_without_adding_participant()
    {
        var host = Guid.NewGuid();
        var member = Guid.NewGuid();
        var entity = Event(host, requiresApproval: true);
        var repo = new FakeJoinRepo(entity);
        var notifications = new FakeNotifications();
        var service = new EventService(repo, new FakeUser(member), notifications);

        await service.JoinAsync(entity.Id);

        var request = Assert.Single(repo.Requests);
        Assert.Equal(member, request.UserId);
        Assert.Equal(JoinRequestStatus.Pending, request.Status);
        Assert.Null(request.DecidedAt);
        // The gate is the whole point: no spot is taken.
        Assert.Equal(0, repo.ParticipantCount);
        Assert.Equal("JoinRequestSubmitted", Assert.Single(notifications.Added).Type);
    }

    [Fact]
    public async Task Host_is_notified_of_a_request_and_the_requester_is_not()
    {
        var host = Guid.NewGuid();
        var member = Guid.NewGuid();
        var entity = Event(host, requiresApproval: true);
        var repo = new FakeJoinRepo(entity);
        var notifications = new FakeNotifications();
        var service = new EventService(repo, new FakeUser(member), notifications);

        await service.JoinAsync(entity.Id);

        var note = Assert.Single(notifications.Added);
        Assert.Equal(host, note.UserId);
        Assert.Equal($"/events/{entity.Id}", note.Link);
    }

    [Fact]
    public async Task Repeated_request_queues_no_second_request_or_notification()
    {
        var host = Guid.NewGuid();
        var member = Guid.NewGuid();
        var entity = Event(host, requiresApproval: true);
        var repo = new FakeJoinRepo(entity);
        var notifications = new FakeNotifications();
        var service = new EventService(repo, new FakeUser(member), notifications);

        await service.JoinAsync(entity.Id);
        await service.JoinAsync(entity.Id);

        Assert.Single(repo.Requests);
        Assert.Single(notifications.Added);
    }

    [Fact]
    public async Task Full_gated_event_still_accepts_a_request_because_pending_reserves_nothing()
    {
        var host = Guid.NewGuid();
        var member = Guid.NewGuid();
        var entity = Event(host, requiresApproval: true, max: 1);
        var repo = new FakeJoinRepo(entity) { ParticipantCount = 1 };
        var service = new EventService(repo, new FakeUser(member), new FakeNotifications());

        await service.JoinAsync(entity.Id);

        Assert.Equal(JoinRequestStatus.Pending, Assert.Single(repo.Requests).Status);
    }

    [Fact]
    public async Task Host_cannot_request_to_join_their_own_gated_event()
    {
        var host = Guid.NewGuid();
        var entity = Event(host, requiresApproval: true);
        var repo = new FakeJoinRepo(entity);
        var service = new EventService(repo, new FakeUser(host), new FakeNotifications());

        await Assert.ThrowsAsync<DomainRuleException>(() => service.JoinAsync(entity.Id));
        Assert.Empty(repo.Requests);
    }
    // ---- Approve / reject ----------------------------------------------------

    [Fact]
    public async Task Approval_adds_participant_and_notifies_requester()
    {
        var host = Guid.NewGuid();
        var member = Guid.NewGuid();
        var entity = Event(host, requiresApproval: true);
        var repo = new FakeJoinRepo(entity);
        repo.Requests.Add(PendingRequest(entity.Id, member));
        var notifications = new FakeNotifications();
        var service = new EventService(repo, new FakeUser(host), notifications);

        await service.ApproveJoinRequestAsync(entity.Id, member);

        Assert.Equal(JoinRequestStatus.Approved, repo.Requests[0].Status);
        Assert.NotNull(repo.Requests[0].DecidedAt);
        Assert.Equal(1, repo.ParticipantCount);

        var note = Assert.Single(notifications.Added);
        Assert.Equal("JoinRequestApproved", note.Type);
        Assert.Equal(member, note.UserId);
    }

    [Fact]
    public async Task Rejection_keeps_member_out_and_notifies_requester()
    {
        var host = Guid.NewGuid();
        var member = Guid.NewGuid();
        var entity = Event(host, requiresApproval: true);
        var repo = new FakeJoinRepo(entity);
        repo.Requests.Add(PendingRequest(entity.Id, member));
        var notifications = new FakeNotifications();
        var service = new EventService(repo, new FakeUser(host), notifications);

        await service.RejectJoinRequestAsync(entity.Id, member);

        Assert.Equal(JoinRequestStatus.Rejected, repo.Requests[0].Status);
        Assert.Equal(0, repo.ParticipantCount);

        var note = Assert.Single(notifications.Added);
        Assert.Equal("JoinRequestRejected", note.Type);
        Assert.Equal(member, note.UserId);
    }

    [Fact]
    public async Task Approval_of_full_event_is_refused_and_leaves_request_pending()
    {
        var host = Guid.NewGuid();
        var member = Guid.NewGuid();
        var entity = Event(host, requiresApproval: true, max: 1);
        var repo = new FakeJoinRepo(entity) { ParticipantCount = 1 };
        repo.Requests.Add(PendingRequest(entity.Id, member));
        var service = new EventService(repo, new FakeUser(host), new FakeNotifications());

        await Assert.ThrowsAsync<EventFullException>(() =>
            service.ApproveJoinRequestAsync(entity.Id, member));

        Assert.Equal(JoinRequestStatus.Pending, repo.Requests[0].Status);
    }

    // ---- Authorization -------------------------------------------------------

    [Fact]
    public async Task Non_host_cannot_approve_or_reject()
    {
        var host = Guid.NewGuid();
        var member = Guid.NewGuid();
        var stranger = Guid.NewGuid();
        var entity = Event(host, requiresApproval: true);
        var repo = new FakeJoinRepo(entity);
        repo.Requests.Add(PendingRequest(entity.Id, member));
        var service = new EventService(repo, new FakeUser(stranger), new FakeNotifications());

        // 404 rather than 403, matching every other host-only path: a stranger is
        // not told the event exists for someone else.
        await Assert.ThrowsAsync<NotFoundException>(() =>
            service.ApproveJoinRequestAsync(entity.Id, member));
        await Assert.ThrowsAsync<NotFoundException>(() =>
            service.RejectJoinRequestAsync(entity.Id, member));

        Assert.Equal(JoinRequestStatus.Pending, repo.Requests[0].Status);
        Assert.Equal(0, repo.ParticipantCount);
    }

    [Fact]
    public async Task Host_cannot_approve_or_reject_their_own_request()
    {
        var host = Guid.NewGuid();
        var entity = Event(host, requiresApproval: true);
        var repo = new FakeJoinRepo(entity);
        // Smuggled in: the join path makes this row unreachable, but the endpoint
        // takes an arbitrary user id, so the rule has to hold on its own.
        repo.Requests.Add(PendingRequest(entity.Id, host));
        var service = new EventService(repo, new FakeUser(host), new FakeNotifications());

        await Assert.ThrowsAsync<DomainRuleException>(() =>
            service.ApproveJoinRequestAsync(entity.Id, host));
        await Assert.ThrowsAsync<DomainRuleException>(() =>
            service.RejectJoinRequestAsync(entity.Id, host));

        Assert.Equal(JoinRequestStatus.Pending, repo.Requests[0].Status);
    }

    [Fact]
    public async Task Signed_out_caller_cannot_review_requests()
    {
        var host = Guid.NewGuid();
        var member = Guid.NewGuid();
        var entity = Event(host, requiresApproval: true);
        var repo = new FakeJoinRepo(entity);
        repo.Requests.Add(PendingRequest(entity.Id, member));
        var service = new EventService(repo, new FakeUser(null), new FakeNotifications());

        await Assert.ThrowsAsync<DomainRuleException>(() =>
            service.ApproveJoinRequestAsync(entity.Id, member));
        Assert.Equal(JoinRequestStatus.Pending, repo.Requests[0].Status);
    }

    [Fact]
    public async Task A_decided_request_cannot_be_decided_again()
    {
        var host = Guid.NewGuid();
        var member = Guid.NewGuid();
        var entity = Event(host, requiresApproval: true);
        var repo = new FakeJoinRepo(entity);
        repo.Requests.Add(new EventJoinRequest
        {
            EventId = entity.Id,
            UserId = member,
            Status = JoinRequestStatus.Rejected,
            RequestedAt = Start,
            DecidedAt = Start,
        });
        var service = new EventService(repo, new FakeUser(host), new FakeNotifications());

        await Assert.ThrowsAsync<NotFoundException>(() =>
            service.ApproveJoinRequestAsync(entity.Id, member));
        Assert.Equal(0, repo.ParticipantCount);
    }
    // ---- No capacity limit ---------------------------------------------------

    [Fact]
    public async Task Unlimited_event_accepts_a_join_far_past_any_number_it_might_have_had()
    {
        // The regression this guards: a null MaxParticipants reaching a `current >=
        // MaxParticipants` comparison. C# lifts the operator, so null compares as
        // false and the join succeeds - correct by accident. The test is written
        // against a count that any stand-in value (0, int.MaxValue) would fail, so
        // the behaviour is pinned even if the comparison is later rewritten.
        var host = Guid.NewGuid();
        var member = Guid.NewGuid();
        var entity = Event(host, requiresApproval: false, max: null);
        var repo = new FakeJoinRepo(entity) { ParticipantCount = 5000 };
        var service = new EventService(repo, new FakeUser(member), new FakeNotifications());

        await service.JoinAsync(entity.Id);

        Assert.Equal(5001, repo.ParticipantCount);
    }

    [Fact]
    public async Task Unlimited_event_approves_a_request_without_a_capacity_check()
    {
        // Approval is the second place capacity is enforced, and the one a
        // half-finished change would miss: an unlimited event whose requests then
        // fail to approve would strand the host's queue with no way to fix it.
        var host = Guid.NewGuid();
        var member = Guid.NewGuid();
        var entity = Event(host, requiresApproval: true, max: null);
        var repo = new FakeJoinRepo(entity) { ParticipantCount = 5000 };
        repo.Requests.Add(PendingRequest(entity.Id, member));
        var service = new EventService(repo, new FakeUser(host), new FakeNotifications());

        await service.ApproveJoinRequestAsync(entity.Id, member);

        Assert.Equal(JoinRequestStatus.Approved, Assert.Single(repo.Requests).Status);
        Assert.Equal(5001, repo.ParticipantCount);
    }

    [Fact]
    public async Task Bounded_event_still_refuses_a_join_once_full()
    {
        // Proves the null branch above did not simply disable the capacity rule.
        var host = Guid.NewGuid();
        var member = Guid.NewGuid();
        var entity = Event(host, requiresApproval: false, max: 1);
        var repo = new FakeJoinRepo(entity) { ParticipantCount = 1 };
        var service = new EventService(repo, new FakeUser(member), new FakeNotifications());

        await Assert.ThrowsAsync<EventFullException>(() => service.JoinAsync(entity.Id));
        Assert.Equal(1, repo.ParticipantCount);
    }

    // ---- Fixtures ------------------------------------------------------------

    private static Event Event(Guid hostId, bool requiresApproval, int? max = 12) => new()
    {
        Id = Guid.NewGuid(),
        HostId = hostId,
        Host = new User { Id = hostId, Name = "Host" },
        Title = "Thursday Tennis",
        VenueName = "Seddon Park",
        Timezone = "Australia/Melbourne",
        StartAt = Start,
        EndAt = Start.AddHours(2),
        MaxParticipants = max,
        Status = EventStatus.Scheduled,
        Visibility = EventVisibility.Public,
        RequiresJoinApproval = requiresApproval,
    };

    private static EventJoinRequest PendingRequest(Guid eventId, Guid userId) => new()
    {
        EventId = eventId,
        UserId = userId,
        Status = JoinRequestStatus.Pending,
        RequestedAt = Start,
    };

    private sealed class FakeUser(Guid? userId) : ICurrentUser
    {
        public Guid? UserId => userId;
        public bool IsDemo => false;
        public bool IsAdmin => false;
        public bool IsModerator => false;
        public bool CanPublishPublicEvents => true;
    }

    private sealed class FakeNotifications : INotificationRepository
    {
        public readonly List<Notification> Added = [];

        public Task AddAsync(Notification notification, CancellationToken ct = default)
        {
            Added.Add(notification);
            return Task.CompletedTask;
        }

        public Task<(IReadOnlyList<NotificationDto> Items, int UnreadCount)> ListForUserAsync(
            Guid userId, int limit, CancellationToken ct = default)
            => throw new NotImplementedException();

        public Task<bool> MarkReadAsync(Guid id, Guid userId, CancellationToken ct = default)
            => throw new NotImplementedException();

        public Task MarkAllReadAsync(Guid userId, CancellationToken ct = default)
            => throw new NotImplementedException();
    }
}


