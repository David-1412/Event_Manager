using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using SportMeet.Infrastructure.Persistence;

#nullable disable

namespace SportMeet.Infrastructure.Migrations;

/// <summary>
/// Edit and delete state on forum posts, forum comments and events.
///
/// <c>edited_at</c>: when the author last changed the content, null if never. The
/// "(edited)" label reads it.
///
/// <c>is_deleted</c>: a boolean view of the existing <c>deleted_at</c> soft-delete
/// timestamp, as a STORED generated column. Generated rather than a second column the
/// code writes, so the flag and the timestamp can never disagree - every existing
/// soft delete already sets deleted_at, and every query that filters on it keeps
/// working unchanged. The application keeps writing deleted_at only; EF does not map
/// is_deleted, which is there for SQL readers and reports.
///
/// The event feed view is not touched: its columns are listed explicitly, so a new
/// table column does not reach it.
///
/// Hand-written in this folder's convention; the model snapshot is not regenerated.
/// </summary>
[DbContext(typeof(AppDbContext))]
[Migration("20261022010000_Add_Edit_And_Delete_State")]
public partial class Add_Edit_And_Delete_State : Migration
{
    private static readonly string[] Tables = ["forum_posts", "forum_comments", "events"];

    protected override void Up(MigrationBuilder migrationBuilder)
    {
        foreach (var table in Tables)
        {
            migrationBuilder.Sql($"""
                ALTER TABLE sportsmeet.{table}
                    ADD COLUMN IF NOT EXISTS edited_at timestamp with time zone NULL,
                    ADD COLUMN IF NOT EXISTS is_deleted boolean
                        GENERATED ALWAYS AS (deleted_at IS NOT NULL) STORED;
                """);
        }
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        foreach (var table in Tables)
        {
            migrationBuilder.Sql($"""
                ALTER TABLE sportsmeet.{table}
                    DROP COLUMN IF EXISTS is_deleted,
                    DROP COLUMN IF EXISTS edited_at;
                """);
        }
    }
}
