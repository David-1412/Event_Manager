using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using SportMeet.Infrastructure.Persistence;

#nullable disable

namespace SportMeet.Infrastructure.Migrations;

/// <summary>
/// The approval workflow's schema: who may approve, and what an event waiting
/// for approval is allowed to be called.
///
///  1. users.role ('Member' | 'Admin', defaulting to 'Member'). Role lives on the
///     row rather than in configuration or in a token claim so a promotion is one
///     UPDATE that takes effect on the next request, and so the local demo - where
///     most callers resolve through the seeded identity - has a real reviewer to
///     look at. The DEFAULT is the backfill: every row that predates this
///     migration is an ordinary member, and the seeder promotes the demo users it
///     owns (an idempotent write, so an already-seeded database is not left with
///     nobody who can review).
///  2. events_status_check widened to the review vocabulary. The existing rows keep
///     'Scheduled', which stays a live status - EventStatusExtensions.IsPublished
///     treats Scheduled and Published as the same thing for visibility.
///  3. events_pending_review_idx, the partial index the review queue reads, shaped
///     like event_drafts_pending_idx for the same reason.
///  4. v_event_feed gains host_role, so GET /api/events/{id} can decide in its
///     single round-trip whether the caller may see an event that is not published
///     yet (its creator, or the admin who has to review it).
///
/// Same structure as Add_Event_Visibility / Add_Event_Thumbnail: drop the view,
/// change the tables, recreate the view. EF cannot snapshot a view, so the view
/// half produces no model diff and is spelled out here, and the new view SQL is the
/// EventViewSql.AddEventReviewWorkflowView constant rather than Definition -
/// Definition is replayed by earlier migrations, which would then reference
/// users.role before this migration creates it (42703 on a fresh database, while an
/// upgraded one never replays them and appears fine).
/// </summary>
[DbContext(typeof(AppDbContext))]
[Migration("20261008120000_Add_Event_Review_Workflow")]
public partial class Add_Event_Review_Workflow : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // The view reads users, and CREATE OR REPLACE cannot resolve a column its
        // SELECT references that the table does not have yet - so it comes down first.
        migrationBuilder.Sql("DROP VIEW sportsmeet.v_event_feed;");

        migrationBuilder.AddColumn<string>(
            name: "role",
            schema: "sportsmeet",
            table: "users",
            type: "character varying(20)",
            maxLength: 20,
            nullable: false,
            defaultValue: "Member");

        migrationBuilder.AddCheckConstraint(
            name: "users_role_check",
            schema: "sportsmeet",
            table: "users",
            sql: "role IN ('Member', 'Admin')");

        // Drop and re-add rather than alter: Postgres has no ALTER CHECK, and the
        // existing rows all satisfy the wider list, so no data has to move.
        migrationBuilder.DropCheckConstraint(
            name: "events_status_check",
            schema: "sportsmeet",
            table: "events");

        migrationBuilder.AddCheckConstraint(
            name: "events_status_check",
            schema: "sportsmeet",
            table: "events",
            sql: "status IN ('Scheduled', 'Cancelled', 'Completed', 'Draft', 'PendingReview', 'Published', 'Rejected')");

        // Oldest submission first, which is the order the queue is worked in. Partial
        // because every other event is dead weight in it.
        migrationBuilder.CreateIndex(
            name: "events_pending_review_idx",
            schema: "sportsmeet",
            table: "events",
            column: "created_at",
            filter: "status = 'PendingReview'");

        migrationBuilder.Sql(EventViewSql.AddEventReviewWorkflowView);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("DROP VIEW sportsmeet.v_event_feed;");

        migrationBuilder.DropIndex(
            name: "events_pending_review_idx",
            schema: "sportsmeet",
            table: "events");

        // Anything this migration let in has to go before the narrower CHECK can go
        // back on: an approved event is 'Published', which the old list rejects, and
        // rolling back would otherwise fail on the rows the workflow created.
        migrationBuilder.Sql("""
            UPDATE sportsmeet.events SET status = 'Scheduled'
             WHERE status IN ('Draft', 'PendingReview', 'Published');
            UPDATE sportsmeet.events SET status = 'Cancelled' WHERE status = 'Rejected';
            """);

        migrationBuilder.DropCheckConstraint(
            name: "events_status_check",
            schema: "sportsmeet",
            table: "events");

        migrationBuilder.AddCheckConstraint(
            name: "events_status_check",
            schema: "sportsmeet",
            table: "events",
            sql: "status IN ('Scheduled', 'Cancelled', 'Completed')");

        migrationBuilder.DropCheckConstraint(
            name: "users_role_check",
            schema: "sportsmeet",
            table: "users");

        migrationBuilder.DropColumn(
            name: "role",
            schema: "sportsmeet",
            table: "users");

        // Back to the shape Add_Event_Thumbnail left behind.
        migrationBuilder.Sql(ThumbnailViewSql);
    }


    /// <summary>The v_event_feed shape as Add_Event_Thumbnail created it, needed only
    /// by Down(). A private constant because <see cref="EventViewSql"/> freezes its
    /// own strings at earlier boundaries; this is the one that immediately precedes
    /// this migration.</summary>
    private const string ThumbnailViewSql = """
        CREATE OR REPLACE VIEW sportsmeet.v_event_feed AS
        SELECT
            e.id,
            e.host_id,
            e.title,
            e.description,
            e.sport_id,
            e.venue_name,
            e.address,
            e.place_id,
            e.lat,
            e.lng,
            e.timezone,
            e.start_at,
            e.end_at,
            e.max_participants,
            e.skill_level,
            e.cost,
            e.status,
            e.cancelled_at,
            e.created_at,
            e.updated_at,
            (SELECT COUNT(*)::int FROM sportsmeet.event_participants p WHERE p.event_id = e.id)
                AS current_participants,
            s.name AS sport_name,
            s.slug AS sport_slug,
            s.icon AS sport_icon,
            u.name AS host_name,
            u.photo_url AS host_photo_url,
            (SELECT string_agg(t.name::text, ',' ORDER BY t.name)
               FROM sportsmeet.event_tags et
               JOIN sportsmeet.tags t ON t.id = et.tag_id
              WHERE et.event_id = e.id) AS tags,
            e.visibility,
            (SELECT COUNT(*)::int FROM sportsmeet.event_interests i WHERE i.event_id = e.id)
                AS interested_count,
            e.thumbnail_url
        FROM sportsmeet.events e
        LEFT JOIN sportsmeet.sports s ON s.id = e.sport_id
        JOIN sportsmeet.users u ON u.id = e.host_id;
        """;
}
