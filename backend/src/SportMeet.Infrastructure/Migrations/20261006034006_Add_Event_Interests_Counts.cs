using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SportMeet.Infrastructure.Migrations;

/// <summary>
/// Adds the interest side of the read model: the event_interests table (if it is
/// not already present) and the interested_count column on v_event_feed, so a
/// browse page resolves joined + interested counts in its single round-trip.
///
/// Why hand-written SQL rather than the scaffolded CreateTable: the dev database
/// already carries an event_interests table and a matching history row
/// (20261002004912_Add_Event_Interests) that were applied out-of-band, so that
/// migration id is recorded but its .cs file is not in the tree. EF applies
/// migrations by history id, so it will never re-run that id; and a plain
/// CreateTable here would crash on the table that already exists. Every statement
/// is therefore idempotent (IF NOT EXISTS / CREATE OR REPLACE), which makes the
/// same migration correct against three starting points at once:
///   - the drifted dev DB (table + old view present)  -> only the view changes;
///   - a fresh DB (nothing present)                   -> table + view created;
///   - a DB where the table exists but the view predates it -> view replaced.
/// The DDL mirrors the existing table exactly (composite PK, cascade FKs,
/// ix_event_interests_user_id, interested_at column) so the reconciled model and
/// the reconciled schema agree.
///
/// The new view SQL is a private constant, not EventViewSql.Definition, for the
/// same reason Add_Event_Visibility gives: Definition is replayed by earlier
/// migrations, and a column added there would make those reference a table or
/// column they predate. It is the Add_Event_Visibility view plus the
/// interested_count correlated subquery, appended last.
/// </summary>
public partial class Add_Event_Interests_Counts : Migration
{
    private const string TableDdl = """
        CREATE TABLE IF NOT EXISTS sportsmeet.event_interests (
            event_id uuid NOT NULL,
            user_id uuid NOT NULL,
            interested_at timestamp with time zone NOT NULL,
            CONSTRAINT pk_event_interests PRIMARY KEY (event_id, user_id),
            CONSTRAINT fk_event_interests_events_event_id FOREIGN KEY (event_id)
                REFERENCES sportsmeet.events (id) ON DELETE CASCADE,
            CONSTRAINT fk_event_interests_users_user_id FOREIGN KEY (user_id)
                REFERENCES sportsmeet.users (id) ON DELETE CASCADE
        );
        """;

    private const string IndexDdl =
        "CREATE INDEX IF NOT EXISTS ix_event_interests_user_id ON sportsmeet.event_interests (user_id);";

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
            -- The interest count, derived exactly like current_participants: a
            -- correlated per-row index scan, not a joined GROUP BY (which would
            -- multiply event rows and inflate both counts and the page LIMIT).
            (SELECT COUNT(*)::int FROM sportsmeet.event_interests i WHERE i.event_id = e.id)
                AS interested_count
        FROM sportsmeet.events e
        LEFT JOIN sportsmeet.sports s ON s.id = e.sport_id
        JOIN sportsmeet.users u ON u.id = e.host_id;
        """;

    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Table first: the view's SELECT references event_interests, so the table
        // must exist before CREATE OR REPLACE parses. IF NOT EXISTS makes this a
        // no-op on the drifted dev DB, where the table is already there.
        migrationBuilder.Sql(TableDdl);
        migrationBuilder.Sql(IndexDdl);
        migrationBuilder.Sql(ViewSql);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Back to the Add_Event_Visibility view shape (no interested_count), then
        // drop the table. The DropTable is unconditional on purpose: a fresh DB
        // rollback must remove what Up created, and the drifted dev DB is expected
        // to be reset/re-seeded rather than rolled back in place.
        migrationBuilder.Sql("DROP VIEW sportsmeet.v_event_feed;");
        migrationBuilder.Sql(EventViewSql.AddEventVisibilityView);
        migrationBuilder.Sql("DROP TABLE IF EXISTS sportsmeet.event_interests;");
    }
}
