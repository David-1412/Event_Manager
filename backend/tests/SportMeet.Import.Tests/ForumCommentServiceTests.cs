using System.Text.Json;
using SportMeet.Application.Common;
using SportMeet.Application.Forum;
using SportMeet.Domain.Entities;
using SportMeet.Domain.Enums;

namespace SportMeet.Import.Tests;

/// <summary>
/// Comment rules: one level of replies, edits, deletes (soft while replies hang off a
/// comment, hard otherwise), and the anonymity rules -
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
        await Assert.ThrowsAsync<ForbiddenException>(() => stranger.DeleteAsync(comment.Id));

        var admin = new ForumCommentService(repo, repo.Forum, new FakeUser(Poster, isAdmin: true));
        await admin.DeleteAsync(comment.Id);
        await Assert.ThrowsAsync<NotFoundException>(() => admin.DeleteAsync(comment.Id));
    }

    [Fact]
    public async Task Deleting_a_comment_without_replies_removes_its_row()
    {
        var (service, repo) = Build(Reader);
        var alone = await service.CreateAsync(PostId, new("Alone", null, false));

        await service.DeleteAsync(alone.Id);

        Assert.Empty(repo.Comments);
    }

    [Fact]
    public async Task Deleting_a_comment_with_replies_keeps_its_row_as_a_soft_delete()
    {
        var (service, repo) = Build(Reader);
        var parent = await service.CreateAsync(PostId, new("Parent", null, false));
        await service.CreateAsync(PostId, new("Child", parent.Id, false));

        await service.DeleteAsync(parent.Id);

        var kept = repo.Comments.Single(c => c.Id == parent.Id);
        Assert.NotNull(kept.DeletedAt);
        Assert.Equal("Parent", kept.Content); // stored, but never sent
    }

    [Fact]
    public async Task Deleting_the_last_reply_under_a_deleted_comment_removes_the_placeholder_too()
    {
        var (service, repo) = Build(Reader);
        var parent = await service.CreateAsync(PostId, new("Parent", null, false));
        var child = await service.CreateAsync(PostId, new("Child", parent.Id, false));
        await service.DeleteAsync(parent.Id);

        await service.DeleteAsync(child.Id);

        Assert.Empty(repo.Comments);
        Assert.Empty(await service.ListAsync(PostId));
    }

    [Fact]
    public async Task Deleting_the_last_comment_under_a_deleted_post_removes_the_post()
    {
        var (service, repo) = Build(Reader);
        var first = await service.CreateAsync(PostId, new("One", null, false));
        var second = await service.CreateAsync(PostId, new("Two", null, false));
        repo.Forum.PostDeleted = true;

        await service.DeleteAsync(first.Id);
        Assert.False(repo.Forum.PostRemoved);   // "Two" still needs the thread
        await service.DeleteAsync(second.Id);
        Assert.True(repo.Forum.PostRemoved);
    }

    [Fact]
    public async Task The_author_can_edit_a_comment_and_it_is_marked_edited()
    {
        var (service, repo) = Build(Reader);
        var comment = await service.CreateAsync(PostId, new("Frist", null, false));
        Assert.Null(comment.EditedAt);
        Assert.True(comment.CanEdit);

        var edited = await service.UpdateAsync(comment.Id, new("  First  "));

        Assert.Equal("First", edited.Content);
        Assert.NotNull(edited.EditedAt);
        Assert.NotNull(Assert.Single(await service.ListAsync(PostId)).EditedAt);
        Assert.Equal("First", repo.Comments.Single().Content);
    }

    [Fact]
    public async Task Only_the_author_can_edit_a_comment_not_even_an_admin()
    {
        var (asReader, repo) = Build(Reader);
        var comment = await asReader.CreateAsync(PostId, new("Mine", null, false));

        var stranger = new ForumCommentService(repo, repo.Forum, new FakeUser(Poster));
        await Assert.ThrowsAsync<ForbiddenException>(() => stranger.UpdateAsync(comment.Id, new("Hacked")));
        var admin = new ForumCommentService(repo, repo.Forum, new FakeUser(Poster, isAdmin: true));
        await Assert.ThrowsAsync<ForbiddenException>(() => admin.UpdateAsync(comment.Id, new("Hacked")));

        var listed = Assert.Single(await stranger.ListAsync(PostId));
        Assert.False(listed.CanEdit);
        Assert.Equal("Mine", listed.Content);
    }

    [Fact]
    public async Task A_deleted_comment_cannot_be_edited_and_a_blank_edit_is_rejected()
    {
        var (service, repo) = Build(Reader);
        var parent = await service.CreateAsync(PostId, new("Parent", null, false));
        await service.CreateAsync(PostId, new("Child", parent.Id, false));
        var other = await service.CreateAsync(PostId, new("Other", null, false));

        await Assert.ThrowsAsync<DomainRuleException>(() => service.UpdateAsync(other.Id, new("   ")));
        await service.DeleteAsync(parent.Id);
        await Assert.ThrowsAsync<NotFoundException>(() => service.UpdateAsync(parent.Id, new("Back")));
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

        public Task HardDeleteAsync(Guid id, CancellationToken ct = default)
        {
            // ON DELETE CASCADE on parent_comment_id.
            Comments.RemoveAll(c => c.Id == id || c.ParentCommentId == id);
            return Task.CompletedTask;
        }

        public Task UpdateContentAsync(Guid id, string content, DateTimeOffset editedAt, CancellationToken ct = default)
        {
            var comment = Comments.Single(c => c.Id == id && c.DeletedAt == null);
            comment.Content = content;
            comment.EditedAt = editedAt;
            return Task.CompletedTask;
        }

        private static ForumCommentRow Row(ForumComment c) => new(
            c.Id, c.PostId, c.ParentCommentId, c.Content, c.IsAnonymous,
            c.AuthorUserId, c.AuthorUserId == Poster ? "Poster Name" : "Reader Name", null, UserRole.Member,
            c.CreatedAt, c.DeletedAt, c.EditedAt);
    }

    private sealed class FakePosts(bool anonymous) : IForumRepository
    {
        public int Refreshes { get; private set; }

        /// <summary>The post was soft-deleted (kept for its comments).</summary>
        public bool PostDeleted { get; set; }
        public bool PostRemoved { get; private set; }

        public Task<ForumPostRow?> GetPostAsync(Guid id, CancellationToken ct = default) =>
            Task.FromResult(PostDeleted ? null : Post(id));

        public Task<ForumPostRow?> GetPostIncludingDeletedAsync(Guid id, CancellationToken ct = default) =>
            Task.FromResult(PostRemoved ? null : Post(id) is { } row && PostDeleted
                ? row with { DeletedAt = DateTimeOffset.UtcNow }
                : Post(id));

        public Task HardDeletePostAsync(Guid id, CancellationToken ct = default)
        {
            PostRemoved = true;
            return Task.CompletedTask;
        }

        private ForumPostRow? Post(Guid id) => id == PostId && !PostRemoved
            ? new ForumPostRow(PostId, 1, 1, "Q&A / Help", "qa", "Title", "Body", anonymous,
                Poster, "Poster Name", null, UserRole.Member, 0, 0, 1, false, DateTimeOffset.UtcNow)
            : null;

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
