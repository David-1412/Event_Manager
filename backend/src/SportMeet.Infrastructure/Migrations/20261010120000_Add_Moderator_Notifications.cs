using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using SportMeet.Infrastructure.Persistence;

#nullable disable

namespace SportMeet.Infrastructure.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20261010120000_Add_Moderator_Notifications")]
public partial class Add_Moderator_Notifications : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropCheckConstraint(
            name: "users_role_check",
            schema: "sportsmeet",
            table: "users");

        migrationBuilder.AddCheckConstraint(
            name: "users_role_check",
            schema: "sportsmeet",
            table: "users",
            sql: "role IN ('Member', 'Moderator', 'Admin')");

        migrationBuilder.CreateTable(
            name: "notifications",
            schema: "sportsmeet",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                user_id = table.Column<Guid>(type: "uuid", nullable: false),
                event_id = table.Column<Guid>(type: "uuid", nullable: true),
                message = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                read_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_notifications", x => x.id);
                table.ForeignKey(
                    name: "fk_notifications_events_event_id",
                    column: x => x.event_id,
                    principalSchema: "sportsmeet",
                    principalTable: "events",
                    principalColumn: "id",
                    onDelete: ReferentialAction.SetNull);
                table.ForeignKey(
                    name: "fk_notifications_users_user_id",
                    column: x => x.user_id,
                    principalSchema: "sportsmeet",
                    principalTable: "users",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "ix_notifications_event_id",
            schema: "sportsmeet",
            table: "notifications",
            column: "event_id");

        migrationBuilder.CreateIndex(
            name: "ix_notifications_user_id_created_at",
            schema: "sportsmeet",
            table: "notifications",
            columns: new[] { "user_id", "created_at" });

        migrationBuilder.CreateIndex(
            name: "ix_notifications_user_id_read_at",
            schema: "sportsmeet",
            table: "notifications",
            columns: new[] { "user_id", "read_at" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "notifications",
            schema: "sportsmeet");

        migrationBuilder.DropCheckConstraint(
            name: "users_role_check",
            schema: "sportsmeet",
            table: "users");

        migrationBuilder.Sql(
            "UPDATE sportsmeet.users SET role = 'Member' WHERE role = 'Moderator';");

        migrationBuilder.AddCheckConstraint(
            name: "users_role_check",
            schema: "sportsmeet",
            table: "users",
            sql: "role IN ('Member', 'Admin')");
    }
}
