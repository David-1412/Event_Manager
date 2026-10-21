using SportMeet.Application.Common;
using SportMeet.Domain.Entities;

namespace SportMeet.Application.Forum;

/// <summary>
/// The campus forum: categories, feeds, posts.
///
/// This is the anonymity boundary. Rows from <see cref="IForumRepository"/> always
/// carry the real author, and <see cref="ToDto"/> is the single place a row becomes
/// something a client can see - so "an anonymous post never names its author" is one
/// rule in one method rather than a filter every query has to remember. Two callers
/// still see the author of an anonymous post: the author (as <c>IsMine</c>, so they
/// can find their own post) and admins, who handle abuse reports. The Moderator role
/// is deliberately excluded - in this app it is the "Creator" tier for clubs and
/// hosts, not a moderation role, and unmasking posters is not a Creator privilege.
/// </summary>
public sealed class ForumService(IForumRepository forum, ICurrentUser? currentUser = null)
{
    public const int DefaultPageSize = 20;
    public const int MaxPageSize = 50;

    /// <summary>Feed cards show this much of the body; the post page shows all of it.</summary>
    public const int ExcerptLength = 400;

    // Mirror the column and CHECK limits in the Add_Forum migration.
    public const int TitleMaxLength = 150;
    public const int ContentMaxLength = 10_000;

    public async Task<IReadOnlyList<ForumCategoryDto>> GetCategoriesAsync(CancellationToken ct = default) =>
        (await forum.ListCategoriesAsync(ct))
            .OrderBy(c => c.SortOrder)
            .Select(c => new ForumCategoryDto(c.Id, c.Name, c.Slug))
            .ToList();

    public const int SearchMaxLength = 100;

    public async Task<ForumPostPage> ListPostsAsync(
        string? category, string? sort, string? cursor, int? limit, CancellationToken ct = default) =>
        await ListPostsAsync(category, sort, cursor, limit, q: null, tab: null, ct);

    /// <param name="q">Free text matched against titles and bodies.</param>
    /// <param name="tab">"all" (default), "following" or "mine". The last two need a
    /// signed-in caller - they are views of the caller's own graph.</param>
    public async Task<ForumPostPage> ListPostsAsync(
        string? category, string? sort, string? cursor, int? limit, string? q, string? tab,
        CancellationToken ct = default)
    {
        var order = ParseSort(sort);
        var feed = ParseTab(tab);
        var viewer = currentUser?.UserId;
        if (feed != ForumTab.All && viewer is null)
            throw new DomainRuleException("Sign in to see this feed.");
        var search = string.IsNullOrWhiteSpace(q) ? null : q.Trim();
        if (search?.Length > SearchMaxLength) search = search[..SearchMaxLength];
        var take = Math.Clamp(limit ?? DefaultPageSize, 1, MaxPageSize);

        int? categoryId = null;
        if (!string.IsNullOrWhiteSpace(category) && category != "all")
        {
            categoryId = (await forum.FindCategoryAsync(category, ct))?.Id
                ?? throw new NotFoundException("Forum category", category);
        }

        // One extra row answers "is there another page" without a COUNT.
        var rows = await forum.ListPostsAsync(
            new ForumPostQuery(
                categoryId, order, ForumCursor.Decode(cursor, order), take + 1,
                Search: search,
                AuthorUserId: feed == ForumTab.Mine ? viewer : null,
                FollowedByUserId: feed == ForumTab.Following ? viewer : null), ct);

        var page = rows.Take(take).ToList();
        var next = rows.Count > take
            ? new ForumCursor(order == ForumSort.Hot ? page[^1].HotScore : null, page[^1].Seq).Encode()
            : null;

        var liked = await LikedAsync(page.Select(r => r.Id).ToList(), ct);
        return new ForumPostPage(page.Select(r => ToDto(r, excerpt: true, liked)).ToList(), next);
    }

    /// <summary>One post in full. A post deleted while it had comments still opens, as
    /// a placeholder, so its thread stays readable.</summary>
    public async Task<ForumPostDto> GetPostAsync(Guid id, CancellationToken ct = default)
    {
        var row = await forum.GetPostIncludingDeletedAsync(id, ct) ?? throw new NotFoundException("Forum post", id, id);
        var liked = row.DeletedAt is null ? await LikedAsync([row.Id], ct) : new HashSet<Guid>();
        return ToDto(row, excerpt: false, liked);
    }

