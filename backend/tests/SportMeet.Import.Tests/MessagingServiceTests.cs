using SportMeet.Application.Common;
using SportMeet.Application.Messaging;
using SportMeet.Domain.Entities;
using SportMeet.Domain.Enums;

namespace SportMeet.Import.Tests;

/// <summary>
/// Direct-message rules at the service boundary: a third person can never read,
/// send into, or even confirm the existence of someone else's conversation, and
/// real-time pushes go to the two participants only. MessagingSqlTests checks the
/// same isolation in the real SQL.
/// </summary>
public sealed class MessagingServiceTests
{
    private static readonly Guid Alice = Guid.Parse("10000000-0000-0000-0000-000000000001");
    private static readonly Guid Bob = Guid.Parse("20000000-0000-0000-0000-000000000002");
    private static readonly Guid Eve = Guid.Parse("30000000-0000-0000-0000-000000000003");

    [Fact]
    public async Task Both_people_land_in_the_same_conversation()
    {
        var store = new FakeStore();
        var fromAlice = await As(Alice, store).StartAsync(new(Bob));
        var fromBob = await As(Bob, store).StartAsync(new(Alice));

        Assert.Equal(fromAlice.Id, fromBob.Id);
        Assert.Equal(Bob, fromAlice.Other.Id);
        Assert.Equal(Alice, fromBob.Other.Id);
    }

    [Fact]
    public async Task A_third_person_cannot_read_send_or_confirm_a_conversation()
    {
        var store = new FakeStore();
        var conversation = await As(Alice, store).StartAsync(new(Bob));
        await As(Alice, store).SendAsync(conversation.Id, new("Hi Bob"));

        var eve = As(Eve, store);
        await Assert.ThrowsAsync<NotFoundException>(() => eve.ListMessagesAsync(conversation.Id, null));
        await Assert.ThrowsAsync<NotFoundException>(() => eve.SendAsync(conversation.Id, new("hello?")));
        await Assert.ThrowsAsync<NotFoundException>(() => eve.GetConversationAsync(conversation.Id));
        await Assert.ThrowsAsync<NotFoundException>(() => eve.MarkReadAsync(conversation.Id));
        Assert.Empty(await eve.ListConversationsAsync());
        Assert.Single(store.Messages);
    }

    [Fact]
    public async Task Pushes_go_to_the_two_participants_only()
    {
        var store = new FakeStore();
        var conversation = await As(Alice, store).StartAsync(new(Bob));
        await As(Alice, store).SendAsync(conversation.Id, new("Hi"));

        var push = Assert.Single(store.Notifier.Sent);
        Assert.Equal(new HashSet<Guid> { Alice, Bob }, push.Recipients.ToHashSet());
    }

    [Fact]
    public async Task Unread_counts_follow_the_other_persons_messages_and_clear_on_read()
    {
        var store = new FakeStore();
        var conversation = await As(Alice, store).StartAsync(new(Bob));
        await As(Alice, store).SendAsync(conversation.Id, new("One"));
        await As(Alice, store).SendAsync(conversation.Id, new("Two"));

        Assert.Equal(2, await As(Bob, store).UnreadCountAsync());
        Assert.Equal(0, await As(Alice, store).UnreadCountAsync());

        await As(Bob, store).MarkReadAsync(conversation.Id);
        Assert.Equal(0, await As(Bob, store).UnreadCountAsync());
    }

    [Fact]
    public async Task Inbox_lists_only_conversations_with_messages_newest_first()
    {
        var store = new FakeStore();
        var withBob = await As(Alice, store).StartAsync(new(Bob));
        var withEve = await As(Alice, store).StartAsync(new(Eve));
        await As(Alice, store).SendAsync(withBob.Id, new("older"));
        await As(Alice, store).SendAsync(withEve.Id, new("newer"));

        var inbox = await As(Alice, store).ListConversationsAsync();
        Assert.Equal([withEve.Id, withBob.Id], inbox.Select(c => c.Id));
        Assert.True(inbox[0].LastMessageIsMine);

        await As(Bob, store).StartAsync(new(Eve));
        Assert.Empty(await As(Eve, store).ListConversationsAsync().ContinueWith(t =>
            t.Result.Where(c => c.Other.Id == Bob).ToList()));
    }

    [Fact]
    public async Task You_cannot_message_yourself_or_someone_who_does_not_exist()
    {
        var store = new FakeStore();
        await Assert.ThrowsAsync<DomainRuleException>(() => As(Alice, store).StartAsync(new(Alice)));
        await Assert.ThrowsAsync<NotFoundException>(() => As(Alice, store).StartAsync(new(Guid.NewGuid())));
    }

    [Theory]
    [InlineData("   ")]
    [InlineData(null)]
    public async Task Blank_and_overlong_messages_are_rejected(string? content)
    {
        var store = new FakeStore();
        var conversation = await As(Alice, store).StartAsync(new(Bob));
        await Assert.ThrowsAsync<DomainRuleException>(() => As(Alice, store).SendAsync(conversation.Id, new(content!)));
        await Assert.ThrowsAsync<DomainRuleException>(() =>
            As(Alice, store).SendAsync(conversation.Id, new(new string('x', MessagingService.ContentMaxLength + 1))));
        Assert.Empty(store.Messages);
    }

    [Fact]
    public async Task Signed_out_callers_get_nothing()
    {
        var service = new MessagingService(new FakeStore(), new FakeNotifier());
        await Assert.ThrowsAsync<DomainRuleException>(() => service.ListConversationsAsync());
    }

