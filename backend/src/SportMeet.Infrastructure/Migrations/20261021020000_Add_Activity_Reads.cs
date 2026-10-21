using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using SportMeet.Infrastructure.Persistence;

#nullable disable

namespace SportMeet.Infrastructure.Migrations;

/// <summary>
/// Read marks for the Likes / New Followers / Comments tabs on the Messages page.
///
/// The activity itself is not copied anywhere: it is read live from forum_post_likes,
/// event_interests, user_follows and forum_comments. This table only records which
/// items a person has seen, keyed by a stable per-item string, one row per (user,
/// item). Rows cascade with the user.
///
/// Hand-written in this folder's convention; the model snapshot is not regenerated.
/// </summary>
[DbContext(typeof(AppDbContext))]
[Migration("20261021020000_Add_Activity_Reads")]
public partial class Add_Activity_Reads : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "activity_reads",
            schema: "sportsmeet",
            columns: table => new
            {
                user_id = table.Column<Guid>(type: "uuid", nullable: false),
                item_key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                read_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_activity_reads", x => new { x.user_id, x.item_key });
                table.ForeignKey(
                    name: "fk_activity_reads_users_user_id",
                    column: x => x.user_id,
                    principalSchema: "sportsmeet",
                    principalTable: "users",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "activity_reads", schema: "sportsmeet");
    }
}
