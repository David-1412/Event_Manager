using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using SportMeet.Infrastructure.Persistence;

#nullable disable

namespace SportMeet.Infrastructure.Migrations;

/// <summary>
/// Demo engagement: the admin tool that adds sample views and Interested marks.
///
/// <c>users.is_demo</c> marks the placeholder accounts (demo1..demo20) the tool
/// creates to own its Interested marks; the app hides them from everyone else.
/// <c>is_demo</c> on <c>event_views</c> and <c>forum_post_views</c> tags the anonymous
/// views the tool inserts, so "Remove demo engagement" can take back exactly what
/// it added and nothing a real reader did. Real views are written by EF, which
/// does not know the column, so they keep the default false.
///
/// Hand-written in this folder's convention; the model snapshot is not regenerated.
/// </summary>
[DbContext(typeof(AppDbContext))]
[Migration("20261023000000_Add_Demo_Accounts")]
public partial class Add_Demo_Accounts : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            ALTER TABLE sportsmeet.users ADD COLUMN IF NOT EXISTS is_demo boolean NOT NULL DEFAULT false;
            ALTER TABLE sportsmeet.event_views ADD COLUMN IF NOT EXISTS is_demo boolean NOT NULL DEFAULT false;
            ALTER TABLE sportsmeet.forum_post_views ADD COLUMN IF NOT EXISTS is_demo boolean NOT NULL DEFAULT false;
            CREATE INDEX IF NOT EXISTS ix_users_is_demo ON sportsmeet.users (id) WHERE is_demo;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DROP INDEX IF EXISTS sportsmeet.ix_users_is_demo;
            ALTER TABLE sportsmeet.forum_post_views DROP COLUMN IF EXISTS is_demo;
            ALTER TABLE sportsmeet.event_views DROP COLUMN IF EXISTS is_demo;
            ALTER TABLE sportsmeet.users DROP COLUMN IF EXISTS is_demo;
            """);
    }
}
