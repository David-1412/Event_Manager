using SportMeet.Domain.Enums;

namespace SportMeet.Domain.Entities;

public class User
{
    public Guid Id { get; set; }

    /// <summary>
    /// Firebase UID. Nullable for all of Milestone 1: Firebase auth is out of
    /// scope, so the only row is the seeded demo host. Auth backfills this and
    /// the column is then tightened to NOT NULL.
    /// </summary>
    public string? AuthUid { get; set; }

    /// <summary>Shown as EventDetailDto.Host.DisplayName.</summary>
    public string Name { get; set; } = string.Empty;

    public string? Email { get; set; }

    /// <summary>null renders the initials avatar on the client.</summary>
    public string? PhotoUrl { get; set; }

    /// <summary>Authorization within the app - see <see cref="UserRole"/>.
    /// Defaults to Member so a row created by the token-mapping path (which
    /// knows nothing about roles) is an ordinary user, and promoting someone
    /// is an explicit write rather than an accident of insertion order.</summary>
    public UserRole Role { get; set; } = UserRole.Member;

    /// <summary>When this row first appeared, i.e. when the person first signed in
    /// (the token-mapping path creates the row on sight, so there is no separate
    /// sign-up moment to date). The admin user table lists it, and the audit trail
    /// reads alongside it.
    ///
    /// The column has existed in the live schema since before this property did —
    /// it was never mapped or migrated here, which is why the property is
    /// <c>DateTimeOffset</c> with a <c>HasDefaultValue</c> rather than nullable: a
    /// row the column already covers keeps its timestamp, and the default covers any
    /// raw INSERT that omits it.</summary>
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public ICollection<Event> HostedEvents { get; set; } = new List<Event>();

    /// <summary>Drafts owned by this user. Navigation only — the privacy filter runs
    /// on <c>EventDraft.UserId</c> in SQL, never by walking this collection.</summary>
    public ICollection<EventDraft> Drafts { get; set; } = new List<EventDraft>();

    public ICollection<Notification> Notifications { get; set; } = new List<Notification>();
}
