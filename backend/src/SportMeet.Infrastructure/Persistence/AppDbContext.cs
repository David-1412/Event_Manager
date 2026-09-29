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
    public DbSet<Sport> Sports => Set<Sport>();
    public DbSet<EventParticipant> EventParticipants => Set<EventParticipant>();
    public DbSet<Tag> Tags => Set<Tag>();
    public DbSet<EventTag> EventTags => Set<EventTag>();

    // Email ingestion. Separate from Events by design: no query over events can
    // reach these, which is what keeps unreviewed model output out of the feed.
    public DbSet<IngestedEmail> IngestedEmails => Set<IngestedEmail>();
    public DbSet<EventDraft> EventDrafts => Set<EventDraft>();


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
