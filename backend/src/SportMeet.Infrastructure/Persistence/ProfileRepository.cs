using Microsoft.EntityFrameworkCore;
using Npgsql;
using SportMeet.Application.Events;
using SportMeet.Application.Profiles;
using SportMeet.Domain.Entities;
using SportMeet.Domain.Enums;

namespace SportMeet.Infrastructure.Persistence;

/// <summary>
/// Public-profile reads over <c>v_event_feed</c> plus the participant and interest
/// tables.
/// </summary>
/// <remarks>
/// Status and visibility are written as <em>exclusion</em> sets against the view's text
/// columns, mirroring <see cref="EventRepository"/>'s ApplyFilters rather than importing
/// it. Duplicating a three-element list is the deliberate trade: the alternative is a
/// shared IQueryable filter that browse depends on, and browse's predicate is the one in
/// this codebase whose exact behaviour is load-bearing. Failing open on a profile is
/// bad; failing open on the feed is worse. Both copies cite the same reason, so drift is
/// visible rather than silent.
///
/// Compared as text, not enum, for the reason recorded on VwEventFeed.Status: the view
/// exposes these as varchar and EF binds an enum Contains as integers, which Postgres
/// rejects with 42883.
/// </remarks>
public sealed class ProfileRepository(AppDbContext db) : IProfileRepository
{
    /// <summary>Statuses that are never publicly visible. The excluded set, so a status
    /// added later fails closed and stays off every profile until this line changes.</summary>
    internal static readonly string[] UnpublishedStatuses =
    [
        nameof(EventStatus.Draft),
        nameof(EventStatus.PendingReview),
        nameof(EventStatus.Rejected),
    ];

    /// <summary>Rows that may appear on any public surface: not cancelled, not awaiting
    /// or denied review. Soft deletes need no clause - the view ends in
    /// <c>WHERE e.deleted_at IS NULL</c>.</summary>
    private IQueryable<VwEventFeed> PublicFeed =>
        db.EventFeed.AsNoTracking()
            .Where(v => v.Status != nameof(EventStatus.Cancelled))
            .Where(v => !UnpublishedStatuses.Contains(v.Status));

