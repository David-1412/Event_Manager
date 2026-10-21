using System.Text.Json;
using SportMeet.Application.Common;
using SportMeet.Application.Forum;
using SportMeet.Domain.Entities;
using SportMeet.Domain.Enums;

namespace SportMeet.Import.Tests;

/// <summary>
/// Comment rules: one level of replies, soft deletes, and the anonymity rules -
/// including the one specific to threads, that an "OP" badge must never unmask the
/// writer of an anonymous post.
/// </summary>
public sealed class ForumCommentServiceTests
{
    private static readonly Guid Poster = Guid.Parse("00000000-0000-0000-0000-0000000000a1");
    private static readonly Guid Reader = Guid.Parse("00000000-0000-0000-0000-0000000000b2");
    private static readonly Guid PostId = Guid.Parse("00000000-0000-0000-0000-0000000000c3");
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task Replies_nest_one_level_under_their_comment()
    {
        var (service, repo) = Build(Reader);
        var top = await service.CreateAsync(PostId, new("First", null, false));
        var reply = await service.CreateAsync(PostId, new("Reply", top.Id, false));
        // Replying to a reply joins the same thread instead of nesting deeper.
        var replyToReply = await service.CreateAsync(PostId, new("Reply to reply", reply.Id, false));

        Assert.Equal(top.Id, replyToReply.ParentCommentId);
        var thread = Assert.Single(await service.ListAsync(PostId));
        Assert.Equal(["Reply", "Reply to reply"], thread.Replies.Select(r => r.Content));
        Assert.All(thread.Replies, r => Assert.Empty(r.Replies));
        Assert.Equal(3, repo.Refreshes);
    }

    [Fact]
    public async Task Anonymous_comment_hides_its_author_from_other_readers()
    {
        var (asPoster, repo) = Build(Poster);
        await asPoster.CreateAsync(PostId, new("Secret", null, true));

        var asReader = new ForumCommentService(repo, repo.Forum, new FakeUser(Reader));
        var comment = Assert.Single(await asReader.ListAsync(PostId));
        Assert.Null(comment.Author);
        Assert.DoesNotContain(Poster.ToString(), JsonSerializer.Serialize(comment, Json));
    }

    [Fact]
    public async Task OP_badge_is_withheld_where_it_would_unmask_an_anonymous_poster()
    {
        var (asPoster, repo) = Build(Poster, postIsAnonymous: true);
        await asPoster.CreateAsync(PostId, new("Named reply from the poster", null, false));
        await asPoster.CreateAsync(PostId, new("Anonymous reply from the poster", null, true));

        var asReader = new ForumCommentService(repo, repo.Forum, new FakeUser(Reader));
        var comments = await asReader.ListAsync(PostId);

        var named = comments.Single(c => !c.IsAnonymous);
        Assert.False(named.IsOp);                     // would reveal who wrote the post
        var anonymous = comments.Single(c => c.IsAnonymous);
        Assert.True(anonymous.IsOp);                  // "Anonymous (OP)" reveals nothing
        Assert.Null(anonymous.Author);
    }

    [Fact]
    public async Task OP_badge_shows_on_a_named_post()
    {
        var (asPoster, repo) = Build(Poster, postIsAnonymous: false);
        await asPoster.CreateAsync(PostId, new("Thanks all", null, false));
        var comment = Assert.Single(await new ForumCommentService(repo, repo.Forum).ListAsync(PostId));
        Assert.True(comment.IsOp);
    }

    [Fact]
    public async Task Deleted_comment_stays_as_a_placeholder_only_while_it_has_replies()
    {
        var (service, repo) = Build(Reader);
        var withReplies = await service.CreateAsync(PostId, new("Parent", null, false));
        await service.CreateAsync(PostId, new("Child", withReplies.Id, false));
        var alone = await service.CreateAsync(PostId, new("Alone", null, false));

        await service.DeleteAsync(withReplies.Id);
        await service.DeleteAsync(alone.Id);

        var placeholder = Assert.Single(await service.ListAsync(PostId));
        Assert.True(placeholder.IsDeleted);
        Assert.Equal("", placeholder.Content);
        Assert.Null(placeholder.Author);
        Assert.Single(placeholder.Replies);
    }

    [Fact]
    public async Task Only_the_author_or_an_admin_can_delete()
    {
        var (asReader, repo) = Build(Reader);
        var comment = await asReader.CreateAsync(PostId, new("Mine", null, false));

        var stranger = new ForumCommentService(repo, repo.Forum, new FakeUser(Poster));
        await Assert.ThrowsAsync<DomainRuleException>(() => stranger.DeleteAsync(comment.Id));

        var admin = new ForumCommentService(repo, repo.Forum, new FakeUser(Poster, isAdmin: true));
        await admin.DeleteAsync(comment.Id);
        await Assert.ThrowsAsync<NotFoundException>(() => admin.DeleteAsync(comment.Id));
    }

