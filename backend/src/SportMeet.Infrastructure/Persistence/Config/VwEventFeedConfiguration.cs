using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace SportMeet.Infrastructure.Persistence.Config;

public sealed class VwEventFeedConfiguration : IEntityTypeConfiguration<VwEventFeed>
{
    public void Configure(EntityTypeBuilder<VwEventFeed> b)
    {
        // ToView (rather than ToTable) marks this as non-mapped: EF emits no DDL
        // for it, so `dotnet ef migrations remove` cannot drop the hand-written
        // view. HasNoKey is required because a view has nothing to key on, and it
        // also makes the type read-only so it can never reach SaveChanges.
        b.ToView("v_event_feed", SportConfiguration.Schema);
        b.HasNoKey();

        // The view's enum columns are text for the same reason the table's are;
        // without these the string-to-enum conversion silently mismatches.
        b.Property(x => x.SkillLevel).HasConversion<string>();

        // Status stays text on this projection - no conversion - because the browse
        // filter needs `NOT IN ('Draft','PendingReview','Rejected')`, which EF can
        // only translate against the column's real type. VwEventFeed documents the
        // 42883 that a converted Contains produces; ToEvent parses it back.
        b.Property(x => x.Status).HasMaxLength(20);

        // The host's role, carried so the read paths can tell an administrator who
        // is allowed to open an unpublished event from anyone else holding the id.
        // Text on the view, and never used as a query predicate - the repository
        // filters on visibility/status columns and joins the host by id - so it
        // needs no value conversion and no index.
        b.Property(x => x.HostRole).HasMaxLength(20);
    }
}
