using SportMeet.Application.Notifications;
using SportMeet.Domain.Entities;

namespace SportMeet.Application.Profiles;

/// <summary>
/// Tells a creator's followers that the creator has an event out.
///
/// The rule the whole feature hangs on: notify on the transition into public
/// visibility, and on nothing else. A Draft, PendingReview or Rejected event is not
/// publicly visible, so announcing it would tell followers about an event they cannot
/// see and that an admin may still refuse - and would leak the existence of something
/// its creator had not finished deciding to publish. A Private event is likewise not
/// announced: it is deliberately undiscoverable, and a notification is the loudest
/// possible way to make something discoverable.
///
/// The caller therefore decides nothing about eligibility beyond having just performed
/// a publish. This class re-checks rather than trusting that, because it has three call
/// sites reached from three different code paths, and a fan-out that trusts its callers
/// is one new call site away from announcing rejections.
///
/// Notification rows are written inside the caller's transaction, matching
/// EventService's existing notifications: a follower who is told about an event can
/// always open it, and an event that is visible can always be found by its followers.
/// The cost of that choice is stated plainly in <see cref="PublishAsync"/>.
/// </summary>
public sealed class CreatorFollowNotifier(
    IProfileRepository profiles,
    INotificationRepository notifications) : ICreatorFollowNotifier
{
    /// <summary>The notification's title. Fixed, per the spec: the differentiation is
    /// in the message, where the creator's name and the event's title are the parts a
    /// reader acts on.</summary>
    public const string Title = "New Event";

    /// <summary>Announce a newly visible event to its host's followers.
    ///
    /// Returns the number of notifications written, which is zero for every case where
    /// the rule says no - a private event, an unpublished one, a host with no
    /// followers, or a host who somehow follows themselves. Callers do not branch on
    /// it; it exists so the behaviour is observable to a test and countable in a log.
    ///
    /// Runs in the caller's transaction. A publish that cannot tell its audience fails
    /// with it, which is the correct trade for a feature whose entire purpose is that
    /// the audience is told: a silently undelivered announcement is not something any
    /// later process can reconstruct, because nothing records who should have been
    /// told. At a follower count where the insert is one batched statement, holding the
    /// transaction open for a few extra milliseconds is the cheaper of the two errors.
    /// </summary>
    public async Task<int> PublishAsync(Event published, CancellationToken ct = default)
    {
        // One call answers both questions the rule needs - may the world see this, and
        // who is telling - and returns nothing for an event that does not qualify, so
        // the guard cannot be skipped by a caller that forgot to check.
        var (followers, hostName) = await profiles.ListFollowersToNotifyAsync(published, ct);
        if (followers.Count == 0)
        {
            return 0;
        }

        var now = DateTimeOffset.UtcNow;
        var message = $"{hostName ?? "A creator you follow"} has created a new event: \"{published.Title}\"";

        foreach (var followerId in followers)
        {
            await notifications.AddAsync(new Notification
            {
                Id = Guid.NewGuid(),
                UserId = followerId,
                Title = Title,
                Message = message,
                Type = NotificationTypes.FollowedCreatorPublished,
                // The event, not the profile: the point of the notification is to get a
                // follower to this specific event, which is the link every other
                // notification in this codebase uses and the one the bell already
                // routes.
                Link = $"/events/{published.Id}",
                Read = false,
                CreatedAt = now,
            }, ct);
        }

        return followers.Count;
    }
}