    public async Task<ProfileSubject?> FindSubjectAsync(Guid userId, CancellationToken ct = default) =>
        // Projected rather than loaded: the User entity carries Email and AuthUid, so
        // selecting only the columns a profile may show means a future property on User
        // cannot quietly widen what an anonymous endpoint knows about someone.
        await db.Users.AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => new ProfileSubject(
                u.Id, u.Name, u.PhotoUrl, u.Role.ToString(), u.CreatedAt))
            .FirstOrDefaultAsync(ct);

    public async Task<ProfileStatsDto> GetStatsAsync(Guid userId, CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;

        // Both hosted numbers from one pass. Two counts would read the same rows twice
        // for the query the profile's first paint waits on.
        var hostedStarts = await PublicFeed
            .Where(v => v.HostId == userId)
            .Select(v => v.StartAt)
            .ToListAsync(ct);

        var eventsCreated = hostedStarts.Count;
        var upcomingEvents = hostedStarts.Count(start => start >= now);

        // Joined: upcoming, and Public only. The visibility test is the privacy rule -
        // nobody, not even this person's own profile, may confirm a private event exists.
        // Distinct because a participant row joins the view per event; the join cannot
        // multiply, but the intent is "how many events", and saying so costs nothing.
        var joinedEvents = await (
            from p in db.EventParticipants.AsNoTracking()
            join v in PublicFeed on p.EventId equals v.Id
            where p.UserId == userId && v.StartAt >= now
                  && v.Visibility == nameof(EventVisibility.Public)
            select v.Id)
            .Distinct()
            .CountAsync(ct);

        // Interested: not bounded by start date. Interest is a standing statement about
        // an event, and the account page's own Interested tile counts it the same way -
        // the two numbers must agree or the public profile contradicts the account page.
        var interestedEvents = await (
            from i in db.EventInterests.AsNoTracking()
            join v in PublicFeed on i.EventId equals v.Id
            where i.UserId == userId && v.Visibility == nameof(EventVisibility.Public)
            select v.Id)
            .Distinct()
            .CountAsync(ct);

        return new ProfileStatsDto(eventsCreated, upcomingEvents, interestedEvents, joinedEvents);
    }

    public async Task<List<FeedRow>> ListPublicHostedAsync(Guid userId, CancellationToken ct = default)
    {
        var rows = await PublicFeed
            .Where(v => v.HostId == userId)
            // Newest first: a profile reads as a portfolio, and the most recent work is
            // what a visitor is judging the host on.
            .OrderByDescending(v => v.StartAt)
            .ToListAsync(ct);

        return rows.Select(EventRepository.ToRow).ToList();
    }

    public async Task<List<FeedRow>> ListPublicUpcomingAsync(Guid userId, CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;

        var rows = await PublicFeed
            .Where(v => v.HostId == userId && v.StartAt >= now)
            // Soonest first - this is the "turn up to their event" list, so the next one
            // leads. Opposite direction to ListPublicHostedAsync, on purpose.
            .OrderBy(v => v.StartAt)
            .ToListAsync(ct);

        return rows.Select(EventRepository.ToRow).ToList();
    }

    public async Task<List<FeedRow>> ListPublicJoinedUpcomingAsync(Guid userId, CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;

        var rows = await (
            from p in db.EventParticipants.AsNoTracking()
            join v in PublicFeed on p.EventId equals v.Id
            where p.UserId == userId && v.StartAt >= now
                  && v.Visibility == nameof(EventVisibility.Public)
            orderby v.StartAt
            select v)
            .ToListAsync(ct);

        return rows.Select(EventRepository.ToRow).ToList();
    }

    public async Task<List<ProfileActivityDto>> GetActivityAsync(
        Guid userId, int limit, CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;

        // Four sources merged in the database, so the ordering and the cap happen in
        // SQL. Merging four materialised lists in memory would be the obvious version
        // and would read every row the account has ever touched to display a dozen.
        //
        // Hosted splits on start_at: an event still ahead reads as "Created", one
        // already run as "Hosted". Same row, two verbs, and the split is what lets a
        // club's completed sessions show as community presence without listing them
        // twice - the two branches are disjoint by construction.
        //
        // Every branch must project to the mapped entity VwEventFeed and nothing else.
        // Npgsql's TranslateConcat calls SelectExpression.ApplyUnion, which rejects a
        // set operation whose branches have been projected to any client type - not
        // only to an unmapped class, but to an anonymous type as well, both measured
        // against the real database. Projecting to VwEventFeed is what keeps each
        // branch a shaped query over the view, so the four compose into one UNION and
        // the ORDER BY and LIMIT land in SQL. Only the columns the feed needs are
        // selected; the rest stay null and are never read.
        //
        // The verb rides in HostRole and the moment the entry reports in CreatedAt,
        // which is why neither is read as itself below. Those two columns are the
        // transport, and the record is built after the cap so the database does the
        // sorting rather than the process.
        var created = PublicFeed
            .Where(v => v.HostId == userId && v.StartAt >= now)
            .Select(v => new VwEventFeed
            {
                Id = v.Id,
                Title = v.Title,
                CreatedAt = v.CreatedAt,
                HostRole = nameof(ProfileActivityKind.Created),
            });

        var hosted = PublicFeed
            .Where(v => v.HostId == userId && v.StartAt < now)
            .Select(v => new VwEventFeed
            {
                Id = v.Id,
                Title = v.Title,
                CreatedAt = v.StartAt,
                HostRole = nameof(ProfileActivityKind.Hosted),
            });

        var joined =
            from p in db.EventParticipants.AsNoTracking()
            join v in PublicFeed on p.EventId equals v.Id
            where p.UserId == userId && v.Visibility == nameof(EventVisibility.Public)
            select new VwEventFeed
            {
                Id = v.Id,
                Title = v.Title,
                CreatedAt = p.JoinedAt,
                HostRole = nameof(ProfileActivityKind.Joined),
            };

        // Interests marked automatically by opening an event are not shown: listing them
        // would publish everything the person ever looked at. Only a tap is a statement.
        var interested =
            from i in db.EventInterests.AsNoTracking()
            join v in PublicFeed on i.EventId equals v.Id
            where i.UserId == userId && !i.IsAuto && v.Visibility == nameof(EventVisibility.Public)
            select new VwEventFeed
            {
                Id = v.Id,
                Title = v.Title,
                CreatedAt = i.CreatedAt,
                HostRole = nameof(ProfileActivityKind.Interested),
            };

        return await created
            .Concat(hosted)
            .Concat(joined)
            .Concat(interested)
            // Newest first, then by event id, so two entries sharing a timestamp keep a
            // stable order across reloads instead of shuffling under the reader.
            .OrderByDescending(v => v.CreatedAt)
            .ThenBy(v => v.Id)
            .Take(limit)
            .Select(v => new ProfileActivityDto(
                v.HostRole, v.Id, v.Title, v.CreatedAt))
            .ToListAsync(ct);
    }

    public async Task<int> CountFollowersAsync(Guid userId, CancellationToken ct = default) =>
        await db.UserFollows.AsNoTracking().CountAsync(f => f.FollowedUserId == userId, ct);

    public async Task<bool> IsFollowingAsync(
        Guid userId, Guid followerId, CancellationToken ct = default) =>
        await db.UserFollows.AsNoTracking()
            .AnyAsync(f => f.FollowedUserId == userId && f.FollowerUserId == followerId, ct);

    public async Task<bool> AddFollowAsync(
        Guid followerUserId, Guid followedUserId, DateTimeOffset createdAt,
        CancellationToken ct = default)
    {
        db.UserFollows.Add(new UserFollow
        {
            FollowerUserId = followerUserId,
            FollowedUserId = followedUserId,
            CreatedAt = createdAt,
        });

        try
        {
            await db.SaveChangesAsync(ct);
            return true;
        }
        catch (DbUpdateException ex) when (IsDuplicateKey(ex))
        {
            // Already following. The composite key made the decision, and the honest
            // reading of "you already did this" is success, not an error - so the
            // service reports the resulting state rather than a failure.
            //
            // The inner-exception walk is deliberate rather than a broad catch:
            // Npgsql surfaces Postgres' 23505 here, and a catch that swallowed every
            // DbUpdateException would also swallow the foreign-key failure from a
            // deleted account and the self-follow CHECK, reporting both as "you are now
            // following them".
            db.Entry(db.UserFollows.Local.Single()).State = EntityState.Detached;
            return false;
        }
    }

    public async Task<bool> RemoveFollowAsync(
        Guid followerUserId, Guid followedUserId, CancellationToken ct = default)
    {
        // Set-based rather than find-then-remove: one statement, and the row never
        // enters the change tracker, so a concurrent unfollow on another device cannot
        // turn into a second failing SaveChanges.
        var removed = await db.UserFollows
            .Where(f => f.FollowerUserId == followerUserId && f.FollowedUserId == followedUserId)
            .ExecuteDeleteAsync(ct);

        return removed > 0;
    }

    public async Task<List<Guid>> ListFollowersAsync(
        Guid followedUserId, CancellationToken ct = default) =>
        await db.UserFollows.AsNoTracking()
            .Where(f => f.FollowedUserId == followedUserId)
            .Select(f => f.FollowerUserId)
            .ToListAsync(ct);

    public async Task<(IReadOnlyList<Guid> FollowerIds, string? HostName)> ListFollowersToNotifyAsync(
        Event published, CancellationToken ct = default)
    {
        // The eligibility rule as a query, so an ineligible event yields no rows rather
        // than relying on a caller to have noticed. Written against the entity here
        // rather than against v_event_feed's text columns: this reads the row that was
        // just written inside the same transaction, and the view is a separate read that
        // a pending publish has not necessarily reflected.
        if (published.Visibility != EventVisibility.Public || !published.Status.IsPublished())
        {
            return ([], null);
        }

        // One projection for both halves. The host's name is read from the same pass as
        // the follower ids so the message can name them without a second round-trip.
        var rows = await db.UserFollows.AsNoTracking()
            .Where(f => f.FollowedUserId == published.HostId && f.FollowerUserId != published.HostId)
            .Select(f => new FollowerRow(f.FollowerUserId, f.FollowedUser.Name))
            .ToListAsync(ct);

        if (rows.Count == 0)
        {
            return ([], null);
        }

        return (rows.Select(r => r.FollowerUserId).ToList(), rows[0].HostName);
    }

    /// <summary>True when an EF write failed on a unique constraint rather than on
    /// something that ought to surface. Postgres reports 23505 for any unique
    /// violation, so this is "a constraint said no", not specifically "the primary key
    /// said no" - which is precise enough here, because the only unique constraint on
    /// user_follows is the one whose duplicate is being handled.</summary>
    private static bool IsDuplicateKey(DbUpdateException ex) =>
        ex.InnerException is PostgresException pg && pg.SqlState == PostgresErrorCodes.UniqueViolation;

    /// <summary>A follower id paired with the followed user's name, so one query
    /// returns both halves of what the fan-out needs. Private to this class.</summary>
    private sealed record FollowerRow(Guid FollowerUserId, string? HostName);
}
