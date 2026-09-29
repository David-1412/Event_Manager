using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SportMeet.Domain.Entities;
using SportMeet.Domain.Enums;

namespace SportMeet.Infrastructure.Persistence.Config;

public sealed class EventDraftConfiguration : IEntityTypeConfiguration<EventDraft>
{
    public void Configure(EntityTypeBuilder<EventDraft> b)
    {
        b.ToTable("event_drafts", SportConfiguration.Schema);

        b.Property(x => x.Id).IsRequired();

        // jsonb, not text: the value is a JSON document we never cast, so the type
        // buys validity-at-the-database-door (a stray write of non-JSON fails
        // loudly) and lets a future psql investigator use ->> on it. EF maps a
        // plain string to jsonb without a value converter, which is what keeps the
        // service free of serialization on the write path.
        b.Property(x => x.Payload).HasColumnType("jsonb").IsRequired();

        // Model's self-reported confidence. numeric(4,3) holds 0.000-1.000; the
        // CHECK is what actually states the contract, since numeric would happily
        // accept 99.
        b.Property(x => x.Confidence).HasColumnType("numeric(4,3)");
        b.ToTable(t => t.HasCheckConstraint(
            "event_drafts_confidence_check",
            "confidence >= 0 AND confidence <= 1"));

        // text[]. MissingFields values are CreateEventDto property names, so they
        // are bounded and short; the array itself is capped well below anything a
        // DTO could produce.
        b.Property(x => x.MissingFields).HasColumnType("text[]");

        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
        b.ToTable(t => t.HasCheckConstraint(
            "event_drafts_status_check",
            $"status IN ('{nameof(DraftStatus.Pending)}', '{nameof(DraftStatus.Approved)}', '{nameof(DraftStatus.Rejected)}', '{nameof(DraftStatus.Duplicate)}', '{nameof(DraftStatus.Deleted)}')"));


        b.Property(x => x.ReviewNote).HasMaxLength(500);
        b.Property(x => x.CreatedAt).IsRequired();

        b.HasOne(x => x.IngestedEmail)
            .WithMany(e => e.Drafts)
            .HasForeignKey(x => x.IngestedEmailId)
            // Restricted rather than cascade: a draft is the human-facing record of
            // a decision, and deleting its source email must not silently erase the
            // audit trail of what was approved and by whom.
            .OnDelete(DeleteBehavior.Restrict);

        // Ownership. Required FK, restricted on delete: a user cannot be removed while
        // their drafts reference them, which is the safe default while deletion of a
        // user is not a real operation yet. The per-user pending queue is the hot read,
        // so one composite index serves both the owner filter and the created_at order,
        // scoped to pending (a partial index, like the queue query itself).
        b.HasOne(x => x.User)
            .WithMany(u => u.Drafts)
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => new { x.UserId, x.CreatedAt })
            .HasDatabaseName("event_drafts_user_pending_idx")
            .HasFilter("status = 'Pending'");


        // The queue's working query — pending drafts newest first, now across every
        // owner (admin/ops view). Partial for the same reason as the events partial
        // index: decided drafts are dead weight in the index the reviewer's page hits
        // on every reload. The per-owner read uses event_drafts_user_pending_idx.
        b.HasIndex(x => x.CreatedAt)
            .HasDatabaseName("event_drafts_pending_idx")
            .HasFilter("status = 'Pending'");


        // Lookup on approve/reject, and FOR UPDATE targets the row by id anyway;
        // this keeps the FK indexed for the reverse (email -> drafts) query the
        // audit path needs.
        b.HasIndex(x => x.IngestedEmailId).HasDatabaseName("event_drafts_email_idx");

        // An approved draft must point at the event it created, and nothing else
        // may. Without this, a partial write could leave status=approved with a
        // null event_id, which reads as "approved into nothing".
        b.ToTable(t => t.HasCheckConstraint(
            "event_drafts_approved_event_check",
            "(status = 'Approved') = (event_id IS NOT NULL)"));
    }
}
