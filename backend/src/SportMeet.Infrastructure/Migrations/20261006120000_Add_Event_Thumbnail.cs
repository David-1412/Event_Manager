using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using SportMeet.Infrastructure.Persistence;

#nullable disable

namespace SportMeet.Infrastructure.Migrations;

/// <summary>
/// events.thumbnail_url - the URL of an optional host-uploaded thumbnail (the
/// bytes live on disk under App_Data/uploads and are served from /uploads/...;
/// the column only ever stores that URL). Nullable: an event with no image is a
/// normal event, and every renderer falls back to the no-image layout.
///
/// Same structure as Add_Event_Visibility: drop the view, change the table,
/// recreate the view. EF cannot snapshot a view, so the view half of the change
/// produces no model diff and is spelled out here.
///
/// The new view SQL is a private constant rather than EventViewSql.Definition,
/// for the same load-bearing reason: Definition is replayed by earlier
/// migrations, so adding a column to it makes those migrations reference
/// events.thumbnail_url before this one creates it (42703 on a fresh database).
/// The new column is appended last because CREATE OR REPLACE only allows new
/// view columns at the end - and here the view is dropped and recreated anyway.
/// </summary>
[DbContext(typeof(AppDbContext))]
[Migration("20261006120000_Add_Event_Thumbnail")]
public partial class Add_Event_Thumbnail : Migration
{
    /// <summary>The v_event_feed shape after Add_Event_Interests_Counts, plus
    /// e.thumbnail_url appended last.</summary>
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
            e.visibility,
            (SELECT COUNT(*)::int FROM sportsmeet.event_interests i WHERE i.event_id = e.id)
                AS interested_count,
            e.thumbnail_url
        FROM sportsmeet.events e
        LEFT JOIN sportsmeet.sports s ON s.id = e.sport_id
        JOIN sportsmeet.users u ON u.id = e.host_id;
        """;

    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // The view reads events, and CREATE OR REPLACE cannot resolve a column in
        // its SELECT that the table does not have yet - so it comes down first.
        migrationBuilder.Sql("DROP VIEW sportsmeet.v_event_feed;");

        // Nullable text, no default: existing rows simply have no thumbnail, which
        // is the same state a newly-created event without an image is in.
        migrationBuilder.AddColumn<string>(
            name: "thumbnail_url",
            schema: "sportsmeet",
            table: "events",
            type: "character varying(400)",
            maxLength: 400,
            nullable: true);

        migrationBuilder.Sql(ViewSql);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("DROP VIEW sportsmeet.v_event_feed;");

        migrationBuilder.DropColumn(
            name: "thumbnail_url",
            schema: "sportsmeet",
            table: "events");

        // Back to the shape the previous migration left behind.
        migrationBuilder.Sql(EventViewSql.Definition);
    }
}