    [Fact]
    public void Pair_order_is_written_byte_order_whichever_way_round_it_is_given()
    {
        // Written byte order is what Postgres compares (MessagingSqlTests checks the
        // database agrees for many random pairs).
        var low = Guid.Parse("00ff0000-0000-0000-0000-000000000001");
        var high = Guid.Parse("01000000-0000-0000-0000-000000000000");
        Assert.Equal((low, high), Conversation.Order(low, high));
        Assert.Equal((low, high), Conversation.Order(high, low));
    }

    [Fact]
    public void Preview_is_one_trimmed_line()
    {
        Assert.Equal("Hi there friend", MessagingService.Preview("  Hi\n there\t friend  "));
        Assert.EndsWith("…", MessagingService.Preview(new string('a', 500)));
    }

    private static MessagingService As(Guid user, FakeStore store) =>
        new(store, store.Notifier, new FakeUser(user));

    private sealed class FakeStore : IMessagingRepository
    {
        private readonly List<Conversation> _conversations = [];
        public List<DirectMessage> Messages { get; } = [];
        public FakeNotifier Notifier { get; } = new();
        private long _seq;
        private int _tick;

        private static ParticipantDto P(Guid id) => new(id, id.ToString()[..1], null, UserRole.Member);

        private ConversationRow Row(Conversation c) => new(c, P(c.UserAId), P(c.UserBId),
            Messages.Count(m => m.ConversationId == c.Id && m.SenderUserId != c.UserAId
                                && (c.UserALastReadAt == null || m.CreatedAt > c.UserALastReadAt)),
            Messages.Count(m => m.ConversationId == c.Id && m.SenderUserId != c.UserBId
                                && (c.UserBLastReadAt == null || m.CreatedAt > c.UserBLastReadAt)));

        private DateTimeOffset Now() => DateTimeOffset.UnixEpoch.AddMinutes(++_tick);

        public Task<IReadOnlyList<ConversationRow>> ListForUserAsync(Guid userId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<ConversationRow>>(_conversations.Where(c => c.Includes(userId)).Select(Row).ToList());

        public Task<ConversationRow?> GetForUserAsync(Guid conversationId, Guid userId, CancellationToken ct = default) =>
            Task.FromResult(_conversations.Where(c => c.Id == conversationId && c.Includes(userId)).Select(Row).FirstOrDefault());

        public Task<Guid> GetOrCreateAsync(Guid userA, Guid userB, DateTimeOffset now, CancellationToken ct = default)
        {
            var existing = _conversations.FirstOrDefault(c => c.UserAId == userA && c.UserBId == userB);
            if (existing is null)
            {
                existing = new Conversation { Id = Guid.NewGuid(), UserAId = userA, UserBId = userB, CreatedAt = Now() };
                _conversations.Add(existing);
            }
            return Task.FromResult(existing.Id);
        }

        public Task<bool> UserExistsAsync(Guid userId, CancellationToken ct = default) =>
            Task.FromResult(userId == Alice || userId == Bob || userId == Eve);

        public Task<IReadOnlyList<(DirectMessage Message, long Seq)>> ListMessagesAsync(
            Guid conversationId, Guid userId, long? before, int take, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<(DirectMessage, long)>>(Messages
                .Where(m => m.ConversationId == conversationId
                            && _conversations.Single(c => c.Id == conversationId).Includes(userId)
                            && (before == null || m.Seq < before))
                .OrderByDescending(m => m.Seq).Take(take).Select(m => (m, m.Seq)).ToList());

        public Task<DirectMessage> AddMessageAsync(
            Guid conversationId, Guid senderId, string content, DateTimeOffset now, CancellationToken ct = default)
        {
            var at = Now();
            var message = new DirectMessage
            {
                Id = Guid.NewGuid(), Seq = ++_seq, ConversationId = conversationId,
                SenderUserId = senderId, Content = content, CreatedAt = at,
            };
            Messages.Add(message);
            var c = _conversations.Single(x => x.Id == conversationId);
            c.LastMessageAt = at;
            c.LastSenderId = senderId;
            c.LastMessagePreview = MessagingService.Preview(content);
            if (c.UserAId == senderId) c.UserALastReadAt = at; else c.UserBLastReadAt = at;
            return Task.FromResult(message);
        }

        public Task MarkReadAsync(Guid conversationId, Guid userId, DateTimeOffset at, CancellationToken ct = default)
        {
            var c = _conversations.Single(x => x.Id == conversationId);
            var now = Now();
            if (c.UserAId == userId) c.UserALastReadAt = now; else c.UserBLastReadAt = now;
            return Task.CompletedTask;
        }

        public Task<int> CountUnreadAsync(Guid userId, CancellationToken ct = default) =>
            Task.FromResult(_conversations.Where(c => c.Includes(userId))
                .Sum(c => c.UserAId == userId ? Row(c).UnreadForA : Row(c).UnreadForB));
    }

    private sealed class FakeNotifier : IMessageNotifier
    {
        public List<(MessageDto Message, IReadOnlyCollection<Guid> Recipients)> Sent { get; } = [];

        public Task MessageSentAsync(MessageDto message, IReadOnlyCollection<Guid> recipients, CancellationToken ct = default)
        {
            Sent.Add((message, recipients));
            return Task.CompletedTask;
        }

        public Task ConversationReadAsync(Guid conversationId, Guid userId, CancellationToken ct = default) =>
            Task.CompletedTask;
    }

    private sealed class FakeUser(Guid id) : ICurrentUser
    {
        public Guid? UserId => id;
        public bool IsDemo => false;
        public bool IsAdmin => false;
        public bool CanPublishPublicEvents => false;
    }
}
