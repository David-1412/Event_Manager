using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SportMeet.Domain.Entities;
using SportMeet.Domain.Enums;

namespace SportMeet.Infrastructure.Persistence.Config;

public sealed class EventJoinRequestConfiguration : IEntityTypeConfiguration<EventJoinRequest>
{
    public void Configure(EntityTypeBuilder<EventJoinRequest> b)
    {
        b.ToTable("event_join_requests", SportConfiguration.Schema);

        // Composite key: one standing per (event, person), the same idempotency
        // shape as event_participants and event_interests. Resending a request
        // that already exists converges instead of queueing a duplicate.
        b.HasKey(x => new { x.EventId, x.UserId });

        // Stored as text under a CHECK, like status and visibility, so a typo fails
        // at the database rather than reaching a client that has no rendering for it.
        b.Property(x => x.Status)
            .HasMaxLength(20)
            .IsRequired()
            .HasConversion<string>()
            .HasColumnName("status");

        b.Property(x => x.RequestedAt).IsRequired().HasColumnName("requested_at");
        b.Property(x => x.DecidedAt).HasColumnName("decided_at");

        b.HasOne(x => x.Event).WithMany().HasForeignKey(x => x.EventId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);

        b.ToTable(t => t.HasCheckConstraint(
            "event_join_requests_status_check",
            "status IN ('Pending', 'Approved', 'Rejected')"));

        // The host's Join Requests list: pending requests for one event, oldest
        // first. Partial so it stays small and stays that way - decided requests
        // leave the index rather than accumulating in it, and the (event_id,
        // requested_at) order matches the query so the queue reads without a sort.
        //
        // The event side of a single lookup is served by the composite key's
        // leading column; this exists for the filtered, ordered list read.
        b.HasIndex(x => new { x.EventId, x.RequestedAt })
            .HasDatabaseName("ix_event_join_requests_event_pending")
            .HasFilter("status = 'Pending'");

        // "What have I asked about?" - the member's own pending state across their
        // events, which is what the detail page reads to render Pending Approval.
        b.HasIndex(x => x.UserId).HasDatabaseName("ix_event_join_requests_user_id");
    }
}
