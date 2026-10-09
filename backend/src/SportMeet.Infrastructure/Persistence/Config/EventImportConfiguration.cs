using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SportMeet.Domain.Entities;

namespace SportMeet.Infrastructure.Persistence.Config;

public sealed class EventImportConfiguration : IEntityTypeConfiguration<EventImport>
{
    public void Configure(EntityTypeBuilder<EventImport> b)
    {
        b.ToTable("event_imports", SportConfiguration.Schema);

        // Text with no CHECK listing the members, so a new input kind is not a migration.
        b.Property(x => x.InputKind).HasConversion<string>().HasMaxLength(20).IsRequired();
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(20).IsRequired();

        b.Property(x => x.SourceText).HasColumnType("text");

        // jsonb for every document: invalid JSON fails at the database door, and the
        // accuracy queries can use ->> directly (see docs/EVENT_IMPORT.md).
        b.Property(x => x.ExtractedData).HasColumnType("jsonb");
        b.Property(x => x.Flags).HasColumnType("jsonb");
        b.Property(x => x.Geocode).HasColumnType("jsonb");
        b.Property(x => x.FinalData).HasColumnType("jsonb");
        b.Property(x => x.FieldOutcomes).HasColumnType("jsonb");

        b.Property(x => x.Confidence).HasColumnType("numeric(4,3)");
        b.ToTable(t => t.HasCheckConstraint(
            "event_imports_confidence_check",
            "confidence >= 0 AND confidence <= 1"));
        b.Property(x => x.MissingFields).HasColumnType("text[]");
        b.Property(x => x.Model).HasMaxLength(100).IsRequired();
        b.Property(x => x.PromptVersion).HasMaxLength(50).IsRequired();

        // Restricted, like drafts: an import is an audit record and must outlive any
        // attempt to remove its user.
        b.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);

        // The global daily ceiling counts by time; the accuracy report scans by outcome.
        b.HasIndex(x => x.CreatedAt);
        b.HasIndex(x => new { x.UserId, x.CreatedAt });
    }
}

public sealed class EventPublishMetricConfiguration : IEntityTypeConfiguration<EventPublishMetric>
{
    public void Configure(EntityTypeBuilder<EventPublishMetric> b)
    {
        b.ToTable("event_publish_metrics", SportConfiguration.Schema);

        b.Property(x => x.Path).HasMaxLength(10).IsRequired();
        b.ToTable(t => t.HasCheckConstraint(
            "event_publish_metrics_path_check",
            "path IN ('manual', 'import', 'draft')"));
        b.ToTable(t => t.HasCheckConstraint(
            "event_publish_metrics_duration_check",
            "duration_ms >= 0"));

        // One measurement per event, so a retried report cannot double-count.
        b.HasIndex(x => x.EventId).IsUnique();
        b.HasIndex(x => new { x.Path, x.CreatedAt });

        // Derivative data: it goes when its event goes.
        b.HasOne<Event>().WithMany().HasForeignKey(x => x.EventId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<EventImport>().WithMany().HasForeignKey(x => x.ImportId).OnDelete(DeleteBehavior.SetNull);
    }
}
