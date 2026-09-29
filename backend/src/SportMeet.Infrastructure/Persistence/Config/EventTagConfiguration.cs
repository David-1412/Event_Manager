using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SportMeet.Domain.Entities;

namespace SportMeet.Infrastructure.Persistence.Config;

public sealed class EventTagConfiguration : IEntityTypeConfiguration<EventTag>
{
    public void Configure(EntityTypeBuilder<EventTag> b)
    {
        b.ToTable("event_tags", SportConfiguration.Schema);

        b.HasKey(x => new { x.EventId, x.TagId });

        // Cascade both ways: deleting an event must not orphan join rows, and a
        // tag is meaningless without the events it labels. Tag rows are not
        // auto-reclaimed when the last one goes (curation was ruled out), so the
        // popularity query is what makes a stale tag invisible rather than the
        // row being deleted.
        b.HasOne(x => x.Event)
            .WithMany(e => e.EventTags)
            .HasForeignKey(x => x.EventId)
            .OnDelete(DeleteBehavior.Cascade);

        b.HasOne(x => x.Tag)
            .WithMany(t => t.EventTags)
            .HasForeignKey(x => x.TagId)
            .OnDelete(DeleteBehavior.Cascade);

        // The popularity query groups by tag over scheduled, future events, and
        // the tag-filter join drives the filtered browse page; both read through
        // this index rather than the primary key, whose leading column is event_id.
        b.HasIndex(x => x.TagId).HasDatabaseName("event_tags_tag_id_idx");
    }
}
