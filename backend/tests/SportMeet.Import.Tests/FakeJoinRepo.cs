using SportMeet.Application.Events;
using SportMeet.Domain.Entities;
using SportMeet.Domain.Enums;

namespace SportMeet.Import.Tests;

/// <summary>In-memory stand-in for the join-request workflow: an event, its
/// requests, its participant count. Deliberately literal - a request list is a List
/// and a participant count is an int - so a failing test says the workflow moved
/// something it should not have, rather than reporting that a mock was called in an
/// unexpected order.</summary>
internal sealed class FakeJoinRepo : IEventRepository
{
    public FakeJoinRepo(Event entity)
    {
        Found = new FeedRow(entity, [], null, "Host", null, 0, 0);
    }

    public FeedRow Found { get; }
    public int ParticipantCount { get; set; }
    public List<EventJoinRequest> Requests { get; } = [];

    /// <summary>Set to make the fake behave as though the caller does not own the
    /// event, which is what the real repository's host-scoped WHERE clause does.</summary>
    public Guid? ActingHost { get; set; }

    public Task RunInTransactionAsync(Func<CancellationToken, Task> action, CancellationToken ct = default)
        => action(ct);

    public Task<Event?> FindWithLockAsync(Guid eventId, CancellationToken ct = default)
        => Task.FromResult(Found.Event.Id == eventId ? Found.Event : null);

    public Task<FeedRow?> FindAsync(Guid id, CancellationToken ct = default)
        => Task.FromResult(Found.Event.Id == id ? Found : null);

    public Task<bool> IsParticipantAsync(Guid eventId, Guid userId, CancellationToken ct = default)
        => Task.FromResult(false);

    public Task<int> CountParticipantsAsync(Guid eventId, CancellationToken ct = default)
        => Task.FromResult(ParticipantCount);

    public Task AddParticipantAsync(Guid eventId, Guid userId, DateTimeOffset joinedAt, CancellationToken ct = default)
    {
        ParticipantCount++;
        return Task.CompletedTask;
    }

    public Task RemoveInterestAsync(Guid eventId, Guid userId, CancellationToken ct = default)
        => Task.CompletedTask;

    public Task<JoinRequestOutcome> AddJoinRequestAsync(
        Guid eventId, Guid userId, DateTimeOffset requestedAt, CancellationToken ct = default)
    {
        var existing = Requests.FirstOrDefault(r => r.EventId == eventId && r.UserId == userId);
        if (existing is null)
        {
            Requests.Add(new EventJoinRequest
            {
                EventId = eventId,
                UserId = userId,
                Status = JoinRequestStatus.Pending,
                RequestedAt = requestedAt,
            });
            return Task.FromResult(JoinRequestOutcome.Created);
        }

        if (existing.Status == JoinRequestStatus.Rejected)
        {
            existing.Status = JoinRequestStatus.Pending;
            existing.DecidedAt = null;
            return Task.FromResult(JoinRequestOutcome.Reopened);
        }

        return Task.FromResult(existing.Status == JoinRequestStatus.Approved
            ? JoinRequestOutcome.AlreadyApproved
            : JoinRequestOutcome.AlreadyPending);
    }

    public Task<List<JoinRequestRow>> ListJoinRequestsAsync(
        Guid eventId, Guid hostId, bool includeDecided, CancellationToken ct = default)
    {
        // Mirrors the production guard: a caller who is not the host sees nothing.
        if (ActingHost is not null && ActingHost != hostId)
        {
            return Task.FromResult(new List<JoinRequestRow>());
        }

        return Task.FromResult(Requests
            .Where(r => r.EventId == eventId && (includeDecided || r.Status == JoinRequestStatus.Pending))
            .OrderBy(r => r.RequestedAt)
            .Select(r => new JoinRequestRow(r.UserId, "Requester", null, r.Status, r.RequestedAt, r.DecidedAt))
            .ToList());
    }

    public Task<bool> TryApproveJoinRequestAsync(
        Guid eventId, Guid userId, Guid hostId, DateTimeOffset now, CancellationToken ct = default)
        => Decide(eventId, userId, hostId, now, JoinRequestStatus.Approved);

