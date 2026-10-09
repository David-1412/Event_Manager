using SportMeet.Application.Common;
using SportMeet.Application.Events;
using SportMeet.Domain.Entities;
using SportMeet.Domain.Enums;

namespace SportMeet.Import.Tests;

/// <summary>
/// The public-event approval workflow, at the layer that owns the rules. The
/// repository is a scripted in-memory double, so these pin the decisions -
/// who publishes immediately, who lands in review, who may read or decide on an
/// unpublished event - without a database behind them.
/// </summary>
public class EventReviewWorkflowTests
{
    private static readonly Guid HostId = Guid.NewGuid();
    private static readonly Guid OtherId = Guid.NewGuid();
    private static readonly DateTimeOffset Start = new(2030, 5, 1, 10, 0, 0, TimeSpan.Zero);

    private sealed class Rig
    {
        public FakeEventRepo Repo = new();
        public Guid? CurrentUser = HostId;
        public bool IsAdmin;

        public EventService Service => new(Repo, new FakeUser(CurrentUser, IsAdmin));
    }

    private sealed class FakeUser(Guid? userId, bool isAdmin) : ICurrentUser
    {
        public Guid? UserId => userId;
        public bool IsDemo => false;
        public bool IsAdmin { get; } = isAdmin;
    }

    private static CreateEventDto Draft(EventVisibility visibility) => new()
    {
        Title = "Public pickup",
        StartAt = Start,
        EndAt = Start.AddHours(2),
        VenueName = "Seddon Park",
        Address = "42 Railway Ave, Seddon",
        Latitude = -37.81,
        Longitude = 144.89,
        MaxParticipants = 12,
        Visibility = visibility,
    };

    private static FeedRow Row(EventStatus status, Guid hostId) => new(
        new Event
        {
            Id = Guid.NewGuid(),
            HostId = hostId,
            Title = "Under review",
            VenueName = "Seddon Park",
            StartAt = Start,
            EndAt = Start.AddHours(2),
            MaxParticipants = 12,
            Status = status,
        },
        [], null, "Host", null, 0, 0);

    private sealed class FakeEventRepo : IEventRepository
    {
        public readonly List<Event> Added = [];
        public FeedRow? Found;
        public bool ReviewCallSucceeds = true;
        public (Guid Id, EventStatus Expected, EventStatus Applied)? LastReview;
        public List<FeedRow> PendingQueue = [];

        public Task AddAsync(Event eventEntity, CancellationToken ct = default)
        {
            Added.Add(eventEntity);
            // CreateAsync re-reads through GetAsync afterwards; make the row
            // readable so the write path completes the way the API's does.
            Found = new FeedRow(eventEntity, [], null, "Host", null, 0, 0);
            return Task.CompletedTask;
        }

        public Task<FeedRow?> FindAsync(Guid id, CancellationToken ct = default)
            => Task.FromResult(Found is { } f && f.Event.Id == id ? f : null);

        public Task<List<FeedRow>> ListPendingReviewAsync(CancellationToken ct = default)
            => Task.FromResult(PendingQueue);

        public Task<bool> TrySetReviewStatusAsync(Guid eventId, EventStatus expectedStatus, EventStatus status, DateTimeOffset now, CancellationToken ct = default)
        {
            LastReview = (eventId, expectedStatus, status);
            if (ReviewCallSucceeds && Found is { } f && f.Event.Id == eventId) f.Event.Status = status;
            return Task.FromResult(ReviewCallSucceeds);
        }

