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

    /// <summary>The Following feed: browse narrowed to events hosted by users the
    /// current viewer follows, with every browse filter still honoured.
    ///
    /// Throws <see cref="DomainRuleException"/> when nobody is signed in — the
    /// feed is defined by the viewer's follow graph, so an anonymous answer would
    /// be a lie rather than an empty list. The Api layer turns that into a
    /// refusal the browse page shows as a sign-in prompt.</summary>
    Task<PagedResult<EventListItemDto>> ListFollowingAsync(EventQueryModel query, CancellationToken ct = default);

    /// <summary>Throws <see cref="NotFoundException"/> when the id is unknown,
    /// which the Api layer turns into 404 + code "NotFound".</summary>
    Task<EventDetailDto> GetAsync(Guid id, CancellationToken ct = default);

    /// <summary>Throws <see cref="DomainRuleException"/> for an unknown sport slug.</summary>
    Task<EventDetailDto> CreateAsync(CreateEventDto dto, CancellationToken ct = default);

    /// <summary>Updates an event owned by the current user. Public events edited
    /// by Members return to admin review; Moderators and Admins may publish their
    /// own edits immediately.</summary>
    Task<EventDetailDto> UpdateHostedAsync(Guid eventId, UpdateAdminEventRequest request, CancellationToken ct = default);

    /// <summary>Throws <see cref="NotFoundException"/> when the event is unknown,
    /// <see cref="EventFullException"/> when capacity is reached (409 EventFull),
    /// or <see cref="DomainRuleException"/> for the host, a cancelled event, or
    /// one that has already started.</summary>
    Task<EventDetailDto> JoinAsync(Guid eventId, CancellationToken ct = default);

    /// <summary>Throws <see cref="NotFoundException"/> when the event is unknown;
    /// the host leaving cancels the event instead of leaving it hostless.
    /// Leaving while unjoined is a no-op so the client's retry loop converges.</summary>
    Task<EventDetailDto> LeaveAsync(Guid eventId, CancellationToken ct = default);

    /// <summary>The host's Join Requests queue for one event. Host-only: a caller who
    /// does not own the event gets 404. Pending requests only unless
    /// <paramref name="includeDecided"/> asks for the full history.</summary>
    Task<JoinRequestListDto> ListJoinRequestsAsync(
        Guid eventId, bool includeDecided, CancellationToken ct = default);

    /// <summary>Approve a pending request, which adds that member as a participant in
    /// the same transaction and notifies them. Host-only; approving your own request
    /// is refused. Throws <see cref="EventFullException"/> (409) when no spot is
    /// left, leaving the request pending.</summary>
    Task<JoinRequestListDto> ApproveJoinRequestAsync(
        Guid eventId, Guid userId, CancellationToken ct = default);

    /// <summary>Reject a pending request. The member is not added and is notified.
    /// Host-only; rejecting your own request is refused.</summary>
    Task<JoinRequestListDto> RejectJoinRequestAsync(
        Guid eventId, Guid userId, CancellationToken ct = default);

    /// <summary>Event ids the current viewer has joined. Empty (not an error) when
    /// no identity is configured, so My events degrades to the Interested list.</summary>
    Task<IReadOnlyList<Guid>> ListMyJoinedAsync(CancellationToken ct = default);

    /// <summary>Event ids with a pending join request from the current viewer, for
    /// the browse card's "Request submitted" state. Empty (not an error) when no
    /// identity is configured, mirroring ListMyJoinedAsync.</summary>
    Task<IReadOnlyList<Guid>> ListMyPendingJoinRequestsAsync(CancellationToken ct = default);

    /// <summary>Add or remove the current viewer's interest in one event (toggle).
    /// Throws <see cref="DomainRuleException"/> when no identity is configured and
    /// <see cref="NotFoundException"/> when the event is unknown. Returns the new
    /// state (true = now interested).</summary>
    Task<bool> ToggleInterestAsync(Guid eventId, CancellationToken ct = default);

    /// <summary>
    /// Mark the caller Interested because they just opened the event for the first time
    /// (the caller decides "first" - see EventAnalyticsService.TryRecordViewAsync).
    /// Returns whether an interest was added. Default-implemented so test doubles of
    /// this interface need not know about it.
    /// </summary>
    Task<bool> TryAutoInterestAsync(EventDetailDto detail, CancellationToken ct = default) =>
        Task.FromResult(false);

    /// <summary>Event ids the current viewer has marked interested. Empty (not an
    /// error) when no identity is configured, mirroring ListMyJoinedAsync.</summary>
    Task<IReadOnlyList<Guid>> ListMyInterestedAsync(CancellationToken ct = default);

    /// <summary>All events hosted by the current user, including cancelled and past events.</summary>
    Task<IReadOnlyList<EventListItemDto>> ListMyHostedAsync(CancellationToken ct = default);

    Task<EventDetailDto> CancelAsync(Guid eventId, CancellationToken ct = default);

    Task<EventDetailDto> ReopenAsync(Guid eventId, CancellationToken ct = default);

    /// <summary>Delete one of your own events once it has been cancelled. Soft: the row
    /// keeps its history but disappears from every list and link.</summary>
    Task DeleteCancelledAsync(Guid eventId, CancellationToken ct = default) =>
        throw new NotSupportedException();

    /// <summary>The review queue: every public event awaiting an administrator's
    /// decision, oldest submission first. Throws <see cref="DomainRuleException"/>
    /// when nobody is signed in and <see cref="NotFoundException"/> for a caller
    /// without the admin role - the same 404 the detail endpoint gives for a row the
    /// caller may not see, so the queue does not advertise that it exists.</summary>
    Task<IReadOnlyList<EventListItemDto>> ListPendingReviewAsync(CancellationToken ct = default);

    /// <summary>Approve a submitted event: PendingReview (or a resubmitted Rejected)
    /// becomes Published, which puts it in the public feed on the next request.
    /// Throws <see cref="NotFoundException"/> for a non-admin caller or an event that
    /// is not awaiting a decision.</summary>
    Task<EventDetailDto> ApproveAsync(Guid eventId, CancellationToken ct = default);

    /// <summary>Reject a submitted event. It stays readable by its creator - who can
    /// edit and resubmit it - and never appears in the public feed. Same refusals as
    /// <see cref="ApproveAsync"/>.</summary>
    Task<EventDetailDto> RejectAsync(Guid eventId, CancellationToken ct = default);

    /// <summary>Most-used tags on currently-visible events, for the home page's
    /// filter chips. Empty on a fresh database (the demo seed creates no tags),
    /// which the client renders as an empty state rather than a stray divider.</summary>
    Task<IReadOnlyList<PopularTag>> ListPopularTagsAsync(int limit = 12, CancellationToken ct = default);

    /// <summary>Existing tag names starting with the normalized prefix, for the
    /// create form's autocomplete. Free entry is always allowed regardless of what
    /// this returns.</summary>
    Task<IReadOnlyList<string>> SuggestTagsAsync(string? prefix, int limit = 8, CancellationToken ct = default);
}