    /// <summary>
    /// Edit a post's title and body. Author only - an admin can remove a post but not
    /// put words in someone's mouth. Category and anonymity stay as posted: changing
    /// either after people have replied would change what they replied to.
    /// </summary>
    public async Task<ForumPostDto> UpdatePostAsync(Guid id, UpdateForumPostDto dto, CancellationToken ct = default)
    {
        var viewer = currentUser?.UserId
            ?? throw new DomainRuleException("You must be signed in to edit a post.");
        var row = await forum.GetPostAsync(id, ct) ?? throw new NotFoundException("Forum post", id, id);
        if (row.AuthorUserId != viewer) throw new ForbiddenException("You can only edit your own posts.");

        var (title, content) = ValidatePost(dto.Title, dto.Content);
        await forum.UpdatePostAsync(id, title, content, DateTimeOffset.UtcNow, ct);
        return await GetPostAsync(id, ct);
    }

    /// <summary>
    /// Delete a post: its author, or an admin. A post with comments is soft-deleted, so
    /// the thread stays readable under a "[deleted]" placeholder; one with no comments
    /// has nothing to keep and its row is removed.
    /// </summary>
    public async Task DeletePostAsync(Guid id, CancellationToken ct = default)
    {
        var viewer = currentUser?.UserId
            ?? throw new DomainRuleException("You must be signed in to delete a post.");
        var row = await forum.GetPostAsync(id, ct) ?? throw new NotFoundException("Forum post", id, id);
        if (row.AuthorUserId != viewer && currentUser?.IsAdmin != true)
            throw new ForbiddenException("You can only delete your own posts.");

        if (row.CommentCount > 0) await forum.SoftDeletePostAsync(id, DateTimeOffset.UtcNow, ct);
        else await forum.HardDeletePostAsync(id, ct);
    }

    /// <summary>
    /// Mark or clear "Interested" on a post. Idempotent in both directions - a retried
    /// tap converges on the requested state - and the count returned is recounted from
    /// the likes table, so the number on screen is the stored one, not a guess.
    /// </summary>
    public async Task<ForumInterestResultDto> SetInterestedAsync(
        Guid id, bool interested, CancellationToken ct = default)
    {
        var viewer = currentUser?.UserId
            ?? throw new DomainRuleException("You must be signed in to mark a post interested.");
        _ = await forum.GetPostAsync(id, ct) ?? throw new NotFoundException("Forum post", id, id);

        if (interested) await forum.AddLikeAsync(id, viewer, DateTimeOffset.UtcNow, ct);
        else await forum.RemoveLikeAsync(id, viewer, ct);

        var count = await forum.RefreshEngagementAsync(id, ct);
        return new ForumInterestResultDto(interested, count);
    }

    /// <summary>
    /// Record that the post page was opened, and - on a signed-in reader's first ever
    /// view of someone else's post - mark them Interested.
    ///
    /// Views and Interested are kept apart on purpose: the view total only ever goes
    /// up (it is the Campus Pulse score), while Interested can be turned off at any
    /// time and only moves the post's interested count. "First ever" rather than "not
    /// interested yet" is what makes turning it off stick when they come back.
    /// </summary>
    public async Task<ForumViewResultDto> RecordViewAsync(Guid id, CancellationToken ct = default)
    {
        var row = await forum.GetPostAsync(id, ct) ?? throw new NotFoundException("Forum post", id, id);
        var viewer = currentUser?.UserId;
        var now = DateTimeOffset.UtcNow;

        var (viewCount, isNew) = await forum.RecordViewAsync(id, viewer, now, ct);
        if (viewer is not { } me) return new ForumViewResultDto(viewCount, false, row.LikeCount);

        var interested = (await forum.ListLikedPostIdsAsync(me, [id], ct)).Contains(id);
        var likeCount = row.LikeCount;
        if (!interested && isNew && row.AuthorUserId != me && await forum.CountViewsByUserAsync(id, me, ct) == 1)
        {
            await forum.AddLikeAsync(id, me, now, ct);
            likeCount = await forum.RefreshEngagementAsync(id, ct);
            interested = true;
        }

        return new ForumViewResultDto(viewCount, interested, likeCount);
    }

