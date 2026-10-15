namespace SportMeet.Application.Notifications;

/// <summary>
/// The values written to <see cref="Domain.Entities.Notification.Type"/>.
///
/// The column is free text with no CHECK constraint, so nothing in the database
/// stops a typo - and a typo is invisible: the row still saves, still appears in
/// the inbox, and only ever reads wrong if the client specialises in that type.
/// Centralised here so a caller cannot invent a spelling.
///
/// Existing types already stored in the table (<c>EventApproved</c>,
/// <c>JoinRequestSubmitted</c>, <c>JoinMilestone</c>, ...) are deliberately not
/// moved: renaming a stored value is a data migration, and the point here is to
/// keep new writes consistent, not to churn ones that work.
/// </summary>
public static class NotificationTypes
{
    /// <summary>Sent to approved participants 24 hours before the event starts.</summary>
    public const string EventStartingTomorrow = "EventStartingTomorrow";

    /// <summary>Sent to approved participants one hour before the event starts.</summary>
    public const string EventStartingSoon = "EventStartingSoon";

    /// <summary>Sent to a creator's followers when one of their events becomes publicly
    /// visible. Fires on the transition into visibility, so it is written for a public
    /// event published straight through by a creator and for one released by an admin's
    /// approval - and never for a Draft, PendingReview or Rejected event, which are not
    /// publicly visible and have nothing to announce.</summary>
    public const string FollowedCreatorPublished = "FollowedCreatorPublished";
}
