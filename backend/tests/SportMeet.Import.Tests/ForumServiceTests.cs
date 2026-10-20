using System.Text.Json;
using SportMeet.Application.Common;
using SportMeet.Application.Forum;
using SportMeet.Domain.Entities;
using SportMeet.Domain.Enums;

namespace SportMeet.Import.Tests;

/// <summary>
/// The forum's rules that do not need a database: who an anonymous post names, how
/// paging cursors round-trip, and what creating a post requires. The anonymity tests
/// assert on serialized JSON rather than on the DTO's properties, because the promise
/// is about what leaves the server - a field nobody reads in C# but that still ships
/// over the wire would be the leak.
/// </summary>
public sealed class ForumServiceTests
{
    private static readonly Guid Author = Guid.Parse("00000000-0000-0000-0000-0000000000a1");
    private static readonly Guid Reader = Guid.Parse("00000000-0000-0000-0000-0000000000b2");
    private static readonly ForumCategory Qa = new() { Id = 1, Name = "Q&A / Help", Slug = "qa", SortOrder = 1 };

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task Anonymous_post_does_not_name_its_author_to_other_readers()
    {
        var repo = new FakeForum(Row(anonymous: true));
        var post = await new ForumService(repo, new FakeUser(Reader)).GetPostAsync(repo.Rows[0].Id);

        var json = JsonSerializer.Serialize(post, Json);
        Assert.Null(post.Author);
        Assert.False(post.IsMine);
        Assert.DoesNotContain(Author.ToString(), json);
        Assert.DoesNotContain("Real Name", json);
    }

    [Fact]
    public async Task Anonymous_post_is_hidden_from_signed_out_readers_in_the_feed()
    {
        var repo = new FakeForum(Row(anonymous: true));
        var page = await new ForumService(repo).ListPostsAsync(null, null, null, null);

        var json = JsonSerializer.Serialize(page, Json);
        Assert.Null(page.Items.Single().Author);
        Assert.DoesNotContain(Author.ToString(), json);
    }

    [Fact]
    public async Task Author_still_recognises_their_own_anonymous_post()
    {
        var repo = new FakeForum(Row(anonymous: true));
        var post = await new ForumService(repo, new FakeUser(Author)).GetPostAsync(repo.Rows[0].Id);

        Assert.True(post.IsMine);
        Assert.Equal(Author, post.Author?.Id);
    }

    [Fact]
    public async Task Admin_sees_who_wrote_an_anonymous_post()
    {
        var repo = new FakeForum(Row(anonymous: true));
        var post = await new ForumService(repo, new FakeUser(Reader, isAdmin: true)).GetPostAsync(repo.Rows[0].Id);

        Assert.Equal(Author, post.Author?.Id);
        Assert.False(post.IsMine);
    }

    [Fact]
    public async Task Moderator_role_does_not_unmask_anonymous_posts()
    {
        // Moderator is the "Creator" tier for clubs, not a moderation role.
        var repo = new FakeForum(Row(anonymous: true));
        var moderator = new FakeUser(Reader, isAdmin: false, canPublish: true);
        var post = await new ForumService(repo, moderator).GetPostAsync(repo.Rows[0].Id);

        Assert.Null(post.Author);
    }

    [Fact]
    public async Task Named_post_shows_its_author()
    {
        var repo = new FakeForum(Row(anonymous: false));
        var post = await new ForumService(repo).GetPostAsync(repo.Rows[0].Id);

        Assert.Equal("Real Name", post.Author?.Name);
    }

    [Fact]
    public async Task Feed_truncates_long_bodies_and_the_post_page_does_not()
    {
        var repo = new FakeForum(Row(anonymous: false, content: new string('x', 1000)));
        var service = new ForumService(repo);

        var card = (await service.ListPostsAsync(null, null, null, null)).Items.Single();
        var full = await service.GetPostAsync(repo.Rows[0].Id);

        Assert.True(card.IsTruncated);
        Assert.Equal(ForumService.ExcerptLength, card.Content.Length);
        Assert.False(full.IsTruncated);
        Assert.Equal(1000, full.Content.Length);
    }

    [Fact]
    public async Task Next_cursor_is_present_only_when_another_page_exists()
    {
        var repo = new FakeForum(Row(seq: 3), Row(seq: 2), Row(seq: 1));
        var service = new ForumService(repo);

        var first = await service.ListPostsAsync(null, "new", null, 2);
        Assert.Equal(2, first.Items.Count);
        Assert.NotNull(first.NextCursor);

        var second = await service.ListPostsAsync(null, "new", first.NextCursor, 2);
        Assert.Single(second.Items);
        Assert.Null(second.NextCursor);
        Assert.Equal(2, repo.LastQuery!.After!.Seq);
    }

