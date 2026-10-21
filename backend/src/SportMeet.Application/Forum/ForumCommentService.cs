using SportMeet.Application.Common;
using SportMeet.Domain.Entities;
using SportMeet.Domain.Enums;

namespace SportMeet.Application.Forum;

/// <param name="Content">Empty when <paramref name="IsDeleted"/>.</param>
/// <param name="IsOp">Written by the post's author. Withheld wherever it would unmask
/// someone - see <see cref="ForumCommentService"/>.</param>
/// <param name="CanDelete">The caller may delete it: their own, or they are an admin.</param>
/// <param name="Replies">One level only: replies never carry replies of their own.</param>
public sealed record ForumCommentDto(
    Guid Id,
    Guid? ParentCommentId,
    string Content,
    bool IsDeleted,
    bool IsAnonymous,
    ForumAuthorDto? Author,
    bool IsMine,
    bool IsOp,
    bool CanDelete,
    DateTimeOffset CreatedAt,
    IReadOnlyList<ForumCommentDto> Replies);

public sealed record CreateForumCommentDto(string Content, Guid? ParentCommentId, bool IsAnonymous);

/// <summary>A comment with its author's display columns. Always the real author;
/// redaction happens in the service.</summary>
public sealed record ForumCommentRow(
    Guid Id,
    Guid PostId,
    Guid? ParentCommentId,
    string Content,
    bool IsAnonymous,
    Guid AuthorUserId,
    string AuthorName,
    string? AuthorPhotoUrl,
    UserRole AuthorRole,
    DateTimeOffset CreatedAt,
    DateTimeOffset? DeletedAt);

public interface IForumCommentRepository
{
    /// <summary>Every comment on the post, deleted ones included, oldest first.</summary>
    Task<IReadOnlyList<ForumCommentRow>> ListForPostAsync(Guid postId, CancellationToken ct = default);

    Task<ForumCommentRow?> GetAsync(Guid id, CancellationToken ct = default);

    Task AddAsync(ForumComment comment, CancellationToken ct = default);

    Task SoftDeleteAsync(Guid id, DateTimeOffset deletedAt, CancellationToken ct = default);
}

/// <summary>
/// Comments and one-level replies on forum posts.
///
/// The anonymity rules match posts: the author is stored, and only the writer and
/// admins see who wrote an anonymous comment. One extra rule is specific to
/// threads - the "OP" badge. On an anonymous post, an "OP" badge on a comment the
/// poster wrote under their own name would tell everyone who wrote the post, so the
/// badge is only shown when it reveals nothing: on a named post, or on a comment
/// that is itself anonymous ("Anonymous (OP)").
/// </summary>
public sealed class ForumCommentService(
    IForumCommentRepository comments,
    IForumRepository forum,
    ICurrentUser? currentUser = null)
{
    public const int ContentMaxLength = 5_000;

    public async Task<IReadOnlyList<ForumCommentDto>> ListAsync(Guid postId, CancellationToken ct = default)
    {
        var post = await forum.GetPostAsync(postId, ct) ?? throw new NotFoundException("Forum post", postId, postId);
        var rows = await comments.ListForPostAsync(postId, ct);

        var replies = rows
            .Where(r => r.ParentCommentId is not null && r.DeletedAt is null)
            .GroupBy(r => r.ParentCommentId!.Value)
            .ToDictionary(g => g.Key, g => g.OrderBy(r => r.CreatedAt).ToList());

        return rows
            .Where(r => r.ParentCommentId is null)
            // A deleted comment stays only as a placeholder its replies hang from.
            .Where(r => r.DeletedAt is null || replies.ContainsKey(r.Id))
            .OrderBy(r => r.CreatedAt)
            .Select(r => ToDto(r, post, replies.GetValueOrDefault(r.Id)?
                .Select(reply => ToDto(reply, post, null)).ToList()))
            .ToList();
    }

    public async Task<ForumCommentDto> CreateAsync(Guid postId, CreateForumCommentDto dto, CancellationToken ct = default)
    {
        var authorId = currentUser?.UserId
            ?? throw new DomainRuleException("You must be signed in to comment.");
        var content = dto.Content?.Trim() ?? "";
        if (content.Length == 0) throw new DomainRuleException("Write something before posting a comment.");
        if (content.Length > ContentMaxLength)
            throw new DomainRuleException($"Comments can be at most {ContentMaxLength:N0} characters.");

        var post = await forum.GetPostAsync(postId, ct) ?? throw new NotFoundException("Forum post", postId, postId);

        Guid? parentId = null;
        if (dto.ParentCommentId is { } requested)
        {
            var parent = await comments.GetAsync(requested, ct);
            if (parent is null || parent.PostId != postId || parent.DeletedAt is not null)
                throw new DomainRuleException("That comment is no longer available to reply to.");
            // Replying to a reply joins its thread rather than nesting deeper.
            parentId = parent.ParentCommentId ?? parent.Id;
        }

        var comment = new ForumComment
        {
            Id = Guid.NewGuid(),
            PostId = postId,
            AuthorUserId = authorId,
            ParentCommentId = parentId,
            Content = content,
            IsAnonymous = dto.IsAnonymous,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        await comments.AddAsync(comment, ct);
        await forum.RefreshEngagementAsync(postId, ct);

        var row = await comments.GetAsync(comment.Id, ct)
            ?? throw new InvalidOperationException("A comment that was just saved could not be read back.");
        return ToDto(row, post, []);
    }

    public async Task DeleteAsync(Guid commentId, CancellationToken ct = default)
    {
        var viewer = currentUser?.UserId
            ?? throw new DomainRuleException("You must be signed in to delete a comment.");
        var row = await comments.GetAsync(commentId, ct);
        if (row is null || row.DeletedAt is not null) throw new NotFoundException("Comment", commentId, commentId);
        if (row.AuthorUserId != viewer && currentUser?.IsAdmin != true)
            throw new DomainRuleException("You can only delete your own comments.");

        await comments.SoftDeleteAsync(commentId, DateTimeOffset.UtcNow, ct);
        await forum.RefreshEngagementAsync(row.PostId, ct);
    }

    private ForumCommentDto ToDto(ForumCommentRow row, ForumPostRow post, IReadOnlyList<ForumCommentDto>? replies)
    {
        var viewer = currentUser?.UserId;
        var isAdmin = currentUser?.IsAdmin == true;
        var isMine = viewer is { } v && v == row.AuthorUserId;
        var deleted = row.DeletedAt is not null;

        var showAuthor = !deleted && (!row.IsAnonymous || isMine || isAdmin);
        var wroteThePost = row.AuthorUserId == post.AuthorUserId;
        var isOp = !deleted && wroteThePost && (!post.IsAnonymous || row.IsAnonymous || isAdmin);

        return new ForumCommentDto(
            row.Id,
            row.ParentCommentId,
            deleted ? "" : row.Content,
            deleted,
            row.IsAnonymous,
            showAuthor ? new ForumAuthorDto(row.AuthorUserId, row.AuthorName, row.AuthorPhotoUrl, row.AuthorRole) : null,
            isMine && !deleted,
            isOp,
            !deleted && (isMine || isAdmin),
            row.CreatedAt,
            replies ?? []);
    }
}
