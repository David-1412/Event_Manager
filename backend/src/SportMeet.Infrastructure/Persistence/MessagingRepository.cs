using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using SportMeet.Application.Forum;
using SportMeet.Application.Messaging;
using SportMeet.Domain.Entities;

namespace SportMeet.Infrastructure.Persistence;

public sealed class ForumCommentRepository(AppDbContext db) : IForumCommentRepository
{
    public async Task<IReadOnlyList<ForumCommentRow>> ListForPostAsync(Guid postId, CancellationToken ct = default) =>
        await db.ForumComments.AsNoTracking()
            .Where(c => c.PostId == postId)
            .OrderBy(c => c.CreatedAt)
            .Select(ToRow)
            .ToListAsync(ct);

    public Task<ForumCommentRow?> GetAsync(Guid id, CancellationToken ct = default) =>
        db.ForumComments.AsNoTracking().Where(c => c.Id == id).Select(ToRow).FirstOrDefaultAsync(ct);

    public async Task AddAsync(ForumComment comment, CancellationToken ct = default)
    {
        db.ForumComments.Add(comment);
        await db.SaveChangesAsync(ct);
    }

    public Task SoftDeleteAsync(Guid id, DateTimeOffset deletedAt, CancellationToken ct = default) =>
        db.ForumComments.Where(c => c.Id == id && c.DeletedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.DeletedAt, deletedAt), ct);

    private static readonly Expression<Func<ForumComment, ForumCommentRow>> ToRow = c => new ForumCommentRow(
        c.Id,
        c.PostId,
        c.ParentCommentId,
        c.Content,
        c.IsAnonymous,
        c.AuthorUserId,
        c.Author.Name,
        c.Author.PhotoUrl,
        c.Author.Role,
        c.CreatedAt,
        c.DeletedAt);
}

public sealed class MessagingRepository(AppDbContext db) : IMessagingRepository
{
    public async Task<IReadOnlyList<ConversationRow>> ListForUserAsync(Guid userId, CancellationToken ct = default) =>
        await Rows(db.Conversations.Where(c => c.UserAId == userId || c.UserBId == userId)).ToListAsync(ct);

    public Task<ConversationRow?> GetForUserAsync(Guid conversationId, Guid userId, CancellationToken ct = default) =>
        Rows(db.Conversations.Where(c => c.Id == conversationId && (c.UserAId == userId || c.UserBId == userId)))
            .FirstOrDefaultAsync(ct);

    public async Task<Guid> GetOrCreateAsync(Guid userA, Guid userB, DateTimeOffset now, CancellationToken ct = default)
    {
        var existing = await FindPairAsync(userA, userB, ct);
        if (existing is { } id) return id;

        var conversation = new Conversation { Id = Guid.NewGuid(), UserAId = userA, UserBId = userB, CreatedAt = now };
        db.Conversations.Add(conversation);
        try
        {
            await db.SaveChangesAsync(ct);
            return conversation.Id;
        }
        catch (DbUpdateException ex) when (ex.InnerException is Npgsql.PostgresException { SqlState: "23505" })
        {
            // Both people opened the thread at the same moment: the unique pair key let
            // one insert through, and this caller joins that row.
            db.Entry(conversation).State = EntityState.Detached;
            return await FindPairAsync(userA, userB, ct)
                ?? throw new InvalidOperationException("The conversation that won the race could not be found.");
        }
    }

    public Task<bool> UserExistsAsync(Guid userId, CancellationToken ct = default) =>
        db.Users.AsNoTracking().AnyAsync(u => u.Id == userId, ct);

    public async Task<IReadOnlyList<(DirectMessage Message, long Seq)>> ListMessagesAsync(
        Guid conversationId, Guid userId, long? before, int take, CancellationToken ct = default)
    {
        // Scoped by the caller here as well as in the service: the join to the
        // conversation means a stranger's id returns no rows at all.
        var query = db.DirectMessages.AsNoTracking()
            .Where(m => m.ConversationId == conversationId
                        && (m.Conversation.UserAId == userId || m.Conversation.UserBId == userId));
        if (before is { } seq) query = query.Where(m => m.Seq < seq);

        var rows = await query.OrderByDescending(m => m.Seq).Take(take).ToListAsync(ct);
        return rows.Select(m => (m, m.Seq)).ToList();
    }

    public async Task<DirectMessage> AddMessageAsync(
        Guid conversationId, Guid senderId, string content, DateTimeOffset now, CancellationToken ct = default)
    {
        var conversation = await db.Conversations.FirstAsync(c => c.Id == conversationId, ct);
        var message = new DirectMessage
        {
            Id = Guid.NewGuid(),
            ConversationId = conversationId,
            SenderUserId = senderId,
            Content = content,
            CreatedAt = now,
        };
        db.DirectMessages.Add(message);

        conversation.LastMessageAt = now;
        conversation.LastMessagePreview = MessagingService.Preview(content);
        conversation.LastSenderId = senderId;
        // Sending implies having read everything up to now.
        if (conversation.UserAId == senderId) conversation.UserALastReadAt = now;
        else conversation.UserBLastReadAt = now;

        // One SaveChanges: the message and the inbox columns commit together.
        await db.SaveChangesAsync(ct);
        return message;
    }

    public async Task MarkReadAsync(Guid conversationId, Guid userId, DateTimeOffset at, CancellationToken ct = default)
    {
        await db.Conversations.Where(c => c.Id == conversationId && c.UserAId == userId)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.UserALastReadAt, at), ct);
        await db.Conversations.Where(c => c.Id == conversationId && c.UserBId == userId)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.UserBLastReadAt, at), ct);
    }

    public Task<int> CountUnreadAsync(Guid userId, CancellationToken ct = default) =>
        db.DirectMessages.AsNoTracking().CountAsync(m =>
            m.SenderUserId != userId
            && ((m.Conversation.UserAId == userId
                 && (m.Conversation.UserALastReadAt == null || m.CreatedAt > m.Conversation.UserALastReadAt))
                || (m.Conversation.UserBId == userId
                    && (m.Conversation.UserBLastReadAt == null || m.CreatedAt > m.Conversation.UserBLastReadAt))),
            ct);

    private Task<Guid?> FindPairAsync(Guid userA, Guid userB, CancellationToken ct) =>
        db.Conversations.AsNoTracking()
            .Where(c => c.UserAId == userA && c.UserBId == userB)
            .Select(c => (Guid?)c.Id)
            .FirstOrDefaultAsync(ct);

    /// <summary>Conversations with both people's display columns and each side's
    /// unread count - messages from the other person after that side's read mark.</summary>
    private IQueryable<ConversationRow> Rows(IQueryable<Conversation> conversations) =>
        conversations.AsNoTracking().Select(c => new ConversationRow(
            c,
            new ParticipantDto(c.UserA.Id, c.UserA.Name, c.UserA.PhotoUrl, c.UserA.Role),
            new ParticipantDto(c.UserB.Id, c.UserB.Name, c.UserB.PhotoUrl, c.UserB.Role),
            db.DirectMessages.Count(m => m.ConversationId == c.Id && m.SenderUserId != c.UserAId
                                         && (c.UserALastReadAt == null || m.CreatedAt > c.UserALastReadAt)),
            db.DirectMessages.Count(m => m.ConversationId == c.Id && m.SenderUserId != c.UserBId
                                         && (c.UserBLastReadAt == null || m.CreatedAt > c.UserBLastReadAt))));
}
