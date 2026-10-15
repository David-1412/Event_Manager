using SportMeet.Domain.Entities;

namespace SportMeet.Application.Profiles;

/// <summary>
/// The seam the publish paths announce through.
///
/// An interface for the same reason <see cref="IProfileRepository"/> has one and
/// <see cref="ProfileService"/> does not: it sits on a write path that the event
/// service reaches from three places, and the existing event tests construct an
/// <c>EventService</c> with fakes. Without a seam those tests would have to build a
/// real notifier and a real profile repository to test something unrelated, and a
/// publish path with no way to inject "nothing" is a publish path nobody can test in
/// isolation.
/// </summary>
public interface ICreatorFollowNotifier
{
    /// <summary>Announce a newly visible event to its host's followers, returning how
    /// many notifications were written. See <see cref="CreatorFollowNotifier"/> for
    /// when that is zero.</summary>
    Task<int> PublishAsync(Event published, CancellationToken ct = default);
}
