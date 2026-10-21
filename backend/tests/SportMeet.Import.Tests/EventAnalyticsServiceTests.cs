using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SportMeet.Application.Common;
using SportMeet.Application.Events;
using SportMeet.Domain.Entities;
using SportMeet.Domain.Enums;

namespace SportMeet.Import.Tests;

/// <summary>
/// Event analytics: who may read the numbers, and what a recorded view costs the
/// page that records it.
///
/// The fakes here take the deduplication rule on trust, and that is deliberate. "At
/// most one view per user per event per day" is enforced by a unique index, so the
/// only honest test of it is one that collides with a real index - a fake that
/// simulated it would be testing its own imitation of the constraint rather than the
/// constraint. What these cover is instead the two decisions that live in C# and
/// nowhere else: who is allowed to see a view count, and whether a failed insert can
/// reach a visitor's screen.
/// </summary>
public sealed class EventAnalyticsServiceTests
{
    private static readonly DateTimeOffset Start = new(2030, 5, 1, 10, 0, 0, TimeSpan.Zero);

    // ---- Only the host may read ----

    [Fact]
    public async Task Host_reads_views_joined_and_interested()
    {
        var host = Guid.NewGuid();
        var entity = Event(host);
        var views = new FakeViews(17);
        var service = new EventAnalyticsService(
            views, new FakeAnalyticsRepo(new FeedRow(entity, [], null, "Host", null, 4, 9)), User(host), new TestLogger());

        var result = await service.GetAsync(entity.Id);

        Assert.Equal(17, result.Views);
        Assert.Equal(4, result.JoinedCount);
        Assert.Equal(9, result.InterestedCount);
    }

    [Fact]
    public async Task A_non_host_gets_not_found_and_nothing_is_counted()
    {
        var host = Guid.NewGuid();
        var stranger = Guid.NewGuid();
        var entity = Event(host);
        var views = new FakeViews(17);
        var service = new EventAnalyticsService(
            views, new FakeAnalyticsRepo(new FeedRow(entity, [], null, "Host", null, 4, 9)), User(stranger), new TestLogger());

        var error = await Assert.ThrowsAsync<NotFoundException>(() => service.GetAsync(entity.Id));

        Assert.Equal(entity.Id, error.Id);
        // Not merely an unanswered question: the count is never even asked for, so a
        // refused call cannot be timed into a guess about how popular the event is.
        Assert.Equal(0, views.CountCalls);
    }

    [Fact]
    public async Task An_anonymous_caller_is_asked_to_sign_in_rather_than_shown_a_zero()
    {
        var entity = Event(Guid.NewGuid());
        var service = new EventAnalyticsService(
            new FakeViews(0), new FakeAnalyticsRepo(new FeedRow(entity, [], null, "Host", null, 0, 0)), User(null), new TestLogger());

        // The 422 shape rather than a 404: with nobody acting there is no ownership to
        // test, and "sign in" is the answer the caller can act on.
        await Assert.ThrowsAsync<DomainRuleException>(() => service.GetAsync(entity.Id));
    }

    [Fact]
    public async Task An_unknown_event_is_not_found_even_for_the_caller_who_claims_to_host_it()
    {
        var service = new EventAnalyticsService(
            new FakeViews(0), new FakeAnalyticsRepo(null), User(Guid.NewGuid()), new TestLogger());

        await Assert.ThrowsAsync<NotFoundException>(() => service.GetAsync(Guid.NewGuid()));
    }

    // ---- First view: the moment Interested is marked automatically ----

    [Fact]
    public async Task Only_a_signed_in_viewers_first_ever_view_counts_as_first()
    {
        var viewer = Guid.NewGuid();
        var entity = Event(Guid.NewGuid());
        var views = new FakeViews(1);
        var service = new EventAnalyticsService(
            views, new FakeAnalyticsRepo(new FeedRow(entity, [], null, "Host", null, 0, 0)), User(viewer), new TestLogger());

        Assert.True(await service.TryRecordViewAsync(entity.Id));
        // Same day again: not new, so someone who un-marked Interested stays un-marked.
        Assert.False(await service.TryRecordViewAsync(entity.Id));
    }

