using SportMeet.Application.Common;
using SportMeet.Domain.Entities;
using SportMeet.Domain.Enums;

namespace SportMeet.Application.Messaging;

/// <summary>The other person in a conversation, as the inbox shows them.</summary>
public sealed record ParticipantDto(Guid Id, string Name, string? PhotoUrl, UserRole Role);

public sealed record ConversationDto(
    Guid Id,
    ParticipantDto Other,
    string? LastMessagePreview,
    DateTimeOffset? LastMessageAt,
    bool LastMessageIsMine,
    int UnreadCount);

public sealed record MessageDto(Guid Id, Guid ConversationId, Guid SenderId, string Content, DateTimeOffset CreatedAt);

/// <param name="Items">Oldest first, ready to render top to bottom.</param>
/// <param name="Before">Pass back as <c>before</c> for older messages; null at the start.</param>
public sealed record MessagePage(IReadOnlyList<MessageDto> Items, long? Before);

public sealed record StartConversationDto(Guid RecipientUserId);

public sealed record SendMessageDto(string Content);

/// <summary>A conversation row with both people's display columns, for the caller to
/// pick "the other one" from.</summary>
public sealed record ConversationRow(
    Conversation Conversation,
    ParticipantDto UserA,
    ParticipantDto UserB,
    int UnreadForA,
    int UnreadForB);

/// <summary>
/// Storage for direct messages. Every read that returns message content takes the
/// caller's id and filters on it in SQL, so isolation does not depend on the service
/// remembering a check: a query for someone else's conversation simply finds nothing.
/// </summary>
public interface IMessagingRepository
{
    Task<IReadOnlyList<ConversationRow>> ListForUserAsync(Guid userId, CancellationToken ct = default);

    /// <summary>Null unless <paramref name="userId"/> is one of the two participants.</summary>
    Task<ConversationRow?> GetForUserAsync(Guid conversationId, Guid userId, CancellationToken ct = default);

    /// <summary>The pair's conversation, created if it does not exist. Concurrent
    /// first messages converge on one row through the unique pair key.</summary>
    Task<Guid> GetOrCreateAsync(Guid userA, Guid userB, DateTimeOffset now, CancellationToken ct = default);

    Task<bool> UserExistsAsync(Guid userId, CancellationToken ct = default);

    /// <summary>Messages of a conversation the caller belongs to, newest first, with
    /// <c>seq &lt; before</c>; empty when the caller is not a participant.</summary>
    Task<IReadOnlyList<(DirectMessage Message, long Seq)>> ListMessagesAsync(
        Guid conversationId, Guid userId, long? before, int take, CancellationToken ct = default);

    /// <summary>Stores the message and updates the conversation's last-message columns
    /// and the sender's read mark in one transaction.</summary>
    Task<DirectMessage> AddMessageAsync(
        Guid conversationId, Guid senderId, string content, DateTimeOffset now, CancellationToken ct = default);

    Task MarkReadAsync(Guid conversationId, Guid userId, DateTimeOffset at, CancellationToken ct = default);

    Task<int> CountUnreadAsync(Guid userId, CancellationToken ct = default);
}

/// <summary>Real-time delivery. The API implements it with SignalR; the service only
/// says who must hear about what.</summary>
public interface IMessageNotifier
{
    Task MessageSentAsync(MessageDto message, IReadOnlyCollection<Guid> recipients, CancellationToken ct = default);

    Task ConversationReadAsync(Guid conversationId, Guid userId, CancellationToken ct = default);
}

