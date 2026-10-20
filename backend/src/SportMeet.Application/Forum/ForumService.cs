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

    public async Task<ForumPostDto> GetPostAsync(Guid id, CancellationToken ct = default)
    {
        var row = await forum.GetPostAsync(id, ct) ?? throw new NotFoundException("Forum post", id, id);
        return ToDto(row, excerpt: false, await LikedAsync([row.Id], ct));
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

    private async Task<IReadOnlySet<Guid>> LikedAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct) =>
        currentUser?.UserId is { } viewer && ids.Count > 0
            ? await forum.ListLikedPostIdsAsync(viewer, ids, ct)
            : new HashSet<Guid>();

    public async Task<ForumPostDto> CreatePostAsync(CreateForumPostDto dto, CancellationToken ct = default)
    {
        var authorId = currentUser?.UserId
            ?? throw new DomainRuleException("You must be signed in to post.");

        // Checked here rather than by a FluentValidation validator: the API's
        // ValidationFilter only runs CreateEventDto's. The migration's CHECK is the
        // backstop, but it would surface as a 500 instead of a readable 422.
        if (string.IsNullOrWhiteSpace(dto.Content))
            throw new DomainRuleException("Write something before posting.");
        if (dto.Content.Trim().Length > ContentMaxLength)
            throw new DomainRuleException($"Posts can be at most {ContentMaxLength:N0} characters.");
        if (dto.Title?.Trim().Length > TitleMaxLength)
            throw new DomainRuleException($"Titles can be at most {TitleMaxLength} characters.");

        var category = await forum.FindCategoryAsync(dto.Category, ct)
            ?? throw new DomainRuleException("Choose one of the forum's categories.");

        var now = DateTimeOffset.UtcNow;
        var post = new ForumPost
        {
            Id = Guid.NewGuid(),
            AuthorUserId = authorId,
            CategoryId = category.Id,
            // An all-whitespace title is no title: the card would otherwise render an
            // empty headline above the body.
            Title = string.IsNullOrWhiteSpace(dto.Title) ? null : dto.Title.Trim(),
            Content = dto.Content.Trim(),
            IsAnonymous = dto.IsAnonymous,
            HotScore = ForumRanking.HotScore(0, 0, now),
            CreatedAt = now,
        };
        await forum.AddPostAsync(post, ct);

        return await GetPostAsync(post.Id, ct);
    }

    private ForumPostDto ToDto(ForumPostRow row, bool excerpt, IReadOnlySet<Guid> liked)
    {
        var viewerId = currentUser?.UserId;
        var isMine = viewerId is { } viewer && viewer == row.AuthorUserId;
        var showAuthor = !row.IsAnonymous || isMine || currentUser?.IsAdmin == true;

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
            row.CreatedAt);
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
