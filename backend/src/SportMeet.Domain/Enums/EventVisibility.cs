namespace SportMeet.Domain.Enums;

/// <summary>
/// Who can discover an event. This is a *discovery* rule, not an access rule:
/// it decides whether the event appears in the browse feed, never whether a
/// given caller may open it.
///
///  - <see cref="Public"/> is the default and the only value the browse query
///    returns, so a public event is reachable both from `/` and by its link.
///  - <see cref="Private"/> is deliberately *not* hidden from
///    <c>GET /api/events/{id}</c>: the whole point of a private event is that
///    the host can send someone the link and that person can open it and join.
///    Anyone holding the link can reach it; nobody can *find* it without one.
///
/// Persisted as its CLR name (see EventConfiguration), matching the enum-as-text
/// convention every other status column in this schema follows.
/// </summary>
public enum EventVisibility
{
    Public,
    Private,
}