    [Theory]
    [InlineData("   ")]
    [InlineData(null)]
    public async Task Blank_comments_are_rejected(string? content)
    {
        var (service, repo) = Build(Reader);
        await Assert.ThrowsAsync<DomainRuleException>(() => service.CreateAsync(PostId, new(content!, null, false)));
        Assert.Empty(repo.Comments);
    }

    [Fact]
    public async Task Commenting_needs_a_signed_in_user_and_a_live_post()
    {
        var (_, repo) = Build(Reader);
        await Assert.ThrowsAsync<DomainRuleException>(() =>
            new ForumCommentService(repo, repo.Forum).CreateAsync(PostId, new("Hi", null, false)));
        await Assert.ThrowsAsync<NotFoundException>(() =>
            new ForumCommentService(repo, repo.Forum, new FakeUser(Reader)).CreateAsync(Guid.NewGuid(), new("Hi", null, false)));
    }

    private static (ForumCommentService Service, FakeComments Repo) Build(Guid viewer, bool postIsAnonymous = false)
    {
        var repo = new FakeComments(postIsAnonymous);
        return (new ForumCommentService(repo, repo.Forum, new FakeUser(viewer)), repo);
    }

    private sealed class FakeComments(bool postIsAnonymous) : IForumCommentRepository
    {
        public List<ForumComment> Comments { get; } = [];
        public int Refreshes => Forum.Refreshes;
        public FakePosts Forum { get; } = new(postIsAnonymous);

        public Task<IReadOnlyList<ForumCommentRow>> ListForPostAsync(Guid postId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<ForumCommentRow>>(
                Comments.Where(c => c.PostId == postId).OrderBy(c => c.CreatedAt).Select(Row).ToList());

        public Task<ForumCommentRow?> GetAsync(Guid id, CancellationToken ct = default) =>
            Task.FromResult(Comments.Where(c => c.Id == id).Select(Row).FirstOrDefault());

        public Task AddAsync(ForumComment comment, CancellationToken ct = default)
        {
            // Strictly increasing timestamps so ordering is deterministic in a fast test.
            comment.CreatedAt = DateTimeOffset.UtcNow.AddTicks(Comments.Count);
            Comments.Add(comment);
            return Task.CompletedTask;
        }

        public Task SoftDeleteAsync(Guid id, DateTimeOffset deletedAt, CancellationToken ct = default)
        {
            Comments.Single(c => c.Id == id).DeletedAt = deletedAt;
            return Task.CompletedTask;
        }

        private static ForumCommentRow Row(ForumComment c) => new(
            c.Id, c.PostId, c.ParentCommentId, c.Content, c.IsAnonymous,
            c.AuthorUserId, c.AuthorUserId == Poster ? "Poster Name" : "Reader Name", null, UserRole.Member,
            c.CreatedAt, c.DeletedAt);
    }

    private sealed class FakePosts(bool anonymous) : IForumRepository
    {
        public int Refreshes { get; private set; }

        public Task<ForumPostRow?> GetPostAsync(Guid id, CancellationToken ct = default) =>
            Task.FromResult(id == PostId
                ? new ForumPostRow(PostId, 1, 1, "Q&A / Help", "qa", "Title", "Body", anonymous,
                    Poster, "Poster Name", null, UserRole.Member, 0, 0, 1, false, DateTimeOffset.UtcNow)
                : null);

        public Task<int> RefreshEngagementAsync(Guid postId, CancellationToken ct = default)
        {
            Refreshes++;
            return Task.FromResult(0);
        }

        public Task<IReadOnlyList<ForumCategory>> ListCategoriesAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public Task<ForumCategory?> FindCategoryAsync(string slug, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<ForumPostRow>> ListPostsAsync(ForumPostQuery query, CancellationToken ct = default) => throw new NotSupportedException();
        public Task AddPostAsync(ForumPost post, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<bool> AddLikeAsync(Guid postId, Guid userId, DateTimeOffset createdAt, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<bool> RemoveLikeAsync(Guid postId, Guid userId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlySet<Guid>> ListLikedPostIdsAsync(Guid userId, IReadOnlyCollection<Guid> postIds, CancellationToken ct = default) => throw new NotSupportedException();
    }

    private sealed class FakeUser(Guid id, bool isAdmin = false) : ICurrentUser
    {
        public Guid? UserId => id;
        public bool IsDemo => false;
        public bool IsAdmin => isAdmin;
        public bool CanPublishPublicEvents => isAdmin;
    }
}
