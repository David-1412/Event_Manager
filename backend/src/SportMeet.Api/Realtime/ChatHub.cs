using Microsoft.AspNetCore.SignalR;
using SportMeet.Application.Common;
using SportMeet.Application.Messaging;

namespace SportMeet.Api.Realtime;

/// <summary>
/// Real-time delivery for direct messages - <c>/hubs/chat</c>.
///
/// Push only. The hub exposes no methods a client can call: messages are sent
/// through <c>POST /api/messages/...</c>, where they are validated, rate-limited and
/// stored, and only then pushed here. A hub method that wrote messages would be a
/// second write path to secure, so there is none.
///
/// Each connection joins exactly one group, its own user's, and pushes go to the
/// groups of the conversation's two participants - so a connection can only ever
/// receive messages addressed to the person who opened it. A connection without a
/// verified identity is closed on arrival; that includes the configured demo
/// identity, which would otherwise let any anonymous visitor listen to one shared
/// inbox.
/// </summary>
public sealed class ChatHub : Hub
{
    public const string Path = "/hubs/chat";

    public static string UserGroup(Guid userId) => $"user:{userId:N}";

    public override async Task OnConnectedAsync()
    {
        var http = Context.GetHttpContext();
        var currentUser = http?.RequestServices.GetService<ICurrentUser>();
        if (currentUser?.UserId is not { } userId || currentUser.IsDemo)
        {
            Context.Abort();
            return;
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, UserGroup(userId));
        await base.OnConnectedAsync();
    }
}

/// <summary><see cref="IMessageNotifier"/> over SignalR. Event names are the contract
/// with the frontend's messaging provider.</summary>
public sealed class SignalRMessageNotifier(IHubContext<ChatHub> hub) : IMessageNotifier
{
    public const string MessageReceived = "messageReceived";
    public const string ConversationRead = "conversationRead";

    public Task MessageSentAsync(MessageDto message, IReadOnlyCollection<Guid> recipients, CancellationToken ct = default) =>
        hub.Clients.Groups(recipients.Distinct().Select(ChatHub.UserGroup).ToList())
            .SendAsync(MessageReceived, message, ct);

    public Task ConversationReadAsync(Guid conversationId, Guid userId, CancellationToken ct = default) =>
        hub.Clients.Group(ChatHub.UserGroup(userId)).SendAsync(ConversationRead, new { conversationId }, ct);
}
