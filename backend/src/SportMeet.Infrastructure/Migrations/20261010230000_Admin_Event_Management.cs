using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using SportMeet.Infrastructure.Persistence;

#nullable disable

namespace SportMeet.Infrastructure.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20261010230000_Admin_Event_Management")]
public partial class Admin_Event_Management : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("DROP VIEW sportsmeet.v_event_feed;");

        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "deleted_at",
            schema: "sportsmeet",
            table: "events",
            type: "timestamp with time zone",
            nullable: true);

        migrationBuilder.Sql(ActiveEventsView);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("DROP VIEW sportsmeet.v_event_feed;");
        migrationBuilder.DropColumn(
            name: "deleted_at",
            schema: "sportsmeet",
            table: "events");
        migrationBuilder.Sql(EventViewSql.AddEventRejectionReasonView);
    }

    private const string ActiveEventsView = """
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
            e.rejection_reason,
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
            e.thumbnail_url,
            u.role AS host_role
        FROM sportsmeet.events e
        LEFT JOIN sportsmeet.sports s ON s.id = e.sport_id
        JOIN sportsmeet.users u ON u.id = e.host_id
        WHERE e.deleted_at IS NULL;
        """;
}