    [Fact]
    public async Task A_return_visit_on_a_later_day_is_not_a_first_view()
    {
        var entity = Event(Guid.NewGuid());
        var views = new FakeViews(1) { EarlierDays = 1 };
        var service = new EventAnalyticsService(
            views, new FakeAnalyticsRepo(new FeedRow(entity, [], null, "Host", null, 0, 0)), User(Guid.NewGuid()), new TestLogger());

        Assert.False(await service.TryRecordViewAsync(entity.Id));
    }

    [Fact]
    public async Task Anonymous_views_and_failures_are_never_first_views()
    {
        var entity = Event(Guid.NewGuid());
        var feed = new FakeAnalyticsRepo(new FeedRow(entity, [], null, "Host", null, 0, 0));

        Assert.False(await new EventAnalyticsService(new FakeViews(1), feed, User(null), new TestLogger())
            .TryRecordViewAsync(entity.Id));
        Assert.False(await new EventAnalyticsService(
                new ThrowingViews(() => new InvalidOperationException("down")), feed, User(Guid.NewGuid()), new TestLogger())
            .TryRecordViewAsync(entity.Id));
    }

    // ---- Recording a view never costs the visitor the page ----

    [Fact]
    public async Task Opening_a_detail_records_one_view_for_the_signed_in_viewer()
    {
        var viewer = Guid.NewGuid();
        var entity = Event(Guid.NewGuid());
        var views = new FakeViews(3);
        var service = new EventAnalyticsService(
            views, new FakeAnalyticsRepo(new FeedRow(entity, [], null, "Host", null, 0, 0)), User(viewer), new TestLogger());

        await service.TryRecordViewAsync(entity.Id);

        var recorded = Assert.Single(views.Recorded);
        Assert.Equal(entity.Id, recorded.EventId);
        Assert.Equal(viewer, recorded.UserId);
    }

    [Fact]
    public async Task An_anonymous_opening_is_recorded_with_no_user_rather_than_an_invented_one()
    {
        var entity = Event(Guid.NewGuid());
        var views = new FakeViews(1);
        var service = new EventAnalyticsService(
            views, new FakeAnalyticsRepo(new FeedRow(entity, [], null, "Host", null, 0, 0)), User(null), new TestLogger());

        await service.TryRecordViewAsync(entity.Id);

        // The nullable column is what lets anonymous views exist without sharing one
        // daily allowance across every visitor; a Guid.Empty here would silently
        // deduplicate all of them against each other.
        Assert.Null(Assert.Single(views.Recorded).UserId);
    }

    [Fact]
    public async Task A_failed_view_insert_returns_to_the_caller_instead_of_throwing()
    {
        var entity = Event(Guid.NewGuid());
        var service = new EventAnalyticsService(
            new ThrowingViews(() => new InvalidOperationException("database unavailable")),
            new FakeAnalyticsRepo(new FeedRow(entity, [], null, "Host", null, 0, 0)),
            User(entity.HostId),
            new TestLogger());

        // The event is what the visitor came for; a statistic may not turn it into an
        // error screen. That this completes at all is the assertion - the detail
        // endpoint calls it unguarded, so a throw here would fail the page.
        await service.TryRecordViewAsync(entity.Id);
    }

    [Fact]
    public async Task A_cancelled_request_is_not_reported_as_a_failure()
    {
        var entity = Event(Guid.NewGuid());
        var logger = new TestLogger();
        var service = new EventAnalyticsService(
            new ThrowingViews(() => new InvalidOperationException("aborted")),
            new FakeAnalyticsRepo(new FeedRow(entity, [], null, "Host", null, 0, 0)),
            User(entity.HostId),
            logger);

        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await service.TryRecordViewAsync(entity.Id, cts.Token);

        // The visitor navigated away. Nothing broke, and a warning here would fill the
        // log with non-events every time someone loads a page and leaves quickly.
        Assert.DoesNotContain(logger.Lines, l => l.StartsWith("Warning"));
    }

    [Fact]
    public async Task A_command_abort_after_a_deadline_is_noted_without_a_warning()
    {
        var entity = Event(Guid.NewGuid());
        var logger = new TestLogger();
        // The shape Npgsql produces when a visitor leaves mid-query: a cancellation
        // carrying the *command's* already-cancelled token, while the request's own
        // token is untouched. Type alone cannot tell this from a broken database, and
        // a filter on the request token misfiles it as a failure.
        using var commandToken = new CancellationTokenSource();
        await commandToken.CancelAsync();
        var abort = await Aborted(commandToken);
        var service = new EventAnalyticsService(
            new ThrowingViews(() => abort),
            new FakeAnalyticsRepo(new FeedRow(entity, [], null, "Host", null, 0, 0)),
            User(entity.HostId),
            logger);

        await service.TryRecordViewAsync(entity.Id);

        Assert.DoesNotContain(logger.Lines, l => l.StartsWith("Warning"));
    }

