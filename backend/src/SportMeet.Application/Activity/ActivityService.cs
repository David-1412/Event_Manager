using SportMeet.Application.Common;
using SportMeet.Domain.Enums;

namespace SportMeet.Application.Activity;

/// <summary>The three tabs above the inbox.</summary>
public enum ActivityKind
{
    Likes,
    Follows,
    Comments,
}

/// <summary>Who did it. Null for an anonymous comment.</summary>
public sealed record ActivityActorDto(Guid Id, string Name, string? PhotoUrl, UserRole Role);

/// <param name="Key">Stable id of the item; pass it back to mark it read.</param>
/// <param name="Action">What happened, ready to show after the name ("liked your post").</param>
/// <param name="TargetTitle">What it happened to, e.g. the post title. Null for a follow.</param>
/// <param name="Snippet">The comment text, for comments.</param>
/// <param name="Href">Where tapping the item goes.</param>
public sealed record ActivityItemDto(
    string Key,
    ActivityKind Kind,
    ActivityActorDto? Actor,
    string Action,
    string? TargetTitle,
    string? Snippet,
    string Href,
    DateTimeOffset CreatedAt,
    bool IsRead);

public sealed record ActivityCountsDto(int Likes, int Follows, int Comments);

public sealed record MarkActivityReadDto(IReadOnlyList<string> Keys);

/// <summary>An item as the repository finds it, before read state is applied.
/// <see cref="Actor"/> is always the real person; the service hides anonymous ones.</summary>
public sealed record ActivityRow(
    string Key,
    ActivityKind Kind,
    ActivityActorDto Actor,
    bool ActorIsAnonymous,
    string Action,
    string? TargetTitle,
    string? Snippet,
    string Href,
    DateTimeOffset CreatedAt);

public interface IActivityRepository
{
    /// <summary>The newest items of one kind addressed to <paramref name="userId"/>
    /// since <paramref name="since"/>, newest first. Never includes the user's own
    /// actions, and never an automatic (first-view) interest.</summary>
    Task<IReadOnlyList<ActivityRow>> ListAsync(
        Guid userId, ActivityKind kind, DateTimeOffset since, int take, CancellationToken ct = default);

    /// <summary>Which of <paramref name="keys"/> the user has marked read.</summary>
    Task<IReadOnlySet<string>> ListReadAsync(Guid userId, IReadOnlyCollection<string> keys, CancellationToken ct = default);

    Task MarkReadAsync(Guid userId, IReadOnlyCollection<string> keys, DateTimeOffset at, CancellationToken ct = default);
}

/// <summary>
/// Likes, New Followers and Comments: activity on your posts, events and profile.
///
/// Read live from the rows that already record each action, with only the read mark
/// stored separately. Anonymous comments keep their anonymity here too: the item says
/// "Anonymous" and carries no actor, exactly as the thread itself does.
/// </summary>
public sealed class ActivityService(IActivityRepository activity, ICurrentUser? currentUser = null)
{
    /// <summary>How far back the tabs look. Older activity is not news.</summary>
    public static readonly TimeSpan Window = TimeSpan.FromDays(30);

    public const int PageSize = 50;
    public const int MaxKeysPerRead = 100;
    public const int MaxKeyLength = 200;

    public async Task<IReadOnlyList<ActivityItemDto>> ListAsync(ActivityKind kind, CancellationToken ct = default)
    {
        var me = Me();
        var rows = await activity.ListAsync(me, kind, DateTimeOffset.UtcNow - Window, PageSize, ct);
        var read = await activity.ListReadAsync(me, rows.Select(r => r.Key).ToList(), ct);
        return rows.Select(r => ToDto(r, read.Contains(r.Key))).ToList();
    }

    public async Task<ActivityCountsDto> UnreadCountsAsync(CancellationToken ct = default)
    {
        var me = Me();
        var since = DateTimeOffset.UtcNow - Window;
        async Task<int> Unread(ActivityKind kind)
        {
            var rows = await activity.ListAsync(me, kind, since, PageSize, ct);
            var read = await activity.ListReadAsync(me, rows.Select(r => r.Key).ToList(), ct);
            return rows.Count(r => !read.Contains(r.Key));
        }

        return new ActivityCountsDto(
            await Unread(ActivityKind.Likes),
            await Unread(ActivityKind.Follows),
            await Unread(ActivityKind.Comments));
    }

    /// <summary>Marks the given items read for the caller. A key can only ever mark the
    /// caller's own read state, so an arbitrary key is harmless - but they are bounded
    /// so the endpoint cannot be used to fill the table.</summary>
    public async Task MarkReadAsync(MarkActivityReadDto dto, CancellationToken ct = default)
    {
        var me = Me();
        var keys = (dto.Keys ?? [])
            .Where(k => !string.IsNullOrWhiteSpace(k) && k.Length <= MaxKeyLength)
            .Distinct()
            .ToList();
        if (keys.Count == 0) return;
        if (keys.Count > MaxKeysPerRead)
            throw new DomainRuleException($"Mark at most {MaxKeysPerRead} items at a time.");
        await activity.MarkReadAsync(me, keys, DateTimeOffset.UtcNow, ct);
    }

    private ActivityItemDto ToDto(ActivityRow row, bool isRead)
    {
        var showActor = !row.ActorIsAnonymous || currentUser?.IsAdmin == true;
        return new ActivityItemDto(
            row.Key,
            row.Kind,
            showActor ? row.Actor : null,
            row.Action,
            row.TargetTitle,
            row.Snippet,
            row.Href,
            row.CreatedAt,
            isRead);
    }

    private Guid Me() =>
        currentUser?.UserId ?? throw new DomainRuleException("You must be signed in to see your activity.");
}

/// <summary>
/// The stable item keys. Likes and follows include when they happened: someone who
/// unfollows and follows again is a new follow, and should show as unread again
/// rather than inheriting the old one's read mark.
/// </summary>
public static class ActivityKeys
{
    public static string PostLike(Guid postId, Guid userId, DateTimeOffset at) =>
        $"like:post:{postId:N}:{userId:N}:{at.UtcTicks}";

    public static string EventInterest(Guid eventId, Guid userId, DateTimeOffset at) =>
        $"like:event:{eventId:N}:{userId:N}:{at.UtcTicks}";

    public static string Follow(Guid followerId, DateTimeOffset at) => $"follow:{followerId:N}:{at.UtcTicks}";

    public static string Comment(Guid commentId) => $"comment:{commentId:N}";
}
