using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using SportMeet.Infrastructure.Persistence;

#nullable disable

namespace SportMeet.Infrastructure.Migrations;

/// <summary>
/// Monash Pulse by views, and Interested marked automatically on a first view.
///
///  1. forum_post_views, mirroring event_views: one row per opening, deduplicated to
///     one per signed-in reader per post per UTC day by a partial unique index.
///     forum_posts.view_count is incremented only when a row lands, so it is a
///     monotonic total - un-marking Interested never touches it.
///  2. event_interests.is_auto: an interest created by a first view rather than a
///     tap. Counted everywhere, but excluded from the public profile activity feed,
///     which would otherwise publish every event a person opened.
///  3. viewed_at indexes on both view tables for "viewed within this period".
///
/// Hand-written in this folder's convention; the model snapshot is not regenerated.
/// </summary>
[DbContext(typeof(AppDbContext))]
[Migration("20261021010000_Add_Views_And_Auto_Interest")]
public partial class Add_Views_And_Auto_Interest : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "forum_post_views",
            schema: "sportsmeet",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                post_id = table.Column<Guid>(type: "uuid", nullable: false),
                user_id = table.Column<Guid>(type: "uuid", nullable: true),
                viewed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                viewed_on = table.Column<DateOnly>(type: "date", nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_forum_post_views", x => x.id);
                table.ForeignKey(
                    name: "fk_forum_post_views_forum_posts_post_id",
                    column: x => x.post_id,
                    principalSchema: "sportsmeet",
                    principalTable: "forum_posts",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "fk_forum_post_views_users_user_id",
                    column: x => x.user_id,
                    principalSchema: "sportsmeet",
                    principalTable: "users",
                    principalColumn: "id",
                    onDelete: ReferentialAction.SetNull);
            });

        migrationBuilder.CreateIndex(
            name: "ux_forum_post_views_daily",
            schema: "sportsmeet",
            table: "forum_post_views",
            columns: new[] { "post_id", "user_id", "viewed_on" },
            unique: true,
            filter: "user_id IS NOT NULL");
        migrationBuilder.CreateIndex(
            name: "ix_forum_post_views_viewed_at",
            schema: "sportsmeet",
            table: "forum_post_views",
            column: "viewed_at");

        migrationBuilder.AddColumn<bool>(
            name: "is_auto",
            schema: "sportsmeet",
            table: "event_interests",
            type: "boolean",
            nullable: false,
            defaultValue: false);

        migrationBuilder.CreateIndex(
            name: "ix_event_views_viewed_at",
            schema: "sportsmeet",
            table: "event_views",
            column: "viewed_at");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(name: "ix_event_views_viewed_at", schema: "sportsmeet", table: "event_views");
        migrationBuilder.DropColumn(name: "is_auto", schema: "sportsmeet", table: "event_interests");
        migrationBuilder.DropTable(name: "forum_post_views", schema: "sportsmeet");
    }
}
