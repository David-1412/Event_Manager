using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using SportMeet.Infrastructure.Persistence;

#nullable disable

namespace SportMeet.Infrastructure.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20261010220000_Expand_Notification_Inbox")]
public partial class Expand_Notification_Inbox : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "title",
            schema: "sportsmeet",
            table: "notifications",
            type: "character varying(120)",
            maxLength: 120,
            nullable: false,
            defaultValue: "Event update");

        migrationBuilder.AddColumn<string>(
            name: "type",
            schema: "sportsmeet",
            table: "notifications",
            type: "character varying(40)",
            maxLength: 40,
            nullable: false,
            defaultValue: "EventApproved");

        migrationBuilder.AddColumn<string>(
            name: "link",
            schema: "sportsmeet",
            table: "notifications",
            type: "character varying(2048)",
            maxLength: 2048,
            nullable: true);

        migrationBuilder.AddColumn<bool>(
            name: "read",
            schema: "sportsmeet",
            table: "notifications",
            type: "boolean",
            nullable: false,
            defaultValue: false);

        migrationBuilder.Sql("""
            UPDATE sportsmeet.notifications
               SET link = CASE WHEN event_id IS NULL THEN NULL ELSE '/events/' || event_id::text END,
                   read = read_at IS NOT NULL;
            """);

        migrationBuilder.DropIndex(
            name: "ix_notifications_event_id",
            schema: "sportsmeet",
            table: "notifications");

        migrationBuilder.DropIndex(
            name: "ix_notifications_user_id_read_at",
            schema: "sportsmeet",
            table: "notifications");

        migrationBuilder.DropForeignKey(
            name: "fk_notifications_events_event_id",
            schema: "sportsmeet",
            table: "notifications");

        migrationBuilder.DropColumn(
            name: "event_id",
            schema: "sportsmeet",
            table: "notifications");

        migrationBuilder.DropColumn(
            name: "read_at",
            schema: "sportsmeet",
            table: "notifications");

        migrationBuilder.AlterColumn<string>(
            name: "title",
            schema: "sportsmeet",
            table: "notifications",
            type: "character varying(120)",
            maxLength: 120,
            nullable: false,
            oldClrType: typeof(string),
            oldType: "character varying(120)",
            oldMaxLength: 120,
            oldDefaultValue: "Event update");

        migrationBuilder.AlterColumn<string>(
            name: "type",
            schema: "sportsmeet",
            table: "notifications",
            type: "character varying(40)",
            maxLength: 40,
            nullable: false,
            oldClrType: typeof(string),
            oldType: "character varying(40)",
            oldMaxLength: 40,
            oldDefaultValue: "EventApproved");

        migrationBuilder.CreateIndex(
            name: "ix_notifications_user_id_read",
            schema: "sportsmeet",
            table: "notifications",
            columns: new[] { "user_id", "read" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "ix_notifications_user_id_read",
            schema: "sportsmeet",
            table: "notifications");

        migrationBuilder.AddColumn<Guid>(
            name: "event_id",
            schema: "sportsmeet",
            table: "notifications",
            type: "uuid",
            nullable: true);

        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "read_at",
            schema: "sportsmeet",
            table: "notifications",
            type: "timestamp with time zone",
            nullable: true);

        migrationBuilder.Sql("""
            UPDATE sportsmeet.notifications
               SET event_id = CASE
                     WHEN link ~ '^/events/[0-9a-fA-F-]{36}$' THEN substring(link from 9)::uuid
                     ELSE NULL
                   END,
                   read_at = CASE WHEN read THEN created_at ELSE NULL END;
            """);

        migrationBuilder.DropColumn(
            name: "title",
            schema: "sportsmeet",
            table: "notifications");
        migrationBuilder.DropColumn(
            name: "type",
            schema: "sportsmeet",
            table: "notifications");
        migrationBuilder.DropColumn(
            name: "link",
            schema: "sportsmeet",
            table: "notifications");
        migrationBuilder.DropColumn(
            name: "read",
            schema: "sportsmeet",
            table: "notifications");

        migrationBuilder.AddForeignKey(
            name: "fk_notifications_events_event_id",
            schema: "sportsmeet",
            table: "notifications",
            column: "event_id",
            principalSchema: "sportsmeet",
            principalTable: "events",
            principalColumn: "id",
            onDelete: ReferentialAction.SetNull);

        migrationBuilder.CreateIndex(
            name: "ix_notifications_event_id",
            schema: "sportsmeet",
            table: "notifications",
            column: "event_id");

        migrationBuilder.CreateIndex(
            name: "ix_notifications_user_id_read_at",
            schema: "sportsmeet",
            table: "notifications",
            columns: new[] { "user_id", "read_at" });
    }
}
