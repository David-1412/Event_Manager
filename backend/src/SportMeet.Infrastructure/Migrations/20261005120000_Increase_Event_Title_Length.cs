using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using SportMeet.Infrastructure.Persistence;

#nullable disable

namespace SportMeet.Infrastructure.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20261005120000_Increase_Event_Title_Length")]
public partial class Increase_Event_Title_Length : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("DROP VIEW sportsmeet.v_event_feed;");
        migrationBuilder.DropCheckConstraint(
            name: "events_title_length_check",
            schema: "sportsmeet",
            table: "events");

        migrationBuilder.AlterColumn<string>(
            name: "title",
            schema: "sportsmeet",
            table: "events",
            type: "character varying(120)",
            maxLength: 120,
            nullable: false,
            oldClrType: typeof(string),
            oldType: "character varying(80)",
            oldMaxLength: 80);

        migrationBuilder.AddCheckConstraint(
            name: "events_title_length_check",
            schema: "sportsmeet",
            table: "events",
            sql: "char_length(title) BETWEEN 3 AND 120");
        migrationBuilder.Sql(EventViewSql.Definition);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("DROP VIEW sportsmeet.v_event_feed;");
        migrationBuilder.DropCheckConstraint(
            name: "events_title_length_check",
            schema: "sportsmeet",
            table: "events");

        migrationBuilder.AlterColumn<string>(
            name: "title",
            schema: "sportsmeet",
            table: "events",
            type: "character varying(80)",
            maxLength: 80,
            nullable: false,
            oldClrType: typeof(string),
            oldType: "character varying(120)",
            oldMaxLength: 120);

        migrationBuilder.AddCheckConstraint(
            name: "events_title_length_check",
            schema: "sportsmeet",
            table: "events",
            sql: "char_length(title) BETWEEN 3 AND 80");
        migrationBuilder.Sql(EventViewSql.Definition);
    }
}