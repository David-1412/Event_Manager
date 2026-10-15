namespace SportMeet.Application.Profiles;

/// <summary>
/// Read models for the public profile at <c>/profile/{userId}</c>.
///
/// Everything here is public by construction, and that is a hard requirement rather
/// than a convention: the profile endpoint is anonymous, so a field added to one of
/// these records is a field disclosed to the whole world. Deliberately absent - email,
/// auth uid, event view counts, anything from EventAnalyticsDto, admin capability, and
/// any participation in a Private event. Adding one of those is a privacy regression,
/// not a feature.
///
/// The profile subject's own id does appear (UserId): the URL already carries it, so it
/// discloses nothing new, and the client needs it so the "this is me" case does not
/// offer to navigate to its own profile.
/// </summary>
/// <param name="UserId">The profile subject's internal id.</param>
/// <param name="DisplayName">The name the person is known by - never their email.</param>
/// <param name="AvatarUrl">Null renders the initials avatar, which is a real state
/// rather than a gap (same convention as ParticipantDto).</param>
/// <param name="Role">The UserRole name. The client maps Moderator to the "Creator"
/// label, in components/admin/role-badge.tsx, so that wording lives in one place and
/// cannot read differently on a profile than on a card. Null renders no badge.</param>
/// <param name="Bio">Free text chosen for public display. Null when unset - there is
/// no server-side default, because "no bio" and "a bio saying nothing" are different
/// states the client should not have to guess between.</param>
/// <param name="OrganizationName">Set for a club or society rather than a natural
/// person, e.g. "Monash Badminton Club". Presentational today; see ProfileKind.</param>
/// <param name="ProfileKind">Whether the profile presents as a club or a person.
/// Always "Person" today: it exists so an organization profile can arrive later as new
/// data rather than a schema redesign. Nothing branches on it yet.</param>
/// <param name="MemberSince">When the account first appeared. Public because it reads
/// as tenure, which is the trust signal the profile exists to give.</param>
/// <param name="Stats">The four Overview numbers.</param>
/// <param name="FollowersCount">How many people follow this profile. Public by design -
/// a follower count is the app's strongest "other people already trust this host"
/// signal, which is the reason the page exists. It counts follows, not engagement, and
/// discloses nothing about any individual follower.</param>
/// <param name="IsFollowing">Whether the *caller* follows this profile. False for an
/// anonymous caller and for a caller who does not follow, which is why the client shows
/// the follow control only once it knows who is asking: with no identity, false is not a
/// fact about the reader, it is the absence of one.</param>
public sealed record ProfileSummaryDto(
    Guid UserId,
    string DisplayName,
    string? AvatarUrl,
    string? Role,
    string? Bio,
    string? OrganizationName,
    string ProfileKind,
    DateTimeOffset MemberSince,
    ProfileStatsDto Stats,
    int FollowersCount = 0,
    bool IsFollowing = false);

/// <summary>
/// The four numbers on the Overview section. Computed server-side in a single aggregate
/// pass (see IProfileRepository.GetStatsAsync) rather than assembled from the per-tab
/// list endpoints the way the account page still does.
/// </summary>
/// <param name="EventsCreated">Events hosted at any point in time, publicly visible
/// ones only. Includes past events: "42 events created" is a record of activity, and
/// restricting it to upcoming would understate an established club.</param>
/// <param name="UpcomingEvents">Events hosted that start from now on. Excludes
/// Cancelled, Draft, PendingReview and Rejected.</param>
/// <param name="InterestedEvents">Public events this user has marked interested.</param>
/// <param name="JoinedEvents">Upcoming public events this user has joined. Public
/// only - a count that quietly included private ones would leak the existence of an
/// event its host chose not to publish.</param>
public sealed record ProfileStatsDto(
    int EventsCreated,
    int UpcomingEvents,
    int InterestedEvents,
    int JoinedEvents);

/// <summary>
/// The body of POST and DELETE <c>/api/profiles/{userId}/follow</c>.
/// </summary>
/// <param name="IsFollowing">The subject's state for the caller <em>after</em> the
/// call, not what the caller asked for. Following someone already followed returns
/// <c>true</c> and unfollowing someone not followed returns <c>false</c>, so a retried
/// tap converges on the truth instead of oscillating - the same reasoning as the
/// interest endpoints, where a double tap must not silently leave the flag inverted.</param>
/// <param name="FollowersCount">The subject's follower count after the change, so the
/// header number settles on the server's value. Recomputing it client-side as ±1 drifts
/// permanently the moment a second device, or a lost retry, moves the real count.</param>
public sealed record ProfileFollowResultDto(bool IsFollowing, int FollowersCount);

/// <summary>
/// One entry in the activity feed. A record rather than an entity because it is a read
/// model assembled across four tables, and a row of a feed rather than anything writable.
/// </summary>
/// <param name="Kind">What the person did, as the name of a ProfileActivityKind. Sent
/// as text for the same reason the other status fields in this API are: the client
/// compares against the string.</param>
/// <param name="EventId">The event the verb points at; the feed links through it.</param>
/// <param name="EventTitle">Denormalised so the feed renders without a second fetch.</param>
/// <param name="OccurredAt">What the verb means by "when" - created_at for Created,
/// start_at for Hosted, the join or interest timestamp for those. Ordering only ever
/// compares this.</param>
public sealed record ProfileActivityDto(
    string Kind,
    Guid EventId,
    string EventTitle,
    DateTimeOffset OccurredAt);

/// <summary>
/// The activity verbs. Ordinals never reach the wire, so the values are free to be
/// reordered; the names are the contract.
/// </summary>
public enum ProfileActivityKind
{
    /// <summary>They created and published the event.</summary>
    Created,

    /// <summary>They hosted an event that has since started - the past-tense twin of
    /// Created, so a completed run still counts as community presence.</summary>
    Hosted,

    /// <summary>They joined someone else's event.</summary>
    Joined,

    /// <summary>They marked interest. Independent of Joined: the join path clears
    /// interest, so an event lands under one verb and not both.</summary>
    Interested,
}
