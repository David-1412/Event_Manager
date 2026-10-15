using SportMeet.Application.Events;
using SportMeet.Application.Notifications;
using SportMeet.Domain.Entities;

namespace SportMeet.Import.Tests;

/// <summary>In-memory stand-in for the reminder repository.
///
/// The duplication guarantee is a property of the real claim - a conditional UPDATE
/// whose WHERE clause matches only while the sentinel is null - so the fake
/// reproduces exactly that: <see cref="Claimed"/> is what a claim writes, and a claim
/// against an event that already carries a value returns false and changes nothing.
/// A fake that always returned true would let every duplicate test pass while proving
/// nothing, which is the failure mode worth designing against here.
///
/// Deliberately literal about the rest: candidates are a List the test populates, and
/// the filters the SQL would apply are the test's responsibility. That keeps
/// "cancelled events send nothing" an assertion about the boundary between the two -
/// the fake holds no cancelled event, so the service cannot send one, and the query
/// that keeps it out is covered separately.</summary>
internal sealed class FakeReminderRepo : IEventReminderRepository
{
    /// <summary>What the next <see cref="ListDueAsync"/> returns, per kind. A queue
    /// rather than one list so a test can give the two reminders different rosters.</summary>
    public Dictionary<ReminderKind, List<ReminderCandidate>> Due { get; } = new()
    {
        [ReminderKind.Tomorrow] = [],
        [ReminderKind.OneHour] = [],
    };

    /// <summary>The sentinels, keyed event then kind. What a claim wrote.</summary>
    public Dictionary<(Guid EventId, ReminderKind Kind), DateTimeOffset> Claimed { get; } = [];

    /// <summary>Every claim call, in order, including ones that returned false - so a
    /// test can tell "never asked" from "asked and lost the race".</summary>
    public List<(Guid EventId, ReminderKind Kind)> ClaimAttempts { get; } = [];

    /// <summary>Arguments of the last read, so a test can assert the horizon the service
    /// asked for rather than only what it did with the answer.</summary>
    public (ReminderKind Kind, DateTimeOffset Now, int Limit)? LastQuery { get; private set; }

    /// <summary>Set to make a claim fail as though another worker won the race.</summary>
    public HashSet<Guid> RefuseClaimsFor { get; } = [];

    public Task<List<ReminderCandidate>> ListDueAsync(
        ReminderKind kind,
        DateTimeOffset now,
        int limit,
        CancellationToken ct = default)
    {
        LastQuery = (kind, now, limit);
        return Task.FromResult(Due[kind]);
    }

    public Task<bool> TryClaimAsync(
        Guid eventId,
        ReminderKind kind,
        DateTimeOffset sentAt,
        CancellationToken ct = default)
    {
        ClaimAttempts.Add((eventId, kind));

        if (RefuseClaimsFor.Contains(eventId)) return Task.FromResult(false);

        // The whole point: a second claim against the same (event, kind) finds the
        // sentinel set and loses, exactly as the UPDATE's WHERE clause would.
        if (Claimed.ContainsKey((eventId, kind))) return Task.FromResult(false);

        Claimed[(eventId, kind)] = sentAt;
        return Task.FromResult(true);
    }
}

/// <summary>Records the notifications written. Separate from the join tests' fake
/// because that one asserts on join-workflow types and this one needs the full
/// notification, including the message text the requirement fixes.</summary>
internal sealed class RecordingNotifications : INotificationRepository
{
    public List<Notification> Added { get; } = [];

    public Task AddAsync(Notification notification, CancellationToken ct = default)
    {
        Added.Add(notification);
        return Task.CompletedTask;
    }

    public Task<(IReadOnlyList<NotificationDto> Items, int UnreadCount)> ListForUserAsync(
        Guid userId, int limit, CancellationToken ct = default)
    {
        IReadOnlyList<NotificationDto> none = [];
        return Task.FromResult((none, 0));
    }

    public Task<bool> MarkReadAsync(Guid id, Guid userId, CancellationToken ct = default)
        => Task.FromResult(false);

    public Task MarkAllReadAsync(Guid userId, CancellationToken ct = default)
        => Task.CompletedTask;
}
