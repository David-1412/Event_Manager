using Microsoft.EntityFrameworkCore;
using SportMeet.Domain.Entities;
using SportMeet.Infrastructure.Persistence.Config;
using SportMeet.Domain.Enums;

namespace SportMeet.Infrastructure.Persistence;

/// <summary>
/// Every entity mapping lives in an IEntityTypeConfiguration file rather than
/// here, so the model stays reviewable as later milestones add tables.
///
/// Column and table naming comes from EFCore.NamingConventions
/// (UseSnakeCaseNamingConvention), configured in DependencyInjection: Title ->
/// title, EventParticipant -> event_participants. That is what lets the schema
/// in the migrations be read exactly as written in IMPLEMENTATION_PLAN.md §2 and
/// keeps the camelCase JSON policy a single global setting.
/// </summary>
public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Event> Events => Set<Event>();
    public DbSet<User> Users => Set<User>();
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<Sport> Sports => Set<Sport>();
    public DbSet<EventParticipant> EventParticipants => Set<EventParticipant>();
    public DbSet<EventInterest> EventInterests => Set<EventInterest>();

    /// <summary>Join requests for events with approval turned on. A decision
    /// record, never the source of truth for membership - that stays
    /// <see cref="EventParticipants"/>.</summary>
    public DbSet<EventJoinRequest> EventJoinRequests => Set<EventJoinRequest>();
    public DbSet<Tag> Tags => Set<Tag>();
    public DbSet<EventTag> EventTags => Set<EventTag>();

    // Email ingestion. Separate from Events by design: no query over events can
    // reach these, which is what keeps unreviewed model output out of the feed.
    public DbSet<IngestedEmail> IngestedEmails => Set<IngestedEmail>();
    public DbSet<EventDraft> EventDrafts => Set<EventDraft>();

    // Paste-to-event import: what was proposed, what was published, how long it took.
    public DbSet<EventImport> EventImports => Set<EventImport>();
    public DbSet<EventPublishMetric> EventPublishMetrics => Set<EventPublishMetric>();

    // Append-only trail of privilege changes. Deliberately not reachable from any
    // event or draft query: an audit line is not data the product filters over.
    public DbSet<UserRoleAudit> UserRoleAudit => Set<UserRoleAudit>();

    /// <summary>Detail-page openings, one row per view. Append-only and never read
    /// by the feed: the host's view count is a COUNT over these rows, computed only
    /// behind the host check the analytics endpoint performs.</summary>
    public DbSet<EventView> EventViews => Set<EventView>();

    /// <summary>Directed "A follows B" edges. Read by the profile's follower count and
    /// by the fan-out that tells followers about a newly published event; never read by
    /// the feed.</summary>
    public DbSet<UserFollow> UserFollows => Set<UserFollow>();


    /// <summary>Read-only projection over v_event_feed. Never in SaveChanges -
    /// EF treats keyless entity types as read-only, and no DbSet of it is exposed
    /// so it cannot be written by accident.</summary>
    internal DbSet<VwEventFeed> EventFeed => Set<VwEventFeed>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Declares sportsmeet.haversine_km to EF. Without it the distance
        // ordering evaluates in memory and AppDbFunctions' throw fires - a loud
        // failure is the point, since the alternative is a wrong-slow query.
        modelBuilder.HasDbFunction(
            typeof(AppDbFunctions).GetMethod(nameof(AppDbFunctions.HaversineKm))!,
            f => f.HasName("haversine_km").HasSchema(SportConfiguration.Schema));

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
    }
}
