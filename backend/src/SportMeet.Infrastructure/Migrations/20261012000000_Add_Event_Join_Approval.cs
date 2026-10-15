using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using SportMeet.Infrastructure.Persistence;

#nullable disable

namespace SportMeet.Infrastructure.Migrations;

/// <summary>
/// Host-moderated participation: a host can require that joining be approved, and
/// the requests that produces are kept until decided.
///
///  1. events.requires_join_approval (boolean, NOT NULL, DEFAULT false). The
///     DEFAULT is the entire backward-compatibility story: every event that
///     predates this column reads false and therefore keeps joining immediately,
///     with no data to backfill and no code path to guard.
///  2. event_join_requests, keyed (event_id, user_id) like event_participants and
///     event_interests so one person holds exactly one standing per event, with
///     status text under a CHECK in the Pending / Approved / Rejected vocabulary.
///     Decided requests are kept rather than deleted: the host's list is the
///     audit trail, and "the host said no" needs to survive.
///  3. Two indexes. The partial one on pending requests backs the host's queue in
///     oldest-first order without a sort; decided rows leave it, so it does not
///     grow with history. The user index backs the member-side read.
///  4. v_event_feed gains requires_join_approval, so the detail page renders the
///     Pending-approval join state from the single round-trip it already makes.
///     The view is a keyless projection, so this is what keeps that read at one
///     query instead of adding a second per event.
///
/// Approval writes an event_participants row in the same transaction, so
/// current_participants - which the view derives by COUNT - becomes correct with
/// no change here. A pending request is deliberately absent from every count: it
/// reserves nothing, matching Interested.
///
/// Same view structure as the migrations before it: drop, change the tables,
/// recreate. EF cannot snapshot a view, so the view half produces no model diff
/// and is spelled out here, and the new SQL is EventViewSql.AddEventJoinApprovalView
/// rather than the shape earlier migrations replay.
/// </summary>
[DbContext(typeof(AppDbContext))]
[Migration("20261012000000_Add_Event_Join_Approval")]
public partial class Add_Event_Join_Approval : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // The view reads events, and CREATE OR REPLACE cannot resolve a column its
        // SELECT references that the table does not have yet - so it comes down first.
        migrationBuilder.Sql("DROP VIEW sportsmeet.v_event_feed;");

        migrationBuilder.AddColumn<bool>(
            name: "requires_join_approval",
            schema: "sportsmeet",
            table: "events",
            type: "boolean",
            nullable: false,
            defaultValue: false);

        migrationBuilder.CreateTable(
            name: "event_join_requests",
            schema: "sportsmeet",
            columns: table => new
            {
                event_id = table.Column<Guid>(type: "uuid", nullable: false),
                user_id = table.Column<Guid>(type: "uuid", nullable: false),
                status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                requested_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                decided_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_event_join_requests", x => new { x.event_id, x.user_id });
                table.ForeignKey(
                    name: "fk_event_join_requests_events_event_id",
                    column: x => x.event_id,
                    principalSchema: "sportsmeet",
                    principalTable: "events",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "fk_event_join_requests_users_user_id",
                    column: x => x.user_id,
                    principalSchema: "sportsmeet",
                    principalTable: "users",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        // PascalCase spellings: the value converter writes Enum.ToString(), so a
        // CHECK of 'pending' would reject every insert.
        migrationBuilder.AddCheckConstraint(
            name: "event_join_requests_status_check",
            schema: "sportsmeet",
            table: "event_join_requests",
            sql: "status IN ('Pending', 'Approved', 'Rejected')");

        // The host's queue: pending requests for one event, oldest first. Partial so
        // it holds only what the host can still act on.
        migrationBuilder.CreateIndex(
            name: "ix_event_join_requests_event_pending",
            schema: "sportsmeet",
            table: "event_join_requests",
            columns: new[] { "event_id", "requested_at" },
            filter: "status = 'Pending'");

        migrationBuilder.CreateIndex(
            name: "ix_event_join_requests_user_id",
            schema: "sportsmeet",
            table: "event_join_requests",
            column: "user_id");

        migrationBuilder.Sql(EventViewSql.AddEventJoinApprovalView);
    }
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("DROP VIEW sportsmeet.v_event_feed;");

        // The requests table goes first: dropping the flag while the requests
        // survived would leave decided-against members with no explanation and no
        // screen that can show them.
        migrationBuilder.DropTable(
            name: "event_join_requests",
            schema: "sportsmeet");

        migrationBuilder.DropColumn(
            name: "requires_join_approval",
            schema: "sportsmeet",
            table: "events");

        // Back to the shape Admin_Event_Management left behind.
        migrationBuilder.Sql(AdminManagementViewSql);
    }

    /// <summary>The v_event_feed shape as Admin_Event_Management created it, needed
    /// only by Down(). A private constant because <see cref="EventViewSql"/> freezes
    /// its own strings at earlier boundaries; this is the one that immediately
    /// precedes this migration.</summary>
    private const string AdminManagementViewSql = """
        CREATE OR REPLACE VIEW sportsmeet.v_event_feed AS
        SELECT
            e.id, e.host_id, e.title, e.description, e.sport_id, e.venue_name,
            e.address, e.place_id, e.lat, e.lng, e.timezone, e.start_at, e.end_at,
            e.max_participants, e.skill_level, e.cost, e.status, e.cancelled_at,
            e.rejection_reason, e.created_at, e.updated_at,
            (SELECT COUNT(*)::int FROM sportsmeet.event_participants p WHERE p.event_id = e.id)
                AS current_participants,
            s.name AS sport_name, s.slug AS sport_slug, s.icon AS sport_icon,
            u.name AS host_name, u.photo_url AS host_photo_url,
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
