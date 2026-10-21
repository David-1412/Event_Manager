using Microsoft.Extensions.Logging;
using SportMeet.Application.Common;

namespace SportMeet.Application.Events;

/// <summary>
/// View tracking and the host's analytics for one event.
///
/// Kept out of <see cref="EventService"/> for a reason beyond size. Recording a
/// view happens on the detail read, which is anonymous, high-traffic and allowed to
/// fail; the analytics read happens on a host-only panel, which is authenticated and
/// must not. Those two have opposite failure requirements, and the first must never
/// be able to break the second - so the detail page calls in through a method that
/// swallows, and the panel calls in through one that does not.
/// </summary>
public sealed class EventAnalyticsService(
    IEventViewRepository views,
    IEventRepository events,
    ICurrentUser currentUser,
    ILogger<EventAnalyticsService> logger)
{
    /// <summary>Called when an event's detail page is opened.
    ///
    /// Deliberately best-effort, and deliberately not a reason to fail the page.
    /// A view is a statistic; the event is the thing the visitor came for. An
    /// analytics insert that throws over a transient database fault must not turn a
    /// readable event into an error screen, so nothing here propagates - the failure
    /// is logged here, where the reason for it is still known.
    /// </summary>
    /// <summary>
    /// Records the view, and returns whether it was the signed-in viewer's first ever
    /// view of this event - the moment Interested is marked automatically. False for
    /// anonymous viewers, repeat views, and any failure to record.
    ///
    /// "First ever", not "not interested yet": someone who un-marks Interested and
    /// comes back must not be re-marked, or turning it off would never stick.
    /// </summary>
    public async Task<bool> TryRecordViewAsync(Guid eventId, CancellationToken ct = default)
    {
        try
        {
            // The identity is read here rather than passed by the caller so that
            // "who viewed it" is decided in one place. ICurrentUser is the same
            // seam the rest of the app reads, so a view is attributed to exactly
            // the person the rest of the request acts as - and an anonymous
            // visitor arrives as null, which is what the nullable column is for.
            var recorded = await views.RecordViewAsync(eventId, currentUser.UserId, DateTimeOffset.UtcNow, ct);
            return recorded.IsNew
                   && currentUser.UserId is { } viewer
                   && await views.CountViewsByUserAsync(eventId, viewer, ct) == 1;
        }
        catch (Exception ex)
        {
            // Two very different things arrive here as a cancellation, and telling
            // them apart is the difference between an empty log and one full of
            // non-events. A visitor navigating away aborts the command, which surfaces
            // as a TaskCanceledException carrying the *command's* token, not this
            // request's - so `ct.IsCancellationRequested` is false for it, and a filter
            // on that flag would file every quick visitor as a tracking failure. The
            // token is therefore inspected as the request proceeds rather than assumed
            // from the exception's type.
            if (ct.IsCancellationRequested || IsCommandAbort(ex))
            {
                logger.LogDebug("View of event {EventId} was not recorded: the request was cancelled.", eventId);
                return false;
            }

            logger.LogWarning(ex, "Could not record a view of event {EventId}.", eventId);
            return false;
        }
    }

    /// <summary>Whether an exception is the database giving up on a command that was
    /// asked to stop, rather than a database that is actually broken. Npgsql raises
    /// <see cref="TaskCanceledException"/> with its own token when a command is
    /// aborted, so the exception's token is the one that says why.</summary>
    private static bool IsCommandAbort(Exception ex) =>
        ex is TaskCanceledException { CancellationToken: { IsCancellationRequested: true } }
        || (ex.InnerException is not null && ex.InnerException != ex && IsCommandAbort(ex.InnerException));

    /// <summary>The host's numbers for one event.
    ///
    /// <b>Ownership is the whole authorization rule</b> and it is resolved from the
    /// event row rather than from anything the client sends: a caller may name an
    /// event id, but never a host id. A caller who is not the host gets
    /// <see cref="NotFoundException"/> rather than a refusal that says "not yours",
    /// for the reason the detail read already gives for an unpublished event -
    /// confirming that the event exists and that someone else owns it is
    /// information, and there is nothing a legitimate non-host needs instead.</summary>
    public async Task<EventAnalyticsDto> GetAsync(Guid eventId, CancellationToken ct = default)
    {
        var userId = currentUser.UserId
            ?? throw new DomainRuleException("You must be signed in to view event analytics.");

        var row = await events.FindAsync(eventId, ct)
            ?? throw new NotFoundException("Event", eventId, eventId);

        if (row.Event.HostId != userId)
        {
            throw new NotFoundException("Event", eventId, eventId);
        }

        // Counted here rather than read from the feed row: the view's derived
        // counts are exactly these two, and taking them from `row` would mean the
        // analytics number and the public number could drift if one read came from
        // the view and the other from a COUNT. Both come from the view now.
        return new EventAnalyticsDto(
            Views: await views.CountViewsAsync(eventId, ct),
            JoinedCount: row.ParticipantCount,
            InterestedCount: row.InterestedCount);
    }
}
