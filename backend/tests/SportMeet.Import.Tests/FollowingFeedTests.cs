using SportMeet.Application.Common;
using SportMeet.Application.Events;
using SportMeet.Application.Notifications;
using SportMeet.Domain.Entities;
using SportMeet.Domain.Enums;

namespace SportMeet.Import.Tests;

/// <summary>
/// The Following feed's policy, on fakes: who may ask, and what reaches the
/// repository when they do. The SQL half â€” that the follow narrowing actually
/// translates and filters â€” lives in <see cref="FollowingFeedSqlTests"/>,
/// because translation is a property of the provider that no fake can stand in
/// for (the same lesson <see cref="ProfileActivitySqlTests"/> records).
/// </summary>
public sealed class FollowingFeedTests
{
    private sealed class FakeUser(Guid? id) : ICurrentUser
    {
        public Guid? UserId { get; } = id;
        public bool IsDemo => false;
        public bool IsAdmin => false;
        public bool CanPublishPublicEvents => false;
    }

    private sealed class FakeNotifications : INotificationRepository
    {
        public Task<(IReadOnlyList<NotificationDto> Items, int UnreadCount)> ListForUserAsync(
            Guid userId, int limit, CancellationToken ct = default)
            => throw new NotImplementedException();
        public Task<bool> MarkReadAsync(Guid id, Guid userId, CancellationToken ct = default)
            => throw new NotImplementedException();
        public Task MarkAllReadAsync(Guid userId, CancellationToken ct = default)
            => throw new NotImplementedException();
        public Task AddAsync(Notification notification, CancellationToken ct = default)
            => throw new NotImplementedException();
    }

    /// <summary>Records the single query the feed is allowed to make and answers
    /// it with an empty page. A second call would be the N+1 the requirement
    /// forbids, so <see cref="QueryCalls"/> counts rather than assumes.</summary>
    private sealed class CapturingEventRepository : IEventRepository
    {
        public EventQueryModel? SeenQuery { get; private set; }
        public int QueryCalls { get; private set; }

        public Task<(List<FeedRow> Rows, int TotalCount)> QueryAsync(
            EventQueryModel query, CancellationToken ct = default)
        {
            QueryCalls += 1;
            SeenQuery = query;
            return Task.FromResult((new List<FeedRow>(), 0));
        }

