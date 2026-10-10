using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using SportMeet.Infrastructure.Persistence;

#nullable disable

namespace SportMeet.Infrastructure.Migrations;

/// <summary>
/// Widens <c>users_role_check</c> to admit <c>'Creator'</c>: a trusted organiser
/// whose own public events publish without review, with no moderation or
/// user-management authority.
///
/// Drop and re-add rather than alter: Postgres has no ALTER CHECK, and every
/// existing row satisfies the wider list, so no data has to move on the way up.
///
/// <c>user_role_audit.from_role</c> / <c>to_role</c> carry no role check (only the
/// no-op guard), so audit lines naming Creator need no schema change. No view
/// change either: <c>v_event_feed.host_role</c> passes the column through as text.
/// </summary>
[DbContext(typeof(AppDbContext))]
[Migration("20261010120000_Add_Creator_Role")]
public partial class Add_Creator_Role : Migration
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
            sql: "role IN ('Member', 'Creator', 'Admin')");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // The narrower check would reject every Creator row, so they go back to
        // Member first - the safe direction, since it removes a privilege rather
        // than inventing one. Their audit lines stay as the record that it happened.
        migrationBuilder.Sql(
            """
            UPDATE sportsmeet.users SET role = 'Member' WHERE role = 'Creator';
            """);

        migrationBuilder.DropCheckConstraint(
            name: "users_role_check",
            schema: "sportsmeet",
            table: "users");

        migrationBuilder.AddCheckConstraint(
            name: "users_role_check",
            schema: "sportsmeet",
            table: "users",
            sql: "role IN ('Member', 'Admin')");
    }
}
