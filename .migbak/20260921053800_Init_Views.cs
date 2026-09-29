using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SportMeet.Infrastructure.Migrations;

/// <summary>
/// HAND-WRITTEN SQL. EF scaffolded this file empty because a view has no model
/// representation, so the only correct way to change it is to edit the SQL here.
///
/// DO NOT RUN `dotnet ef migrations remove` on this migration without reading it
/// first: the tool snapshots nothing it did not generate, and a re-scaffold would
/// silently drop the view and the function. Both are idempotent (DROP IF EXISTS
/// then CREATE), so re-applying after such a mistake is safe.
///
/// Why a view: browse needs each event's participant count, and a stored counter
/// is a lost-update race waiting to happen (IMPLEMENTATION_PLAN.md §2). COUNTing
/// event_participants inside v_event_feed keeps that honest while still serving
/// a whole browse page in one round-trip.
/// </summary>
public partial class Init_Views : Migration
{
    private const string FunctionSql = """
        CREATE OR REPLACE FUNCTION sportsmeet.haversine_km(
            lat1 double precision, lng1 double precision,
            lat2 double precision, lng2 double precision)
        RETURNS double precision
        LANGUAGE sql IMMUTABLE PARALLEL SAFE
        AS $$
            -- acos() is only defined on [-1,1] and floating-point error can push the
            -- cosine term a hair past 1 for two points at the same coordinates, which
            -- would make the whole query return NaN and silently corrupt the distance
            -- ordering. Clamping is one LEAST/GREATEST pair and removes that class of
            -- failure entirely.
            SELECT 6371 * acos(LEAST(1, GREATEST(-1,
                  cos(radians(lat1)) * cos(radians(lat2))
                    * cos(radians(lng2) - radians(lng1))
                + sin(radians(lat1)) * sin(radians(lat2))
            )));
        $$;
        """;

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
            -- Correlated rather than a joined GROUP BY: the planner turns it into a
            -- per-row index scan on the composite event_participants key, which is
            -- cheaper than aggregating the whole table for a 20-row page.
            (SELECT COUNT(*)::int FROM sportsmeet.event_participants p WHERE p.event_id = e.id)
                AS current_participants,
            s.name AS sport_name,
            s.slug AS sport_slug,
            s.icon AS sport_icon,
            u.name AS host_name,
            u.photo_url AS host_photo_url
        FROM sportsmeet.events e
        JOIN sportsmeet.sports s ON s.id = e.sport_id
        JOIN sportsmeet.users u ON u.id = e.host_id;
        """;

    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Function first: the view does not use it, but registering both here keeps
        // "everything the queries depend on that EF cannot model" in one file.
        migrationBuilder.Sql(FunctionSql);
        migrationBuilder.Sql(ViewSql);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("DROP VIEW IF EXISTS sportsmeet.v_event_feed;");
        migrationBuilder.Sql("DROP FUNCTION IF EXISTS sportsmeet.haversine_km(double precision, double precision, double precision, double precision);");
    }
}