        public Task<FeedRow?> FindAsync(Guid id, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<JoinRequestOutcome> AddJoinRequestAsync(Guid eventId, Guid userId, DateTimeOffset requestedAt, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<List<JoinRequestRow>> ListJoinRequestsAsync(Guid eventId, Guid hostId, bool includeDecided, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<bool> TryApproveJoinRequestAsync(Guid eventId, Guid userId, Guid hostId, DateTimeOffset now, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<bool> TryRejectJoinRequestAsync(Guid eventId, Guid userId, Guid hostId, DateTimeOffset now, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<JoinRequestStatus?> FindMyJoinRequestStatusAsync(Guid eventId, Guid userId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<List<FeedRow>> ListHostedAsync(Guid hostId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Sport?> FindSportBySlugAsync(string slug, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Dictionary<string, Tag>> FindTagsByNamesAsync(IReadOnlyList<string> names, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<List<PopularTag>> ListPopularTagsAsync(int limit, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<List<string>> SuggestTagsAsync(string prefix, int limit, CancellationToken ct = default) => throw new NotSupportedException();
        public Task AddAsync(Event eventEntity, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<List<User>> ListParticipantsAsync(Guid eventId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<bool> IsParticipantAsync(Guid eventId, Guid userId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<List<Guid>> ListJoinedEventIdsAsync(Guid userId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<List<Guid>> ListInterestedEventIdsAsync(Guid userId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<List<Guid>> ListPendingJoinRequestEventIdsAsync(Guid userId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<int> CountParticipantsAsync(Guid eventId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<int> CountInterestedAsync(Guid eventId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task RunInTransactionAsync(Func<CancellationToken, Task> action, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Event?> FindWithLockAsync(Guid eventId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task AddParticipantAsync(Guid eventId, Guid userId, DateTimeOffset joinedAt, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<bool> IsInterestedAsync(Guid eventId, Guid userId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task AddInterestAsync(Guid eventId, Guid userId, DateTimeOffset createdAt, CancellationToken ct = default) => throw new NotSupportedException();
        public Task RemoveInterestAsync(Guid eventId, Guid userId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<bool> RemoveParticipantAsync(Guid eventId, Guid userId, DateTimeOffset cancelledAt, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<bool> TrySetStatusAsync(Guid eventId, Guid hostId, EventStatus expectedStatus, EventStatus status, DateTimeOffset now, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<List<FeedRow>> ListPendingReviewAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public Task<bool> TrySetReviewStatusAsync(Guid eventId, EventStatus expectedStatus, EventStatus status, DateTimeOffset now, CancellationToken ct = default) => throw new NotSupportedException();
        public Task ReplaceEventTagsAsync(Guid eventId, IReadOnlyList<string> names, DateTimeOffset now, CancellationToken ct = default) => throw new NotSupportedException();
        public Task SaveChangesAsync(CancellationToken ct = default) => throw new NotSupportedException();
    }

    private static EventService ServiceWith(ICurrentUser user, CapturingEventRepository repo) =>
        new(repo, user, new FakeNotifications());

    [Fact]
    public async Task Following_narrows_the_query_to_the_viewer_in_one_call()
    {
        var viewer = Guid.NewGuid();
        var repo = new CapturingEventRepository();
        var service = ServiceWith(new FakeUser(viewer), repo);

        await service.ListFollowingAsync(new EventQueryModel { Search = "run", TagName = "running" });

        // One repository call: the follow narrowing rides that query rather than
        // a fetch per followed creator, so the count of creators cannot move the
        // number of round trips.
        Assert.Equal(1, repo.QueryCalls);
        Assert.NotNull(repo.SeenQuery);
        Assert.Equal(viewer, repo.SeenQuery!.FollowedByUserId);
        Assert.Equal("run", repo.SeenQuery.Search);
        Assert.Equal("running", repo.SeenQuery.TagName);
    }

    [Fact]
    public async Task Following_carries_every_browse_filter_across()
    {
        var viewer = Guid.NewGuid();
        var repo = new CapturingEventRepository();
        var service = ServiceWith(new FakeUser(viewer), repo);

        await service.ListFollowingAsync(new EventQueryModel
        {
            DateFilter = EventDateFilter.Today,
            RadiusKm = 5,
            OriginLat = -37.8136,
            OriginLng = 144.9631,
            Sort = EventSort.Distance,
            Page = 3,
            PageSize = 50,
        });

        // Following is a narrowed browse, not a second feed implementation, so
        // the controls built for browse keep working on it untouched.
        var seen = repo.SeenQuery!;
        Assert.Equal(EventDateFilter.Today, seen.DateFilter);
        Assert.Equal(5, seen.RadiusKm);
        Assert.Equal(-37.8136, seen.OriginLat);
        Assert.Equal(144.9631, seen.OriginLng);
        Assert.Equal(EventSort.Distance, seen.Sort);
        Assert.Equal(3, seen.Page);
        Assert.Equal(50, seen.PageSize);
    }

    [Fact]
    public async Task Following_refuses_an_anonymous_caller_before_querying()
    {
        var repo = new CapturingEventRepository();
        var service = ServiceWith(new FakeUser(null), repo);

        await Assert.ThrowsAsync<DomainRuleException>(
            () => service.ListFollowingAsync(new EventQueryModel()));

        // Refused rather than answered with an empty list: an anonymous feed
        // resolved against the seeded viewer would show one user's social graph
        // wearing another's face.
        Assert.Equal(0, repo.QueryCalls);
        Assert.Null(repo.SeenQuery);
    }

    [Fact]
    public async Task Browse_leaves_the_follow_filter_unset_for_a_signed_in_viewer()
    {
        var repo = new CapturingEventRepository();
        var service = ServiceWith(new FakeUser(Guid.NewGuid()), repo);

        await service.ListAsync(new EventQueryModel());

        // Being signed in must not silently narrow the ordinary feed to the
        // viewer's graph — only the Following entry point sets the filter.
        Assert.Null(repo.SeenQuery!.FollowedByUserId);
    }
}
