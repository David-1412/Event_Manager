using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SportMeet.Domain.Entities;
using SportMeet.Domain.Enums;

namespace SportMeet.Infrastructure.Persistence.Config;

public sealed class IngestedEmailConfiguration : IEntityTypeConfiguration<IngestedEmail>
{
    public void Configure(EntityTypeBuilder<IngestedEmail> b)
    {
        b.ToTable("ingested_emails", SportConfiguration.Schema);

        b.Property(x => x.Id).IsRequired();

        b.Property(x => x.Mailbox).IsRequired();
        b.Property(x => x.MessageUid).IsRequired();
        b.Property(x => x.MessageId).IsRequired();
        b.Property(x => x.FromAddr).IsRequired();
        b.Property(x => x.Subject).IsRequired();
        b.Property(x => x.BodyText).IsRequired();
        b.Property(x => x.BodyHash).IsRequired();

        // The polling idempotency key. Unique rather than a "check then insert":
        // two overlapping cycles must lose safely at insert time, not both decide
        // a message is new.
        b.HasIndex(x => new { x.Mailbox, x.MessageUid })
            .IsUnique()
            .HasDatabaseName("ingested_emails_mailbox_uid_key");

        // Second dedupe tier. Not unique — a body hash can legitimately repeat
        // once recorded, and we want every sighting kept for the audit trail.
        b.HasIndex(x => x.BodyHash).HasDatabaseName("ingested_emails_body_hash_idx");

        // sha256 hex is exactly 64; the CHECK makes a truncated or non-hex write a
        // database error instead of a hash that silently never matches again.
        b.Property(x => x.BodyHash).HasMaxLength(64);
        b.ToTable(t => t.HasCheckConstraint(
            "ingested_emails_body_hash_length_check",
            "char_length(body_hash) = 64"));

        b.Property(x => x.ExtractionStatus).HasConversion<string>().HasMaxLength(20).IsRequired();
        b.ToTable(t => t.HasCheckConstraint(
            "ingested_emails_extraction_status_check",
            $"extraction_status IN ('{nameof(ExtractionStatus.Pending)}', '{nameof(ExtractionStatus.Extracted)}', '{nameof(ExtractionStatus.NoEvent)}', '{nameof(ExtractionStatus.Error)}')"));

        b.Property(x => x.Model).HasMaxLength(100);
        b.Property(x => x.PromptVersion).HasMaxLength(50);

        // A hash is only meaningful if it is lowercase-hex like the comparison.
        b.ToTable(t => t.HasCheckConstraint(
            "ingested_emails_body_hash_lower_check",
            "body_hash = lower(body_hash)"));

        // Queue-draining order: oldest unprocessed first, and the index is partial
        // because processed rows (the overwhelming majority over time) are never
        // scanned by that query.
        b.HasIndex(x => x.CreatedAt)
            .HasDatabaseName("ingested_emails_pending_idx")
            .HasFilter("processed_at IS NULL");

        b.Property(x => x.CreatedAt).IsRequired();
    }
}
