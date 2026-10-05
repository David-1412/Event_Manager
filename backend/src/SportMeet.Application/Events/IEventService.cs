using SportMeet.Application.Common;

namespace SportMeet.Application.Events;

/// <summary>
/// The three Milestone-1 operations. Everything the read side needs comes from
/// here, which is what lets the browse/detail endpoints stay anonymous while
/// the write side still resolves an identity through ICurrentUser.
/// </summary>
public interface IEventService
{
    Task<PagedResult<EventListItemDto>> ListAsync(EventQueryModel query, CancellationToken ct = default);

    /// <summary>Throws <see cref="NotFoundException"/> when the id is unknown,
    /// which the Api layer turns into 404 + code "NotFound".</summary>
    Task<EventDetailDto> GetAsync(Guid id, CancellationToken ct = default);

    /// <summary>Throws <see cref="DomainRuleException"/> for an unknown sport slug.</summary>
    Task<EventDetailDto> CreateAsync(CreateEventDto dto, CancellationToken ct = default);

    /// <summary>Throws <see cref="NotFoundException"/> when the event is unknown,
    /// <see cref="EventFullException"/> when capacity is reached (409 EventFull),
    /// or <see cref="DomainRuleException"/> for the host, a cancelled event, or
    /// one that has already started.</summary>
    Task<EventDetailDto> JoinAsync(Guid eventId, CancellationToken ct = default);

    /// <summary>Throws <see cref="NotFoundException"/> when the event is unknown;
    /// the host leaving cancels the event instead of leaving it hostless.
    /// Leaving while unjoined is a no-op so the client's retry loop converges.</summary>
    Task<EventDetailDto> LeaveAsync(Guid eventId, CancellationToken ct = default);

    /// <summary>Event ids the current viewer has joined. Empty (not an error) when
    /// no identity is configured, so My events degrades to the Interested list.</summary>
    Task<IReadOnlyList<Guid>> ListMyJoinedAsync(CancellationToken ct = default);

    /// <summary>All events hosted by the current user, including cancelled and past events.</summary>
    Task<IReadOnlyList<EventListItemDto>> ListMyHostedAsync(CancellationToken ct = default);

    Task<EventDetailDto> CancelAsync(Guid eventId, CancellationToken ct = default);

    Task<EventDetailDto> ReopenAsync(Guid eventId, CancellationToken ct = default);

    /// <summary>Most-used tags on currently-visible events, for the home page's
    /// filter chips. Empty on a fresh database (the demo seed creates no tags),
    /// which the client renders as an empty state rather than a stray divider.</summary>
    Task<IReadOnlyList<PopularTag>> ListPopularTagsAsync(int limit = 12, CancellationToken ct = default);

    /// <summary>Existing tag names starting with the normalized prefix, for the
    /// create form's autocomplete. Free entry is always allowed regardless of what
    /// this returns.</summary>
    Task<IReadOnlyList<string>> SuggestTagsAsync(string? prefix, int limit = 8, CancellationToken ct = default);
}
