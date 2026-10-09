using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using SportMeet.Infrastructure.Persistence;

#nullable disable

namespace SportMeet.Infrastructure.Migrations;

/// <summary>
/// events.visibility ('Public' | 'Private'), and the same column on
/// v_event_feed so the browse query can filter on it inside the one round-trip
/// the feed exists for.
///
/// Same structure as Increase_Event_Title_Length: drop the view, change the
/// table, recreate the view. EF cannot snapshot a view, so the view half of a
/// change like this produces no model diff and has to be spelled out here.
///
/// The new view SQL is a private constant rather than EventViewSql.Definition,
/// and that is load-bearing rather than stylistic. Definition is replayed by
/// both Add_Tags and Increase_Event_Title_Length, so a column added to it makes
/// those *earlier* migrations reference events.visibility before this migration
/// creates it: 42703 on every fresh database, while an already-upgraded one
/// never replays them and so appears fine. Init_Views keeps its own definition
/// for the same reason.
/// </summary>
[DbContext(typeof(AppDbContext))]
[Migration("20261006000000_Add_Event_Visibility")]
public partial class Add_Event_Visibility : Migration
{
    /// <summary>EventViewSql.Definition plus e.visibility, appended last: CREATE
    /// OR REPLACE only allows new columns at the end, and here the view is dropped
    /// and recreated anyway.</summary>
    private const string ViewSql = """
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
            e.visibility
        FROM sportsmeet.events e
        LEFT JOIN sportsmeet.sports s ON s.id = e.sport_id
        JOIN sportsmeet.users u ON u.id = e.host_id;
        """;

    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // The view reads events, and CREATE OR REPLACE cannot resolve a column in
        // its SELECT that the table does not have yet - so it comes down first.
        migrationBuilder.Sql("DROP VIEW sportsmeet.v_event_feed;");

        // NOT NULL with a server default: every row already in the table is a
        // browse-listing event, which is precisely what 'Public' means, so the
        // default *is* the backfill. It also covers a raw INSERT that omits the
        // column; EF's own INSERT always sends the property, whose entity
        // initializer is Public.
        migrationBuilder.AddColumn<string>(
            name: "visibility",
            schema: "sportsmeet",
            table: "events",
            type: "character varying(10)",
            maxLength: 10,
            nullable: false,
            defaultValue: "Public");

        migrationBuilder.AddCheckConstraint(
            name: "events_visibility_check",
            schema: "sportsmeet",
            table: "events",
            sql: "visibility IN ('Public', 'Private')");

        migrationBuilder.Sql(ViewSql);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("DROP VIEW sportsmeet.v_event_feed;");

        migrationBuilder.DropCheckConstraint(
            name: "events_visibility_check",
            schema: "sportsmeet",
            table: "events");

        migrationBuilder.DropColumn(
            name: "visibility",
            schema: "sportsmeet",
            table: "events");

        // Back to the shape the previous migration left behind.
        migrationBuilder.Sql(EventViewSql.Definition);
    }
}
