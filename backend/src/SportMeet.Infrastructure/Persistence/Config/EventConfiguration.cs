using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SportMeet.Domain.Entities;
using SportMeet.Domain.Enums;

namespace SportMeet.Infrastructure.Persistence.Config;

public sealed class EventConfiguration : IEntityTypeConfiguration<Event>
{
    /// <summary>Bounds that must stay in step with CreateEventDtoValidator and
    /// the client's create-event-schema.ts. MaxParticipants tops out at 50 to
    /// match the client's "50 is the maximum for now" rather than the 100 in
    /// IMPLEMENTATION_PLAN.md §2 - a server looser than the client lets a
    /// non-browser caller store a value the UI then rejects.</summary>
    public const int TitleMaxLength = 120;
    public const int DescriptionMaxLength = 2000;
    public const int VenueMaxLength = 120;
    public const int ParticipantsMin = 2;
    public const int ParticipantsMax = 50;

    public void Configure(EntityTypeBuilder<Event> b)
    {
        b.ToTable("events", SportConfiguration.Schema);
        b.HasQueryFilter(x => x.DeletedAt == null);

        b.Property(x => x.Id).ValueGeneratedNever();

        b.Property(x => x.Title).HasMaxLength(TitleMaxLength).IsRequired();
        b.Property(x => x.Description).HasMaxLength(DescriptionMaxLength);
        b.Property(x => x.VenueName).HasMaxLength(VenueMaxLength).IsRequired();
        b.Property(x => x.Address).HasMaxLength(400);
        b.Property(x => x.ThumbnailUrl).HasMaxLength(400);
        b.Property(x => x.PlaceId).HasMaxLength(200);
        b.Property(x => x.Timezone).HasMaxLength(64).HasDefaultValue("Australia/Melbourne").IsRequired();

        // DateTimeOffset maps to timestamptz under Npgsql; the value arrives as
        // UTC and Postgres keeps the offset, so a stored instant survives a zone
        // change on the server.
        b.Property(x => x.StartAt).IsRequired();
        b.Property(x => x.EndAt).IsRequired();

        // Enums persist as their CLR name so psql output is readable and no magic
        // integer leaks into a dump (IMPLEMENTATION_PLAN.md §3). The CHECK
        // constraints below list exactly these spellings: EF's string conversion
        // writes ToString(), so a CHECK of 'scheduled' would reject every insert.
        // SkillLevel therefore needs no case mapping here - its stored spelling is
        // already the PascalCase the API serialises. Nullable: the create form no
        // longer offers a selector, and a CHECK that evaluates to NULL passes in
        // Postgres, so events_skill_level_check tolerates the null unchanged.
        b.Property(x => x.SkillLevel).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(20).IsRequired();

        // No HasDefaultValue here, deliberately, even though the column has a real
        // server-side DEFAULT in the migration. EF validates a default against the
        // property's model-clr type *before* the value converter runs, so both
        // spellings fail at model build: HasDefaultValue("Public") throws
        // "Cannot set default value 'Public' of type 'System.String' on property
        // 'Visibility' of type 'EventVisibility'", and an enum default throws the
        // mirror image (hit while verifying against a real database). The only
        // default EF would accept is the enum value itself, and EF then *sends* that
        // on insert, which defeats the point of a server default.
        //
        // The server default still does its job, just outside the model: EF sends
        // every mapped property on an INSERT, so an EF write always carries a value
        // (the entity's own initializer makes it Public), while a raw INSERT that
        // omits the column - an old stored draft payload replayed by hand, a manual
        // row - falls back to 'Public' in Postgres. The CHECK is the net for a bad
        // value from either path.
        b.Property(x => x.Visibility).HasConversion<string>().HasMaxLength(10).IsRequired();

        b.Property(x => x.Cost).HasPrecision(6, 2);

        // The reviewer's sentence to the creator. Length-bounded so the reason the
        // admin types cannot outgrow the panel the creator reads it in; the API's
        // validator carries the same number so a too-long reason is a 422, not a
        // database error.
        b.Property(x => x.RejectionReason).HasMaxLength(500);

        b.HasOne(x => x.Host).WithMany(u => u.HostedEvents).HasForeignKey(x => x.HostId).OnDelete(DeleteBehavior.Restrict);

        // Optional and Restrict: sport_id is an emoji lookup now, not a filter, so
        // deleting a seeded sport must not silently take an event's icon with it
        // nor cascade into the event.
        b.HasOne(x => x.Sport).WithMany(s => s.Events).HasForeignKey(x => x.SportId).OnDelete(DeleteBehavior.Restrict);

        b.HasIndex(x => x.StartAt).HasFilter("status = 'Scheduled'").HasDatabaseName("events_start_at_idx");
        b.HasIndex(x => new { x.Lat, x.Lng }).HasDatabaseName("events_geo_bbox_idx");

        // The admin review queue's working query - pending events oldest first, so
        // the one that has been waiting longest is the one an admin opens. Partial
        // for the same reason as the draft queue's: everything else is dead weight
        // in the index the review page hits on every reload.
        b.HasIndex(x => x.CreatedAt)
            .HasDatabaseName("events_pending_review_idx")
            .HasFilter("status = 'PendingReview'");


        b.ToTable(t =>
        {
            t.HasCheckConstraint("events_title_length_check", $"char_length(title) BETWEEN 3 AND {TitleMaxLength}");
            t.HasCheckConstraint(
                "events_description_length_check",
                $"description IS NULL OR char_length(description) <= {DescriptionMaxLength}");
            t.HasCheckConstraint(
                "events_venue_length_check",
                $"char_length(venue_name) BETWEEN 1 AND {VenueMaxLength}");
            t.HasCheckConstraint("events_lat_check", "lat BETWEEN -90 AND 90");
            t.HasCheckConstraint("events_lng_check", "lng BETWEEN -180 AND 180");
            t.HasCheckConstraint(
                "events_participants_check",
                $"max_participants BETWEEN {ParticipantsMin} AND {ParticipantsMax}");
            t.HasCheckConstraint("events_time_range_check", "end_at > start_at");
            t.HasCheckConstraint("events_cost_check", "cost IS NULL OR cost >= 0");
            t.HasCheckConstraint(
                "events_skill_level_check",
                "skill_level IN ('Beginner', 'Intermediate', 'Advanced')");
            t.HasCheckConstraint(
                // The review workflow's four values alongside the original three.
                // 'Scheduled' survives: it is what every pre-workflow row holds and
                // what an admin's or a private creator's event is still written as,
                // so "live" means Scheduled *or* Published (EventStatusExtensions).
                "events_status_check",
                "status IN ('Scheduled', 'Cancelled', 'Completed', 'Draft', 'PendingReview', 'Published', 'Rejected')");
            t.HasCheckConstraint(
                "events_visibility_check",
                "visibility IN ('Public', 'Private')");
        });
    }
}
