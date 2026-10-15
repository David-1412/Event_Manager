using SportMeet.Application.Common;
using SportMeet.Application.Events;
using SportMeet.Application.Profiles;
using SportMeet.Domain.Entities;
using SportMeet.Domain.Enums;

namespace SportMeet.Import.Tests;

/// <summary>
/// The public profile's service-level contract.
///
/// These cover the decisions that live in C# and nowhere else. The visibility rules
/// themselves - which statuses and which private events a profile may show - are
/// enforced in SQL by ProfileRepository, and a fake that reimplemented them would be
/// testing its own imitation of a WHERE clause rather than the clause; those need a
/// database to be honest about, the same reason EventAnalyticsServiceTests declines to
/// fake its unique index.
///
/// What is genuinely in scope here is the shape of the disclosure: that an unknown id
/// is null rather than an empty profile, that the summary carries no email or analytics
/// field even by accident, that a Creator profile is the Moderator tier, and that the
/// activity cap is passed down rather than applied after fetching everything.
/// </summary>
public sealed class ProfileServiceTests
{
    private static readonly DateTimeOffset Now = new(2030, 5, 1, 10, 0, 0, TimeSpan.Zero);

    private static ProfileSubject Subject(Guid id) =>
        new(id, "Monash Badminton Club", "https://cdn/avatar.png", "Moderator", Now.AddDays(-400));

    private static ProfileStatsDto Stats() => new(42, 8, 15, 6);

    [Fact]
    public async Task Summary_exposes_only_public_fields()
    {
        var id = Guid.NewGuid();
        var service = new ProfileService(new FakeProfiles(Subject(id)));

        var summary = await service.GetSummaryAsync(id);

        Assert.NotNull(summary);
        Assert.Equal(id, summary!.UserId);
        Assert.Equal("Monash Badminton Club", summary.DisplayName);
        // The role travels as the persisted name. "Creator" is a display concern and
        // lives in the client's RoleBadge, so the API must not pre-bake the label.
        Assert.Equal("Moderator", summary.Role);
        Assert.Equal(Now.AddDays(-400), summary.MemberSince);
        Assert.Equal(42, summary.Stats.EventsCreated);
        Assert.Equal(8, summary.Stats.UpcomingEvents);
        Assert.Equal(6, summary.Stats.JoinedEvents);
        Assert.Equal(15, summary.Stats.InterestedEvents);
    }