    [Fact]
    public async Task Following_and_mine_feeds_scope_the_query_to_the_caller()
    {
        var repo = new FakeForum(Row());
        var service = new ForumService(repo, new FakeUser(Reader));

        await service.ListPostsAsync(null, null, null, null, q: "  ltb  ", tab: "following");
        Assert.Equal(Reader, repo.LastQuery!.FollowedByUserId);
        Assert.Null(repo.LastQuery.AuthorUserId);
        Assert.Equal("ltb", repo.LastQuery.Search);

        await service.ListPostsAsync(null, null, null, null, q: null, tab: "mine");
        Assert.Equal(Reader, repo.LastQuery!.AuthorUserId);
        Assert.Null(repo.LastQuery.FollowedByUserId);
        Assert.Null(repo.LastQuery.Search);
    }

    [Theory]
    [InlineData("following")]
    [InlineData("mine")]
    public async Task Personal_feeds_need_a_signed_in_caller(string tab)
    {
        await Assert.ThrowsAsync<DomainRuleException>(() =>
            new ForumService(new FakeForum()).ListPostsAsync(null, null, null, null, q: null, tab: tab));
    }

    [Fact]
    public async Task An_unknown_tab_is_rejected()
    {
        await Assert.ThrowsAsync<DomainRuleException>(() =>
            new ForumService(new FakeForum(), new FakeUser(Reader))
                .ListPostsAsync(null, null, null, null, q: null, tab: "everything"));
    }

    [Fact]
    public async Task Marking_interested_twice_counts_once_and_clearing_converges()
    {
        var repo = new FakeForum(Row());
        var id = repo.Rows[0].Id;
        var service = new ForumService(repo, new FakeUser(Reader));

        await service.SetInterestedAsync(id, true);
        var again = await service.SetInterestedAsync(id, true);
        Assert.Equal(new ForumInterestResultDto(true, 1), again);

        var cleared = await service.SetInterestedAsync(id, false);
        var clearedAgain = await service.SetInterestedAsync(id, false);
        Assert.Equal(new ForumInterestResultDto(false, 0), cleared);
        Assert.Equal(cleared, clearedAgain);
    }

    [Fact]
    public async Task The_feed_marks_only_the_callers_own_interests()
    {
        var repo = new FakeForum(Row(seq: 2), Row(seq: 1));
        await new ForumService(repo, new FakeUser(Reader)).SetInterestedAsync(repo.Rows[0].Id, true);

        var mine = await new ForumService(repo, new FakeUser(Reader)).ListPostsAsync(null, "new", null, null);
        var theirs = await new ForumService(repo, new FakeUser(Author)).ListPostsAsync(null, "new", null, null);
        var signedOut = await new ForumService(repo).ListPostsAsync(null, "new", null, null);

        Assert.Equal([true, false], mine.Items.Select(p => p.IsInterested));
        Assert.All(theirs.Items, p => Assert.False(p.IsInterested));
        Assert.All(signedOut.Items, p => Assert.False(p.IsInterested));
    }

    [Fact]
    public async Task Marking_interested_requires_sign_in_and_an_existing_post()
    {
        var repo = new FakeForum(Row());
        await Assert.ThrowsAsync<DomainRuleException>(() =>
            new ForumService(repo).SetInterestedAsync(repo.Rows[0].Id, true));
        await Assert.ThrowsAsync<NotFoundException>(() =>
            new ForumService(repo, new FakeUser(Reader)).SetInterestedAsync(Guid.NewGuid(), true));
    }

    [Theory]
    [InlineData(ForumSort.Hot, 12345.6789012)]
    [InlineData(ForumSort.New, null)]
    public void Cursor_round_trips(ForumSort sort, double? score)
    {
        var cursor = new ForumCursor(score, 42);
        Assert.Equal(cursor, ForumCursor.Decode(cursor.Encode(), sort));
    }

    [Fact]
    public void A_cursor_from_the_other_sort_is_rejected_rather_than_misread()
    {
        var hot = new ForumCursor(1.5, 42).Encode();
        Assert.Throws<DomainRuleException>(() => ForumCursor.Decode(hot, ForumSort.New));
        Assert.Throws<DomainRuleException>(() => ForumCursor.Decode("not-a-cursor", ForumSort.Hot));
    }

    [Fact]
    public async Task Creating_a_post_requires_a_signed_in_user()
    {
        var service = new ForumService(new FakeForum());
        await Assert.ThrowsAsync<DomainRuleException>(() =>
            service.CreatePostAsync(new CreateForumPostDto("qa", null, "Hello", false)));
    }

    [Fact]
    public async Task Creating_a_post_stores_the_author_even_when_anonymous()
    {
        var repo = new FakeForum();
        var post = await new ForumService(repo, new FakeUser(Author))
            .CreatePostAsync(new CreateForumPostDto("qa", "   ", "  Where is LTB?  ", true));

        var stored = repo.Added.Single();
        Assert.Equal(Author, stored.AuthorUserId);
        Assert.True(stored.IsAnonymous);
        Assert.Null(stored.Title);
        Assert.Equal("Where is LTB?", stored.Content);
        Assert.True(post.IsMine);
    }

