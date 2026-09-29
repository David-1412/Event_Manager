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
        b.Property(x => x.Status).HasConversion<string>();
    }
}