    [Fact]
    public async Task Unknown_user_is_null_rather_than_an_empty_profile()
    {
        // A 200 with an empty profile for a made-up id would assert that the id
        // identifies somebody, and the client would render "someone with no activity"
        // for a person who does not exist.
        var service = new ProfileService(new FakeProfiles(null));
        Assert.Null(await service.GetSummaryAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task Summary_has_no_bio_or_organization_until_the_schema_carries_one()
    {
        // Both are null rather than invented. The fields exist on the contract so the
        // client can render a club layout the day the columns do, without a second
        // coordinated release - but null is the honest answer today.
        var id = Guid.NewGuid();
        var service = new ProfileService(new FakeProfiles(Subject(id)));

        var summary = await service.GetSummaryAsync(id);

        Assert.Null(summary!.Bio);
        Assert.Null(summary.OrganizationName);
        Assert.Equal("Person", summary.ProfileKind);
    }

    [Fact]
    public async Task Activity_limit_is_passed_to_the_repository_not_applied_afterwards()
    {
        // The cap has to reach the database. Applied client-side it would read every
        // row an account has ever touched in order to display a dozen.
        var id = Guid.NewGuid();
        var profiles = new FakeProfiles(Subject(id)) { FollowersCount = 127 };

        await new ProfileService(profiles).GetActivityAsync(id);

        Assert.Equal(ProfileService.ActivityLimit, profiles.RequestedActivityLimit);
    }

    [Fact]
    public async Task Summary_carries_follow_state_for_a_signed_in_caller()
    {
        var creator = Guid.NewGuid();
        var viewer = Guid.NewGuid();
        var profiles = new FakeProfiles(Subject(creator)) { FollowersCount = 127 };

        profiles.Edges.Add((viewer, creator));

        var summary = await new ProfileService(profiles, new FakeUser(viewer)).GetSummaryAsync(creator);

        Assert.Equal(127, summary!.FollowersCount);
        Assert.True(summary.IsFollowing);
    }

    [Fact]
    public async Task Anonymous_caller_is_never_asked_whether_they_follow()
    {
        // Not an optimisation. There is no identity to ask about, and a repository that
        // answered the question anyway would be inventing a caller.
        var creator = Guid.NewGuid();
        var profiles = new FakeProfiles(Subject(creator)) { FollowersCount = 127 };

        var summary = await new ProfileService(profiles, new FakeUser(null)).GetSummaryAsync(creator);

        Assert.Equal(127, summary!.FollowersCount);
        Assert.False(summary.IsFollowing);
    }

    [Fact]
    public async Task Own_profile_does_not_report_following_yourself()
    {
        // The control hides itself on your own profile, but the field would be read by
        // anything else that consumes the summary, and "you follow yourself" is never a
        // true thing worth reporting.
        var me = Guid.NewGuid();
        var profiles = new FakeProfiles(Subject(me)) { };

        profiles.Edges.Add((me, me));

        var summary = await new ProfileService(profiles, new FakeUser(me)).GetSummaryAsync(me);

        Assert.False(summary!.IsFollowing);
    }

    [Fact]
    public async Task Following_oneself_is_refused_before_anything_is_read_or_written()
    {
        var me = Guid.NewGuid();
        var profiles = new FakeProfiles(Subject(me));

        var error = await Assert.ThrowsAsync<DomainRuleException>(
            () => new ProfileService(profiles, new FakeUser(me)).FollowAsync(me));

        Assert.Contains("yourself", error.Message);
        Assert.Null(profiles.AddedFollow);
    }

    [Fact]
    public async Task Following_requires_a_signed_in_caller()
    {
        var profiles = new FakeProfiles(Subject(Guid.NewGuid()));
        var service = new ProfileService(profiles, new FakeUser(null));

        await Assert.ThrowsAsync<DomainRuleException>(() => service.FollowAsync(Guid.NewGuid()));
        Assert.Null(profiles.AddedFollow);
    }

    [Fact]
    public async Task Following_a_stranger_records_the_caller_as_the_follower()
    {
        // Direction is the whole feature. Reversing the pair would make the creator
        // follow the reader and would still pass a test that only counted rows.
        var creator = Guid.NewGuid();
        var viewer = Guid.NewGuid();
        var profiles = new FakeProfiles(Subject(creator)) { FollowersCount = 1 };

        var result = await new ProfileService(profiles, new FakeUser(viewer)).FollowAsync(creator);

        Assert.Equal((viewer, creator), profiles.AddedFollow);
        Assert.True(result.IsFollowing);
        Assert.Equal(1, result.FollowersCount);
    }

    [Fact]
    public async Task Unfollowing_removes_the_callers_edge_only()
    {
        var creator = Guid.NewGuid();
        var viewer = Guid.NewGuid();
        var profiles = new FakeProfiles(Subject(creator));

        var result = await new ProfileService(profiles, new FakeUser(viewer)).UnfollowAsync(creator);

        Assert.Equal((viewer, creator), profiles.RemovedFollow);
        Assert.False(result.IsFollowing);
    }

    [Fact]
    public async Task Following_twice_reports_following_rather_than_failing()
    {
        // The duplicate rule is a database constraint, not an API error, so the second
        // call must still answer with the state the caller wanted. A service that
        // reported the repository's "nothing was inserted" as a failure would make the
        // button read as broken after a retried tap.
        var creator = Guid.NewGuid();
        var viewer = Guid.NewGuid();
        var profiles = new FakeProfiles(Subject(creator)) { FollowersCount = 1 };
        profiles.Edges.Add((viewer, creator));
        var service = new ProfileService(profiles, new FakeUser(viewer));

        var again = await service.FollowAsync(creator);

        Assert.True(again.IsFollowing);
    }

    [Fact]
    public async Task Unfollowing_someone_not_followed_reports_not_following()
    {
        var creator = Guid.NewGuid();
        var viewer = Guid.NewGuid();
        var profiles = new FakeProfiles(Subject(creator));
        var service = new ProfileService(profiles, new FakeUser(viewer));

        var result = await service.UnfollowAsync(creator);

        Assert.False(result.IsFollowing);
    }

    [Fact]
    public async Task Following_a_made_up_user_is_not_recorded()
    {
        // Without the subject check this becomes a foreign-key violation and a 500.
        var profiles = new FakeProfiles(null);
        var service = new ProfileService(profiles, new FakeUser(Guid.NewGuid()));

        await Assert.ThrowsAsync<NotFoundException>(() => service.FollowAsync(Guid.NewGuid()));
        Assert.Null(profiles.AddedFollow);
    }

    [Fact]
    public async Task Event_lists_map_to_the_same_card_payload_browse_uses()
    {
        var id = Guid.NewGuid();
        var hosted = new FeedRow(
            HostedEvent(id),
            ["badminton"],
            "🏸",
            "Monash Badminton Club",
            "https://cdn/avatar.png",
            12,
            5,
            UserRole.Moderator);
        var profiles = new FakeProfiles(Subject(id)) { Hosted = [hosted] };

        var items = await new ProfileService(profiles).GetHostedEventsAsync(id);

        var item = Assert.Single(items);
        Assert.Equal(12, item.JoinedCount);
        Assert.Equal(5, item.InterestedCount);
        // Host id and role ride along, which is what makes a card's host line a link
        // with a Creator badge rather than plain text.
        Assert.Equal(id, item.HostId);
        Assert.Equal("Moderator", item.HostRole);
        Assert.Equal("Public", item.Visibility);
        Assert.True(item.IsPublished);
    }

    private static Event HostedEvent(Guid hostId) => new()
    {
        Id = Guid.NewGuid(),
        HostId = hostId,
        Title = "Tennis Saturday",
        VenueName = "Monash Courts",
        Lat = 0,
        Lng = 0,
        Timezone = "Australia/Melbourne",
        StartAt = Now.AddDays(3),
        EndAt = Now.AddDays(3).AddHours(2),
        MaxParticipants = 20,
        Status = EventStatus.Scheduled,
        Visibility = EventVisibility.Public,
        CreatedAt = Now.AddDays(-2),
        UpdatedAt = Now.AddDays(-2),
    };

    /// <summary>The caller, or null for "nobody is signed in". The profile read side
    /// treats a null identity as anonymous rather than as an error, which is what keeps
    /// the endpoint callable without a token.</summary>
    private sealed class FakeUser(Guid? userId) : ICurrentUser
    {
        public Guid? UserId { get; } = userId;
        public bool IsDemo => false;
        public bool IsAdmin => false;
        public bool CanPublishPublicEvents => false;
    }

    /// <summary>Records what the service asked for. Deliberately does not filter:


    /// the visibility rules belong to the SQL and are not reimplemented here.</summary>
    private sealed class FakeProfiles(ProfileSubject? subject) : IProfileRepository
    {
        public int? RequestedActivityLimit { get; private set; }
        public List<FeedRow> Hosted { get; init; } = [];

        public Task<ProfileSubject?> FindSubjectAsync(Guid userId, CancellationToken ct = default) =>
            Task.FromResult(subject is null ? null : subject with { Id = userId });

        public Task<ProfileStatsDto> GetStatsAsync(Guid userId, CancellationToken ct = default) =>
            Task.FromResult(Stats());

        public Task<List<FeedRow>> ListPublicHostedAsync(Guid userId, CancellationToken ct = default) =>
            Task.FromResult(Hosted);

        public Task<List<FeedRow>> ListPublicUpcomingAsync(Guid userId, CancellationToken ct = default) =>
            Task.FromResult(new List<FeedRow>());

        public Task<List<FeedRow>> ListPublicJoinedUpcomingAsync(Guid userId, CancellationToken ct = default) =>
            Task.FromResult(new List<FeedRow>());

        public Task<List<ProfileActivityDto>> GetActivityAsync(
            Guid userId, int limit, CancellationToken ct = default)
        {
            RequestedActivityLimit = limit;
            return Task.FromResult(new List<ProfileActivityDto>());
        }

        public Task<int> CountFollowersAsync(Guid userId, CancellationToken ct = default) =>
            Task.FromResult(FollowersCount);

        // Reads the set rather than a constant, so the service's read-back-after-write
        // is actually exercised: a fake that always returned the same flag would let a
        // service that reported the *requested* state rather than the resulting one
        // pass every follow test.
        public Task<bool> IsFollowingAsync(Guid userId, Guid followerId, CancellationToken ct = default) =>
            Task.FromResult(Edges.Contains((followerId, userId)));

        public Task<bool> AddFollowAsync(
            Guid followerUserId, Guid followedUserId, DateTimeOffset createdAt,
            CancellationToken ct = default)
        {
            AddedFollow = (followerUserId, followedUserId);
            var added = Edges.Add((followerUserId, followedUserId));
            return Task.FromResult(added);
        }

        public Task<bool> RemoveFollowAsync(
            Guid followerUserId, Guid followedUserId, CancellationToken ct = default)
        {
            RemovedFollow = (followerUserId, followedUserId);
            var removed = Edges.Remove((followerUserId, followedUserId));
            return Task.FromResult(removed);
        }

        public Task<List<Guid>> ListFollowersAsync(Guid followedUserId, CancellationToken ct = default) =>
            Task.FromResult(new List<Guid>());

        public Task<(IReadOnlyList<Guid> FollowerIds, string? HostName)> ListFollowersToNotifyAsync(
            Event published, CancellationToken ct = default) =>
            Task.FromResult(EmptyNotify);

        /// <summary>Set by the follow tests. Zero and false are the values the
        /// pre-existing summary assertions were written against, which is what lets
        /// those tests stay untouched.</summary>
        /// <summary>(follower, followed). Seeded by tests that need an existing edge
        /// and mutated by Add/Remove so the read-back reflects the write.</summary>
        public HashSet<(Guid Follower, Guid Followed)> Edges { get; } = [];

        public int FollowersCount { get; init; }
        public (Guid Follower, Guid Followed)? AddedFollow { get; private set; }
        public (Guid Follower, Guid Followed)? RemovedFollow { get; private set; }

        /// <summary>Named rather than written inline: a tuple-typed argument to
        /// Task.FromResult does not infer, and the empty result is what every test that
        /// is not about following needs.</summary>
        private static (IReadOnlyList<Guid> FollowerIds, string? HostName) EmptyNotify =>
            ([], null);
    }
}