    [Fact]
    public async Task A_genuine_database_failure_is_logged_as_a_warning()
    {
        var entity = Event(Guid.NewGuid());
        var logger = new TestLogger();
        var service = new EventAnalyticsService(
            new ThrowingViews(() => new InvalidOperationException("connection refused")),
            new FakeAnalyticsRepo(new FeedRow(entity, [], null, "Host", null, 0, 0)),
            User(entity.HostId),
            logger);

        await service.TryRecordViewAsync(entity.Id);

        // Swallowed for the visitor's sake, not hidden: a tracking path that has quietly
        // stopped counting is exactly what a host's flat Views graph looks like later.
        Assert.Contains(logger.Lines, l => l.StartsWith("Warning"));
    }

    // ---- Helpers ----

    private static ICurrentUser User(Guid? id) => new FakeUser(id);

    /// <summary>A cancellation carrying an already-cancelled token - the shape of a
    /// database command aborted because the visitor stopped caring about the response.
    /// <see cref="TaskCanceledException"/> exposes its token read-only and has no
    /// constructor that takes one, so it is produced by cancelling a real delay; what
    /// the service inspects is the token, not how the exception was made.</summary>
    private static async Task<TaskCanceledException> Aborted(CancellationTokenSource source)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(30), source.Token);
        }
        catch (TaskCanceledException ex)
        {
            return ex;
        }

        throw new InvalidOperationException("The delay was expected to be cancelled.");
    }

    /// <summary>Captures the level of each log line, because what these tests are about
    /// is not whether the service talks but how loudly: a cancelled request logged at
    /// Warning is the noise that hides the real failure, and a broken database logged
    /// at Debug is a tracking path that stops counting unnoticed.</summary>
    private sealed class TestLogger : ILogger<EventAnalyticsService>
    {
        public readonly List<string> Lines = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
            => Lines.Add($"{logLevel}: {formatter(state, exception)}");
    }

    private static Event Event(Guid hostId) => new()
    {
        Id = Guid.NewGuid(),
        HostId = hostId,
        Title = "Run",
        VenueName = "Park",
        StartAt = Start,
        EndAt = Start.AddHours(1),
        MaxParticipants = 10,
        Status = EventStatus.Scheduled,
        CreatedAt = Start.AddDays(-1),
        UpdatedAt = Start.AddDays(-1),
    };

    private sealed class FakeUser(Guid? userId) : ICurrentUser
    {
        public Guid? UserId => userId;
        public bool IsDemo => false;
        public bool IsAdmin => false;
        public bool IsModerator => false;
        public bool CanPublishPublicEvents => false;
    }

    /// <summary>Records what it was asked to record. The deduplication rule belongs to
    /// the database's unique index, so this stores every attempt rather than pretending
    /// to filter out the ones the index would have refused.</summary>
    private sealed class FakeViews(int count) : IEventViewRepository
    {
        public readonly List<(Guid EventId, Guid? UserId)> Recorded = [];
        public int CountCalls;

        public Task<EventViewRecord> RecordViewAsync(
            Guid eventId, Guid? userId, DateTimeOffset viewedAt, CancellationToken ct = default)
        {
            // Mimics the daily unique index for the one day a test spans: a signed-in
            // repeat is not new; anonymous openings always are.
            var isNew = userId is null || !Recorded.Contains((eventId, userId));
            Recorded.Add((eventId, userId));
            return Task.FromResult(new EventViewRecord(count, isNew));
        }

        public Task<int> CountViewsAsync(Guid eventId, CancellationToken ct = default)
        {
            CountCalls++;
            return Task.FromResult(count);
        }

        /// <summary>Views from earlier days, which the daily index would have kept.</summary>
        public int EarlierDays { get; init; }

        public Task<int> CountViewsByUserAsync(Guid eventId, Guid userId, CancellationToken ct = default) =>
            Task.FromResult(EarlierDays + Recorded.Where(r => r == (eventId, userId)).Distinct().Count());
    }

    private sealed class ThrowingViews(Func<Exception> error) : IEventViewRepository
    {
        public Task<EventViewRecord> RecordViewAsync(
            Guid eventId, Guid? userId, DateTimeOffset viewedAt, CancellationToken ct = default)
            => Task.FromException<EventViewRecord>(error());

        public Task<int> CountViewsAsync(Guid eventId, CancellationToken ct = default)
            => throw new InvalidOperationException("database unavailable");

        public Task<int> CountViewsByUserAsync(Guid eventId, Guid userId, CancellationToken ct = default)
            => throw new InvalidOperationException("database unavailable");
    }

    /// <summary>Serves one canned feed row. Only <c>FindAsync</c> is reachable from
    /// analytics; the rest of the read interface is never called from here, and throws
    /// so that a future coupling shows up as a failing test rather than a silent pass.
    /// Deliberately not shared with <c>FakeJoinRepo</c>, which is built around
    /// mutating participant and request state that analytics never touches.</summary>
    private sealed class FakeAnalyticsRepo(FeedRow? found) : IEventRepository
    {
        public Task<FeedRow?> FindAsync(Guid id, CancellationToken ct = default)
            => Task.FromResult(found?.Event.Id == id ? found : null);

        public Task<(List<FeedRow> Rows, int TotalCount)> QueryAsync(EventQueryModel query, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<List<FeedRow>> ListHostedAsync(Guid hostId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<List<FeedRow>> ListPendingReviewAsync(CancellationToken ct = default) => throw new NotImplementedException();
        public Task<Sport?> FindSportBySlugAsync(string slug, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<Dictionary<string, Tag>> FindTagsByNamesAsync(IReadOnlyList<string> names, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<List<PopularTag>> ListPopularTagsAsync(int limit, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<List<string>> SuggestTagsAsync(string prefix, int limit, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<List<User>> ListParticipantsAsync(Guid eventId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<List<Guid>> ListJoinedEventIdsAsync(Guid userId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<List<Guid>> ListPendingJoinRequestEventIdsAsync(Guid userId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<List<Guid>> ListInterestedEventIdsAsync(Guid userId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<bool> TrySetStatusAsync(Guid eventId, Guid hostId, EventStatus expectedStatus, EventStatus status, DateTimeOffset now, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<bool> TrySetReviewStatusAsync(Guid eventId, EventStatus expectedStatus, EventStatus status, DateTimeOffset now, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<bool> RemoveParticipantAsync(Guid eventId, Guid userId, DateTimeOffset cancelledAt, CancellationToken ct = default) => throw new NotImplementedException();
        public Task AddAsync(Event eventEntity, CancellationToken ct = default) => throw new NotImplementedException();
        public Task SaveChangesAsync(CancellationToken ct = default) => throw new NotImplementedException();
        public Task ReplaceEventTagsAsync(Guid eventId, IReadOnlyList<string> names, DateTimeOffset now, CancellationToken ct = default) => throw new NotImplementedException();
        public Task RunInTransactionAsync(Func<CancellationToken, Task> action, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<Event?> FindWithLockAsync(Guid eventId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<bool> IsParticipantAsync(Guid eventId, Guid userId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<int> CountParticipantsAsync(Guid eventId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<int> CountInterestedAsync(Guid eventId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task AddParticipantAsync(Guid eventId, Guid userId, DateTimeOffset joinedAt, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<bool> IsInterestedAsync(Guid eventId, Guid userId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task AddInterestAsync(Guid eventId, Guid userId, DateTimeOffset createdAt, CancellationToken ct = default) => throw new NotImplementedException();
        public Task RemoveInterestAsync(Guid eventId, Guid userId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<JoinRequestOutcome> AddJoinRequestAsync(Guid eventId, Guid userId, DateTimeOffset requestedAt, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<List<JoinRequestRow>> ListJoinRequestsAsync(Guid eventId, Guid hostId, bool includeDecided, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<bool> TryApproveJoinRequestAsync(Guid eventId, Guid userId, Guid hostId, DateTimeOffset now, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<bool> TryRejectJoinRequestAsync(Guid eventId, Guid userId, Guid hostId, DateTimeOffset now, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<JoinRequestStatus?> FindMyJoinRequestStatusAsync(Guid eventId, Guid userId, CancellationToken ct = default) => throw new NotImplementedException();
    }
}