    [Theory]
    [InlineData("   ", null)]
    [InlineData("ok", "x")]
    public async Task Blank_bodies_and_overlong_titles_are_rejected_before_the_database(string content, string? titleChar)
    {
        var title = titleChar is null ? null : new string('x', ForumService.TitleMaxLength + 1);
        var repo = new FakeForum();
        var service = new ForumService(repo, new FakeUser(Author));

        await Assert.ThrowsAsync<DomainRuleException>(() =>
            service.CreatePostAsync(new CreateForumPostDto("qa", title, content, false)));
        Assert.Empty(repo.Added);
    }

    [Fact]
    public async Task Unknown_category_is_rejected()
    {
        var service = new ForumService(new FakeForum(), new FakeUser(Author));
        await Assert.ThrowsAsync<DomainRuleException>(() =>
            service.CreatePostAsync(new CreateForumPostDto("nope", null, "Hello", false)));
    }

    [Fact]
    public void Newer_post_outranks_older_post_with_equal_engagement()
    {
        var older = ForumRanking.HotScore(10, 2, DateTimeOffset.UtcNow.AddHours(-24));
        var newer = ForumRanking.HotScore(10, 2, DateTimeOffset.UtcNow);
        Assert.True(newer > older);
    }

    [Fact]
    public void Ten_times_the_engagement_is_worth_twelve_and_a_half_hours()
    {
        var now = DateTimeOffset.UtcNow;
        var popularOlder = ForumRanking.HotScore(100, 0, now.AddHours(-12));
        var quietNewer = ForumRanking.HotScore(10, 0, now);
        Assert.True(popularOlder > quietNewer);
    }

    private static ForumPostRow Row(bool anonymous = false, long seq = 1, string content = "Body") => new(
        Guid.NewGuid(), seq, Qa.Id, Qa.Name, Qa.Slug, "Title", content, anonymous,
        Author, "Real Name", null, UserRole.Member, 0, 0, seq, false, DateTimeOffset.UtcNow);

    private sealed class FakeForum(params ForumPostRow[] rows) : IForumRepository
    {
        public List<ForumPostRow> Rows { get; } = rows.ToList();
        public List<ForumPost> Added { get; } = [];
        public ForumPostQuery? LastQuery { get; private set; }

        public Task<IReadOnlyList<ForumCategory>> ListCategoriesAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<ForumCategory>>([Qa]);

        public Task<ForumCategory?> FindCategoryAsync(string slug, CancellationToken ct = default) =>
            Task.FromResult(slug == Qa.Slug ? Qa : null);

        public Task<IReadOnlyList<ForumPostRow>> ListPostsAsync(ForumPostQuery query, CancellationToken ct = default)
        {
            LastQuery = query;
            var rows = Rows.OrderByDescending(r => r.Seq)
                .Where(r => query.After is not { } after || r.Seq < after.Seq)
                .Take(query.Take)
                .ToList();
            return Task.FromResult<IReadOnlyList<ForumPostRow>>(rows);
        }

        public Task<ForumPostRow?> GetPostAsync(Guid id, CancellationToken ct = default) =>
            Task.FromResult(Rows.FirstOrDefault(r => r.Id == id));

        public HashSet<(Guid Post, Guid User)> Likes { get; } = [];

        public Task<bool> AddLikeAsync(Guid postId, Guid userId, DateTimeOffset createdAt, CancellationToken ct = default) =>
            Task.FromResult(Likes.Add((postId, userId)));

        public Task<bool> RemoveLikeAsync(Guid postId, Guid userId, CancellationToken ct = default) =>
            Task.FromResult(Likes.Remove((postId, userId)));

        public Task<int> RefreshEngagementAsync(Guid postId, CancellationToken ct = default) =>
            Task.FromResult(Likes.Count(l => l.Post == postId));

        public Task<IReadOnlySet<Guid>> ListLikedPostIdsAsync(
            Guid userId, IReadOnlyCollection<Guid> postIds, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlySet<Guid>>(
                Likes.Where(l => l.User == userId && postIds.Contains(l.Post)).Select(l => l.Post).ToHashSet());

        public Task AddPostAsync(ForumPost post, CancellationToken ct = default)
        {
            Added.Add(post);
            Rows.Add(new ForumPostRow(
                post.Id, Rows.Count + 1, Qa.Id, Qa.Name, Qa.Slug, post.Title, post.Content, post.IsAnonymous,
                post.AuthorUserId, "Real Name", null, UserRole.Member, 0, 0, post.HotScore, false, post.CreatedAt));
            return Task.CompletedTask;
        }
    }

    private sealed class FakeUser(Guid id, bool isAdmin = false, bool canPublish = false) : ICurrentUser
    {
        public Guid? UserId => id;
        public bool IsDemo => false;
        public bool IsAdmin => isAdmin;
        public bool CanPublishPublicEvents => canPublish || isAdmin;
    }
}
