using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using SportMeet.Infrastructure.Persistence;

#nullable disable

namespace SportMeet.Infrastructure.Migrations;

/// <summary>
/// Event view tracking: the rows behind a host's "Views" number.
///
///  1. event_views (id, event_id, user_id nullable, viewed_at, viewed_on). An
///     append-only fact table rather than a counter column on events, for the same
///     reason current_participants is a COUNT over event_participants and not a
///     stored number: a counter needs locking on every page load, and a wrong one
///     cannot be recomputed afterwards while these rows can be.
///  2. viewed_on (date). The day the view belongs to, stored rather than derived at
///     query time so the "one view per person per event per day" rule can live in a
///     unique index. A rule enforced by a query has to be evaluated correctly twice -
///     once to decide, once to write - and two concurrent refreshes can both pass the
///     read. An index cannot be raced: the second insert is refused by the database.
///     UTC calendar day, because a day that moved with the viewer's timezone would
///     let one person view the same event twice in a single day of theirs.
///  3. The unique index is partial on user_id IS NOT NULL. Anonymous views have no
///     identity to deduplicate against, so they are recorded and counted but excluded
///     from the rule. Postgres never collides NULLs in a unique index anyway, so the
///     filter states the intent and keeps the index holding only rows it can enforce.
///  4. A plain event_id index for the count, which spans anonymous rows too and so
///     cannot be served by the partial unique index.
///
/// No CHECK constraint and no foreign key to the reviewer or host: user_id points at
/// whoever opened the page, on delete SET NULL so a closed account does not silently
/// lower a host's historical count. event_id cascades - views of a deleted event go
/// with the event.
///
/// No view change. v_event_feed does not select these columns and the browse feed
/// must not start counting page loads: the count belongs to one host looking at one
/// event, which is the only place it is ever read.
///
/// Hand-written in this folder's convention - the model snapshot is not regenerated
/// by these, so a fresh `dotnet ef migrations add` would offer to re-add what is
/// already here. See the note on 20261012000000_Add_Event_Join_Approval.
/// </summary>
[DbContext(typeof(AppDbContext))]
[Migration("20261014000000_Add_Event_Views")]
public partial class Add_Event_Views : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "event_views",
            schema: "sportsmeet",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                event_id = table.Column<Guid>(type: "uuid", nullable: false),
                user_id = table.Column<Guid>(type: "uuid", nullable: true),
                viewed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                viewed_on = table.Column<DateOnly>(type: "date", nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_event_views", x => x.id);
                table.ForeignKey(
                    name: "fk_event_views_events_event_id",
                    column: x => x.event_id,
                    principalSchema: "sportsmeet",
                    principalTable: "events",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "fk_event_views_users_user_id",
                    column: x => x.user_id,
                    principalSchema: "sportsmeet",
                    principalTable: "users",
                    principalColumn: "id",
                    onDelete: ReferentialAction.SetNull);
            });

        // The deduplication rule. Declared as a unique index rather than a UNIQUE
        // constraint so it can be partial - a constraint cannot be filtered, and
        // unfiltered it would either deduplicate anonymous views against each other
        // (wrong) or exist purely to not apply to them (misleading).
        migrationBuilder.CreateIndex(
            name: "ix_event_views_event_user_day",
            schema: "sportsmeet",
            table: "event_views",
            columns: new[] { "event_id", "user_id", "viewed_on" },
            unique: true,
            filter: "user_id IS NOT NULL");

        migrationBuilder.CreateIndex(
            name: "ix_event_views_event_id",
            schema: "sportsmeet",
            table: "event_views",
            column: "event_id");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // The whole table goes with the feature. There is nothing to preserve: the
        // rows are derived data with no other consumer, and a rollback that kept
        // them would leave a table no code reads.
        migrationBuilder.DropTable(
            name: "event_views",
            schema: "sportsmeet");
    }
}