    public Task<bool> TryRejectJoinRequestAsync(
        Guid eventId, Guid userId, Guid hostId, DateTimeOffset now, CancellationToken ct = default)
        => Decide(eventId, userId, hostId, now, JoinRequestStatus.Rejected);

    private Task<bool> Decide(
        Guid eventId, Guid userId, Guid hostId, DateTimeOffset now, JoinRequestStatus status)
    {
        var owner = ActingHost ?? Found.Event.HostId;
        var request = Requests.FirstOrDefault(r =>
            r.EventId == eventId && r.UserId == userId
            && r.Status == JoinRequestStatus.Pending && owner == hostId);
        if (request is null)
        {
            return Task.FromResult(false);
        }

        request.Status = status;
        request.DecidedAt = now;

        // Approval is what takes the spot, so the fake writes the membership here
        // too - the same coupling the real repository commits in one SaveChanges.
        if (status == JoinRequestStatus.Approved)
        {
            ParticipantCount++;
        }

        return Task.FromResult(true);
    }

    public Task<JoinRequestStatus?> FindMyJoinRequestStatusAsync(
        Guid eventId, Guid userId, CancellationToken ct = default)
        => Task.FromResult(Requests.FirstOrDefault(r => r.EventId == eventId && r.UserId == userId)?.Status);

    // Not reached by these tests.
    public Task<(List<FeedRow> Rows, int TotalCount)> QueryAsync(EventQueryModel query, CancellationToken ct = default) => throw new NotImplementedException();
    public Task<List<FeedRow>> ListHostedAsync(Guid hostId, CancellationToken ct = default) => throw new NotImplementedException();
    public Task<Sport?> FindSportBySlugAsync(string slug, CancellationToken ct = default) => throw new NotImplementedException();
    public Task<Dictionary<string, Tag>> FindTagsByNamesAsync(IReadOnlyList<string> names, CancellationToken ct = default) => throw new NotImplementedException();
    public Task<List<PopularTag>> ListPopularTagsAsync(int limit, CancellationToken ct = default) => throw new NotImplementedException();
    public Task<List<string>> SuggestTagsAsync(string prefix, int limit, CancellationToken ct = default) => throw new NotImplementedException();
    public Task<List<User>> ListParticipantsAsync(Guid eventId, CancellationToken ct = default) => Task.FromResult(new List<User>());
    public Task<List<Guid>> ListJoinedEventIdsAsync(Guid userId, CancellationToken ct = default) => Task.FromResult(new List<Guid>());
    public Task<List<Guid>> ListPendingJoinRequestEventIdsAsync(Guid userId, CancellationToken ct = default) => Task.FromResult(new List<Guid>());
    public Task<int> CountInterestedAsync(Guid eventId, CancellationToken ct = default) => Task.FromResult(0);
    public Task<bool> IsInterestedAsync(Guid eventId, Guid userId, CancellationToken ct = default) => Task.FromResult(false);
    public Task AddInterestAsync(Guid eventId, Guid userId, DateTimeOffset createdAt, CancellationToken ct = default) => Task.CompletedTask;
    public Task<List<Guid>> ListInterestedEventIdsAsync(Guid userId, CancellationToken ct = default) => Task.FromResult(new List<Guid>());
    public Task<bool> TrySetStatusAsync(Guid eventId, Guid hostId, EventStatus expectedStatus, EventStatus status, DateTimeOffset now, CancellationToken ct = default) => Task.FromResult(false);
    public Task<List<FeedRow>> ListPendingReviewAsync(CancellationToken ct = default) => throw new NotImplementedException();
    public Task<bool> TrySetReviewStatusAsync(Guid eventId, EventStatus expectedStatus, EventStatus status, DateTimeOffset now, CancellationToken ct = default) => Task.FromResult(false);
    public Task<bool> RemoveParticipantAsync(Guid eventId, Guid userId, DateTimeOffset cancelledAt, CancellationToken ct = default) => Task.FromResult(false);
    public Task AddAsync(Event entity, CancellationToken ct = default) => Task.CompletedTask;
    public Task SaveChangesAsync(CancellationToken ct = default) => Task.CompletedTask;
    public Task ReplaceEventTagsAsync(Guid eventId, IReadOnlyList<string> names, DateTimeOffset now, CancellationToken ct = default) => Task.CompletedTask;
}

