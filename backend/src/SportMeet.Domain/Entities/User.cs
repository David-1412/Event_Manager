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

    public ICollection<Event> HostedEvents { get; set; } = new List<Event>();

    /// <summary>Drafts owned by this user. Navigation only — the privacy filter runs
    /// on <c>EventDraft.UserId</c> in SQL, never by walking this collection.</summary>
    public ICollection<EventDraft> Drafts { get; set; } = new List<EventDraft>();
}

