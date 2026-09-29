using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SportMeet.Domain.Entities;

namespace SportMeet.Infrastructure.Persistence.Config;

public sealed class EventParticipantConfiguration : IEntityTypeConfiguration<EventParticipant>
{
    public void Configure(EntityTypeBuilder<EventParticipant> b)
    {
        b.ToTable("event_participants", SportConfiguration.Schema);

        // Composite key: one row per (event, person), which is also the
        // idempotency guard the join/leave endpoints lean on later.
        b.HasKey(x => new { x.EventId, x.UserId });

        b.Property(x => x.JoinedAt).IsRequired();

        b.HasOne(x => x.Event).WithMany(e => e.Participants).HasForeignKey(x => x.EventId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}
