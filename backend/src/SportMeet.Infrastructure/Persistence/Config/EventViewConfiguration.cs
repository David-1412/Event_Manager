using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SportMeet.Domain.Entities;

namespace SportMeet.Infrastructure.Persistence.Config;

public sealed class EventViewConfiguration : IEntityTypeConfiguration<EventView>
{
    public void Configure(EntityTypeBuilder<EventView> b)
    {
        b.ToTable("event_views", SportConfiguration.Schema);

        b.Property(x => x.Id).ValueGeneratedNever();

        b.Property(x => x.ViewedAt).IsRequired().HasColumnName("viewed_at");

        // date, not timestamptz. This column exists to be the day in a uniqueness
        // rule, and Postgres compares `date` values exactly - no precision or offset
        // question can make two writers disagree about whether it is the same day.
        b.Property(x => x.ViewedOn).IsRequired().HasColumnName("viewed_on");

        b.HasOne(x => x.Event).WithMany().HasForeignKey(x => x.EventId).OnDelete(DeleteBehavior.Cascade);

        // Set null rather than cascade. A view of an event someone deleted is still
        // a view of that event and the host's number should not silently shrink
        // because an account closed; the FK column simply stops resolving to a user.
        // (The event side does cascade - views of a deleted event go with it.)
        b.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.SetNull);

        // The deduplication, in the place it cannot be raced: one row per
        // (event, person, day). Partial because user_id IS NULL rows are anonymous
        // visitors and there is no such thing as "the same anonymous person" -
        // without the filter, Postgres would treat every NULL as distinct anyway
        // (NULLs never collide in a unique index), but saying it keeps the index
        // small and states the rule where someone can read it.
        //
        // Leading with event_id serves the host's COUNT; the rest of the key is what
        // the insert collides against.
        b.HasIndex(x => new { x.EventId, x.UserId, x.ViewedOn })
            .IsUnique()
            .HasDatabaseName("ix_event_views_event_user_day")
            .HasFilter("user_id IS NOT NULL");

        // The per-event count over the whole table, including anonymous rows the
        // unique index above never covers.
        b.HasIndex(x => x.EventId).HasDatabaseName("ix_event_views_event_id");
        // Campus Pulse asks "which events were viewed in this period".
        b.HasIndex(x => x.ViewedAt).HasDatabaseName("ix_event_views_viewed_at");
    }
}