/// <summary>
/// One-to-one direct messages.
///
/// Isolation is the whole job here, and it is enforced in two places that do not
/// depend on each other:
///  - every repository read is scoped by the caller in SQL;
///  - every service method resolves the conversation *for this caller* first and
///    answers 404 when it is not theirs - not 403, which would confirm to a stranger
///    that the conversation exists.
/// The caller always comes from <see cref="ICurrentUser"/>, never from the request
/// body, and the API refuses the configured demo identity outright on these routes,
/// so a misconfigured server cannot hand one shared identity's inbox to every
/// anonymous visitor.
///
/// Messages are stored as plain text and are not end-to-end encrypted: they are
/// protected in transit by HTTPS (once the site has it) and at rest by database
/// access control, and anyone with direct database access can read them.
/// </summary>
public sealed class MessagingService(
    IMessagingRepository messages,
    IMessageNotifier notifier,
    ICurrentUser? currentUser = null)
{
    public const int ContentMaxLength = 2_000;
    public const int PreviewLength = 140;
    public const int PageSize = 50;

    public async Task<IReadOnlyList<ConversationDto>> ListConversationsAsync(CancellationToken ct = default)
    {
        var me = Me();
        var rows = await messages.ListForUserAsync(me, ct);
        return rows
            // A conversation nobody has written in yet is not part of the inbox.
            .Where(r => r.Conversation.LastMessageAt is not null)
            .OrderByDescending(r => r.Conversation.LastMessageAt)
            .Select(r => ToDto(r, me))
            .ToList();
    }

    /// <summary>Open (or reopen) the thread with someone. Returns the conversation;
    /// nothing is sent until the first message.</summary>
    public async Task<ConversationDto> StartAsync(StartConversationDto dto, CancellationToken ct = default)
    {
        var me = Me();
        if (dto.RecipientUserId == me) throw new DomainRuleException("You can't message yourself.");
        if (!await messages.UserExistsAsync(dto.RecipientUserId, ct))
            throw new NotFoundException("User", dto.RecipientUserId, dto.RecipientUserId);

        var (a, b) = Conversation.Order(me, dto.RecipientUserId);
        var id = await messages.GetOrCreateAsync(a, b, DateTimeOffset.UtcNow, ct);
        var row = await messages.GetForUserAsync(id, me, ct)
            ?? throw new InvalidOperationException("A conversation that was just created could not be read back.");
        return ToDto(row, me);
    }

    public async Task<ConversationDto> GetConversationAsync(Guid id, CancellationToken ct = default)
    {
        var me = Me();
        var row = await messages.GetForUserAsync(id, me, ct) ?? throw NotFound(id);
        return ToDto(row, me);
    }

    public async Task<MessagePage> ListMessagesAsync(Guid id, long? before, CancellationToken ct = default)
    {
        var me = Me();
        _ = await messages.GetForUserAsync(id, me, ct) ?? throw NotFound(id);

        // One extra row answers "is there anything older" without a COUNT.
        var page = await messages.ListMessagesAsync(id, me, before, PageSize + 1, ct);
        var older = page.Count > PageSize;
        var items = page.Take(PageSize).Reverse().Select(m => ToDto(m.Message)).ToList();
        return new MessagePage(items, older ? page[PageSize - 1].Seq : null);
    }

    public async Task<MessageDto> SendAsync(Guid id, SendMessageDto dto, CancellationToken ct = default)
    {
        var me = Me();
        var content = dto.Content?.Trim() ?? "";
        if (content.Length == 0) throw new DomainRuleException("Write a message first.");
        if (content.Length > ContentMaxLength)
            throw new DomainRuleException($"Messages can be at most {ContentMaxLength:N0} characters.");

        var row = await messages.GetForUserAsync(id, me, ct) ?? throw NotFound(id);

        var stored = await messages.AddMessageAsync(id, me, content, DateTimeOffset.UtcNow, ct);
        var message = ToDto(stored);
        // Both sides: the recipient's inbox, and the sender's other open tabs.
        await notifier.MessageSentAsync(message, [row.Conversation.UserAId, row.Conversation.UserBId], ct);
        return message;
    }

    public async Task MarkReadAsync(Guid id, CancellationToken ct = default)
    {
        var me = Me();
        _ = await messages.GetForUserAsync(id, me, ct) ?? throw NotFound(id);
        await messages.MarkReadAsync(id, me, DateTimeOffset.UtcNow, ct);
        await notifier.ConversationReadAsync(id, me, ct);
    }

    public Task<int> UnreadCountAsync(CancellationToken ct = default) => messages.CountUnreadAsync(Me(), ct);

    /// <summary>A short single-line preview for the inbox.</summary>
    public static string Preview(string content)
    {
        var line = string.Join(' ', content.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return line.Length <= PreviewLength ? line : line[..PreviewLength].TrimEnd() + "…";
    }

    private Guid Me() =>
        currentUser?.UserId ?? throw new DomainRuleException("You must be signed in to use messages.");

    private static NotFoundException NotFound(Guid id) => new("Conversation", id, id);

    private static ConversationDto ToDto(ConversationRow row, Guid me)
    {
        var mineIsA = row.Conversation.UserAId == me;
        return new ConversationDto(
            row.Conversation.Id,
            mineIsA ? row.UserB : row.UserA,
            row.Conversation.LastMessagePreview,
            row.Conversation.LastMessageAt,
            row.Conversation.LastSenderId == me,
            mineIsA ? row.UnreadForA : row.UnreadForB);
    }

    private static MessageDto ToDto(DirectMessage m) =>
        new(m.Id, m.ConversationId, m.SenderUserId, m.Content, m.CreatedAt);
}
