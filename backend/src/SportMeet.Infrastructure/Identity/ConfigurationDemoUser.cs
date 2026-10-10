using Microsoft.Extensions.Options;
using SportMeet.Application.Common;
using SportMeet.Domain.Enums;
using SportMeet.Infrastructure.Seeding;

namespace SportMeet.Infrastructure.Identity;

/// <summary>
/// The identity used while Firebase auth is out of scope.
///
/// Deliberately placed in Infrastructure and driven entirely by configuration:
/// the demo id is not a constant, and nothing in Application knows it exists.
/// Replacing this with the token-backed implementation is a one-line change in
/// DependencyInjection, with no edits to EventService or the repository.
///
/// <b>Not registered.</b> <c>AddSportMeetInfrastructure</c> binds
/// <see cref="ICurrentUser"/> to <see cref="TokenCurrentUser"/>, which is the
/// only implementation the API resolves; this class remains as the shape the
/// demo fallback documents and as the type the demo options are bound on. Its
/// <see cref="Role"/> is deliberately non-privileged so that reviving it as
/// the acting identity cannot silently hand every anonymous caller the review
/// queue - see that member.
/// </summary>
public sealed class ConfigurationDemoUser(IOptions<DemoUserOptions> options) : ICurrentUser
{
    public Guid? UserId => options.Value.AsUserId;

    /// <summary>True exactly when a demo id is configured. The name is what
    /// surfaces in logs and in the "demo mode" startup warning, so nothing can
    /// mistake this for a verified caller.</summary>
    public bool IsDemo => options.Value.AsUserId is not null;

    /// <summary>Always Member. Presence of a configured id proves nothing about
    /// privileges, and unlike <see cref="TokenCurrentUser"/> this class has no
    /// database to look the role up in - so it asserts none. The demo review
    /// queue is reached through TokenCurrentUser, which reads the demo id's
    /// users.role row and therefore still requires an actual Admin row.
    /// Returning anything higher here would be a standing privilege grant keyed
    /// on a config value; nothing should consume one.</summary>
    public UserRole Role => UserRole.Member;
}

/// <summary>
/// Bound from the "Demo" configuration section.
///
/// AsUserId is the only demo identity in the system. Leaving it unset is the
/// supported way to run with no identity at all: browse and detail still work,
/// isHost/isJoined become false everywhere, and create fails with a clear
/// "signed in required" message instead of inventing a host.
/// </summary>
public sealed class DemoUserOptions
{
    public const string SectionName = "Demo";

    /// <summary>
    /// The identity the API treats as the current viewer. Defaults to the demo
    /// *participant* (who hosts nothing), not the host, so join/leave are possible
    /// out of the box. Config can override it; setting it to the host id would make
    /// every join fail with "You cannot join an event you are hosting".
    /// </summary>
    public Guid? AsUserId { get; set; } = SportMeet.Infrastructure.Seeding.DemoSeed.DefaultParticipantId;

    /// <summary>Name given to the seeded participant row when it is created. Only
    /// read when seeding.</summary>
    public string ParticipantName { get; set; } = SportMeet.Infrastructure.Seeding.DemoSeed.DefaultParticipantName;

    /// <summary>Must match the host seeded into users. Only read when seeding,
    /// so it never reaches a query.</summary>
    public Guid SeedHostUserId { get; set; } = DemoSeed.DefaultHostId;

    public string SeedHostName { get; set; } = DemoSeed.DefaultHostName;
}
