namespace SportMeet.Application.Common;

/// <summary>
/// Thrown when a row genuinely does not exist. Maps to 404 + code "NotFound".
/// Id is carried so the response can name the id the client asked for - the
/// alternative is a placeholder Guid that would be worse than saying nothing.
/// </summary>
public sealed class NotFoundException(string resource, object key, Guid? id = null)
    : Exception($"{resource} '{key}' was not found.")
{
    public Guid? Id { get; } = id;
}

/// <summary>
/// A violated business rule that the client could not have validated away, e.g.
/// an unknown sport slug. Maps to 422 with a single field-less message so the
/// create form's field-mapping path stays reserved for real field errors.
/// </summary>
public sealed class DomainRuleException : Exception
{
    public DomainRuleException(string message)
        : base(message)
    {
    }
}

/// <summary>
/// The event was full at the moment of joining. Distinct from
/// <see cref="DomainRuleException"/> because JoinButton branches on it
/// (409 + code "EventFull" -> "This event just filled up", spec §7).
/// </summary>
public sealed class EventFullException : Exception
{
    public EventFullException(Guid eventId)
        : base($"Event '{eventId}' is full.")
    {
        EventId = eventId;
    }

    public Guid EventId { get; }
}