    private async Task<IReadOnlySet<Guid>> LikedAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct) =>
        currentUser?.UserId is { } viewer && ids.Count > 0
            ? await forum.ListLikedPostIdsAsync(viewer, ids, ct)
            : new HashSet<Guid>();

    public async Task<ForumPostDto> CreatePostAsync(CreateForumPostDto dto, CancellationToken ct = default)
    {
        var authorId = currentUser?.UserId
            ?? throw new DomainRuleException("You must be signed in to post.");

        var (title, content) = ValidatePost(dto.Title, dto.Content);

        var category = await forum.FindCategoryAsync(dto.Category, ct)
            ?? throw new DomainRuleException("Choose one of the forum's categories.");

        var now = DateTimeOffset.UtcNow;
        var post = new ForumPost
        {
            Id = Guid.NewGuid(),
            AuthorUserId = authorId,
            CategoryId = category.Id,
            Title = title,
            Content = content,
            IsAnonymous = dto.IsAnonymous,
            HotScore = ForumRanking.HotScore(0, 0, now),
            CreatedAt = now,
        };
        await forum.AddPostAsync(post, ct);

        return await GetPostAsync(post.Id, ct);
    }

    /// <summary>The trimmed title (null when blank) and body, or a 422 naming the
    /// problem. Checked here rather than by a FluentValidation validator: the API's
    /// ValidationFilter only runs CreateEventDto's. The migration's CHECK is the
    /// backstop, but it would surface as a 500 instead of a readable 422.</summary>
    private static (string? Title, string Content) ValidatePost(string? title, string? content)
    {
        if (string.IsNullOrWhiteSpace(content))
            throw new DomainRuleException("Write something before posting.");
        if (content.Trim().Length > ContentMaxLength)
            throw new DomainRuleException($"Posts can be at most {ContentMaxLength:N0} characters.");
        if (title?.Trim().Length > TitleMaxLength)
            throw new DomainRuleException($"Titles can be at most {TitleMaxLength} characters.");

        // An all-whitespace title is no title: the card would otherwise render an
        // empty headline above the body.
        return (string.IsNullOrWhiteSpace(title) ? null : title.Trim(), content.Trim());
    }

    private ForumPostDto ToDto(ForumPostRow row, bool excerpt, IReadOnlySet<Guid> liked)
    {
        var viewerId = currentUser?.UserId;
        var isAdmin = currentUser?.IsAdmin == true;
        var isMine = viewerId is { } viewer && viewer == row.AuthorUserId;

        if (row.DeletedAt is not null)
        {
            // Kept only so its comments stay readable: nothing of the post itself -
            // not its words, not who wrote it - is shown to anyone.
            return new ForumPostDto(
                row.Id,
                new ForumCategoryDto(row.CategoryId, row.CategoryName, row.CategorySlug),
                null, "", false, row.IsAnonymous, null, false, false,
                row.LikeCount, row.CommentCount, false, row.CreatedAt, row.ViewCount,
                EditedAt: null, IsDeleted: true, CanEdit: false, CanDelete: false);
        }

        var showAuthor = !row.IsAnonymous || isMine || isAdmin;

        var truncated = excerpt && row.Content.Length > ExcerptLength;
        var content = truncated ? row.Content[..ExcerptLength].TrimEnd() : row.Content;

        return new ForumPostDto(
            row.Id,
            new ForumCategoryDto(row.CategoryId, row.CategoryName, row.CategorySlug),
            row.Title,
            content,
            truncated,
            row.IsAnonymous,
            showAuthor
                ? new ForumAuthorDto(row.AuthorUserId, row.AuthorName, row.AuthorPhotoUrl, row.AuthorRole)
                : null,
            isMine,
            liked.Contains(row.Id),
            row.LikeCount,
            row.CommentCount,
            row.IsPinned,
            row.CreatedAt,
            row.ViewCount,
            row.EditedAt,
            IsDeleted: false,
            CanEdit: isMine,
            CanDelete: isMine || isAdmin);
    }

    private static ForumTab ParseTab(string? tab) => tab?.ToLowerInvariant() switch
    {
        null or "" or "all" => ForumTab.All,
        "following" => ForumTab.Following,
        "mine" => ForumTab.Mine,
        _ => throw new DomainRuleException("Show \"all\", \"following\" or \"mine\"."),
    };

    private static ForumSort ParseSort(string? sort) => sort?.ToLowerInvariant() switch
    {
        null or "" or "hot" => ForumSort.Hot,
        "new" => ForumSort.New,
        _ => throw new DomainRuleException("Sort by \"hot\" or \"new\"."),
    };
}
