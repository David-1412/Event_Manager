using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SportMeet.Domain.Entities;

namespace SportMeet.Infrastructure.Persistence.Config;

public sealed class EventInterestConfiguration : IEntityTypeConfiguration<EventInterest>
{
    public void Configure(EntityTypeBuilder<EventInterest> b)
    {
        b.ToTable("event_interests", SportConfiguration.Schema);

        // Composite key: one row per (event, person), which is also the
        // "cannot be interested twice" guard the toggle endpoint leans on —
        // the same idempotency shape as event_participants.
        b.HasKey(x => new { x.EventId, x.UserId });

        b.Property(x => x.CreatedAt).IsRequired().HasColumnName("interested_at");

        b.HasOne(x => x.Event).WithMany().HasForeignKey(x => x.EventId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);

        // Covers the "interested events for this user" read (the Interested tab
        // and the batched isInterested flag), the only query that filters on the
        // user side. The event side is already served by the composite key's
        // leading column, so the per-event COUNT in v_event_feed needs no index.
        b.HasIndex(x => x.UserId).HasDatabaseName("ix_event_interests_user_id");
    }
}