        public Task<(List<FeedRow> Rows, int TotalCount)> QueryAsync(EventQueryModel query, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<List<FeedRow>> ListHostedAsync(Guid hostId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<Sport?> FindSportBySlugAsync(string slug, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<Dictionary<string, Tag>> FindTagsByNamesAsync(IReadOnlyList<string> names, CancellationToken ct = default) => throw new NotImplementedException();
        public Task ReplaceEventTagsAsync(Guid eventId, IReadOnlyList<string> names, DateTimeOffset now, CancellationToken ct = default) => Task.CompletedTask;
        public Task<List<PopularTag>> ListPopularTagsAsync(int limit, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<List<string>> SuggestTagsAsync(string prefix, int limit, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<List<User>> ListParticipantsAsync(Guid eventId, CancellationToken ct = default) => Task.FromResult(new List<User>());
        public Task<bool> IsParticipantAsync(Guid eventId, Guid userId, CancellationToken ct = default) => Task.FromResult(false);
        public Task<List<Guid>> ListJoinedEventIdsAsync(Guid userId, CancellationToken ct = default) => Task.FromResult(new List<Guid>());
        public Task<int> CountParticipantsAsync(Guid eventId, CancellationToken ct = default) => Task.FromResult(0);
        public Task RunInTransactionAsync(Func<CancellationToken, Task> action, CancellationToken ct = default) => action(ct);
        public Task<Event?> FindWithLockAsync(Guid eventId, CancellationToken ct = default) => Task.FromResult(Found?.Event.Id == eventId ? Found.Event : null);
        public Task AddParticipantAsync(Guid eventId, Guid userId, DateTimeOffset joinedAt, CancellationToken ct = default) => Task.CompletedTask;
        public Task<bool> IsInterestedAsync(Guid eventId, Guid userId, CancellationToken ct = default) => Task.FromResult(false);
        public Task AddInterestAsync(Guid eventId, Guid userId, DateTimeOffset createdAt, CancellationToken ct = default) => Task.CompletedTask;
        public Task RemoveInterestAsync(Guid eventId, Guid userId, CancellationToken ct = default) => Task.CompletedTask;
        public Task<List<Guid>> ListInterestedEventIdsAsync(Guid userId, CancellationToken ct = default) => Task.FromResult(new List<Guid>());
        public Task<bool> TrySetStatusAsync(Guid eventId, Guid hostId, EventStatus expectedStatus, EventStatus status, DateTimeOffset now, CancellationToken ct = default) => Task.FromResult(false);
        public Task<bool> RemoveParticipantAsync(Guid eventId, Guid userId, DateTimeOffset cancelledAt, CancellationToken ct = default) => Task.FromResult(false);
    }


    // ---- create ---------------------------------------------------------------

    [Fact]
    public async Task Member_public_event_is_saved_as_PendingReview()
    {
        var rig = new Rig();

        var dto = await rig.Service.CreateAsync(Draft(EventVisibility.Public));

        var saved = Assert.Single(rig.Repo.Added);
        Assert.Equal(EventStatus.PendingReview, saved.Status);
        Assert.Equal(EventVisibility.Public, saved.Visibility);
        Assert.False(dto.IsPublished);
        Assert.Equal("PendingReview", dto.Status);
    }

    [Fact]
    public async Task Member_private_event_publishes_immediately()
    {
        var rig = new Rig();

        var dto = await rig.Service.CreateAsync(Draft(EventVisibility.Private));

        var saved = Assert.Single(rig.Repo.Added);
        Assert.Equal(EventStatus.Scheduled, saved.Status);
        Assert.True(dto.IsPublished);
    }

    [Fact]
    public async Task Admin_public_event_publishes_immediately()
    {
        var rig = new Rig { IsAdmin = true };

        await rig.Service.CreateAsync(Draft(EventVisibility.Public));

        Assert.Equal(EventStatus.Scheduled, Assert.Single(rig.Repo.Added).Status);
    }

    // ---- read visibility --------------------------------------------------------

    [Fact]
    public async Task Pending_event_is_404_for_a_stranger()
    {
        var rig = new Rig { CurrentUser = OtherId };
        rig.Repo.Found = Row(EventStatus.PendingReview, HostId);

        await Assert.ThrowsAsync<NotFoundException>(() => rig.Service.GetAsync(rig.Repo.Found.Event.Id));
    }

    [Fact]
    public async Task Pending_event_is_readable_by_its_creator_and_by_an_admin()
    {
        var row = Row(EventStatus.PendingReview, HostId);

        var asCreator = new Rig { Repo = { Found = row } };
        var mine = await asCreator.Service.GetAsync(row.Event.Id);
        Assert.False(mine.IsPublished);
        Assert.True(mine.IsHost);

        var asAdmin = new Rig { CurrentUser = OtherId, IsAdmin = true, Repo = { Found = row } };
        var seen = await asAdmin.Service.GetAsync(row.Event.Id);
        Assert.Equal(mine.Id, seen.Id);
    }


    // ---- review -----------------------------------------------------------------

    [Fact]
    public async Task Member_cannot_read_the_queue_or_decide()
    {
        var rig = new Rig();

        await Assert.ThrowsAsync<NotFoundException>(() => rig.Service.ListPendingReviewAsync());
        await Assert.ThrowsAsync<NotFoundException>(() => rig.Service.ApproveAsync(Guid.NewGuid()));
        await Assert.ThrowsAsync<NotFoundException>(() => rig.Service.RejectAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task Approve_moves_PendingReview_to_Published()
    {
        var rig = new Rig { IsAdmin = true };
        var row = Row(EventStatus.PendingReview, HostId);
        rig.Repo.Found = row;

        var dto = await rig.Service.ApproveAsync(row.Event.Id);

        Assert.Equal((row.Event.Id, EventStatus.PendingReview, EventStatus.Published), rig.Repo.LastReview);
        Assert.Equal("Published", dto.Status);
        Assert.True(dto.IsPublished);
    }

    [Fact]
    public async Task Reject_keeps_the_event_with_its_creator_but_unpublished()
    {
        var rig = new Rig { IsAdmin = true };
        var row = Row(EventStatus.PendingReview, HostId);
        rig.Repo.Found = row;

        var dto = await rig.Service.RejectAsync(row.Event.Id);

        Assert.Equal(EventStatus.Rejected, row.Event.Status);
        Assert.False(dto.IsPublished);
        // The creator still sees it; the flag the card renders from is the honest one.
        Assert.True(dto.IsHost);
    }

    [Fact]
    public async Task Deciding_something_that_is_not_awaiting_one_is_404()
    {
        var rig = new Rig { IsAdmin = true };
        rig.Repo.Found = Row(EventStatus.Scheduled, HostId);

        await Assert.ThrowsAsync<NotFoundException>(() => rig.Service.ApproveAsync(rig.Repo.Found.Event.Id));
        Assert.Null(rig.Repo.LastReview);
    }

    [Fact]
    public async Task Resubmitted_Rejected_event_can_be_approved()
    {
        var rig = new Rig { IsAdmin = true };
        var row = Row(EventStatus.Rejected, HostId);
        rig.Repo.Found = row;

        await rig.Service.ApproveAsync(row.Event.Id);

        Assert.Equal(EventStatus.Rejected, rig.Repo.LastReview!.Value.Expected);
        Assert.Equal(EventStatus.Published, row.Event.Status);
    }

    [Fact]
    public async Task Queue_lists_what_the_repository_returns()
    {
        var rig = new Rig { IsAdmin = true };
        rig.Repo.PendingQueue = [Row(EventStatus.PendingReview, HostId), Row(EventStatus.PendingReview, OtherId)];

        var queue = await rig.Service.ListPendingReviewAsync();

        Assert.Equal(2, queue.Count);
        Assert.All(queue, item => Assert.False(item.IsPublished));
    }
}
