using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SportMeet.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Add_User_Owned_Drafts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "event_drafts_status_check",
                schema: "sportsmeet",
                table: "event_drafts");

            migrationBuilder.AddColumn<Guid>(
                name: "owner_user_id",
                schema: "sportsmeet",
                table: "ingested_emails",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "user_id",
                schema: "sportsmeet",
                table: "event_drafts",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.CreateIndex(
                name: "event_drafts_user_pending_idx",
                schema: "sportsmeet",
                table: "event_drafts",
                columns: new[] { "user_id", "created_at" },
                filter: "status = 'Pending'");

            migrationBuilder.AddCheckConstraint(
                name: "event_drafts_status_check",
                schema: "sportsmeet",
                table: "event_drafts",
                sql: "status IN ('Pending', 'Approved', 'Rejected', 'Duplicate', 'Deleted')");

            // ---- Backfill before the FK is added ----
            // AddColumn gave every existing draft the Guid-zero placeholder default,
            // which is not a users row. Ensure a placeholder owner exists (idempotent,
            // so re-running or an already-seeded DB is fine) and repoint every draft to
            // it, so the FK below holds whether the database was empty or already had
            // pre-user drafts. New drafts always carry a real owner from the service.
            migrationBuilder.Sql(
                """
                INSERT INTO sportsmeet.users (id, auth_uid, name, email, photo_url)
                VALUES ('00000000-0000-0000-0000-000000000000', NULL, 'Imported Drafts', NULL, NULL)
                ON CONFLICT (id) DO NOTHING;
                """);
            migrationBuilder.Sql(
                "UPDATE sportsmeet.event_drafts SET user_id = '00000000-0000-0000-0000-000000000000';");

            migrationBuilder.AddForeignKey(
                name: "fk_event_drafts_users_user_id",
                schema: "sportsmeet",

                table: "event_drafts",
                column: "user_id",
                principalSchema: "sportsmeet",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }


        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_event_drafts_users_user_id",
                schema: "sportsmeet",
                table: "event_drafts");

            migrationBuilder.DropIndex(
                name: "event_drafts_user_pending_idx",
                schema: "sportsmeet",
                table: "event_drafts");

            migrationBuilder.DropCheckConstraint(
                name: "event_drafts_status_check",
                schema: "sportsmeet",
                table: "event_drafts");

            migrationBuilder.DropColumn(
                name: "owner_user_id",
                schema: "sportsmeet",
                table: "ingested_emails");

            migrationBuilder.DropColumn(
                name: "user_id",
                schema: "sportsmeet",
                table: "event_drafts");

            migrationBuilder.AddCheckConstraint(
                name: "event_drafts_status_check",
                schema: "sportsmeet",
                table: "event_drafts",
                sql: "status IN ('Pending', 'Approved', 'Rejected', 'Duplicate')");
        }
    }
}
