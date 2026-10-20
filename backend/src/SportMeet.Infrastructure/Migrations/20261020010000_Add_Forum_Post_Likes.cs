using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using SportMeet.Infrastructure.Persistence;

#nullable disable

namespace SportMeet.Infrastructure.Migrations;

/// <summary>
/// "Interested" on forum posts, and the indexes the Monash Pulse trending list reads.
///
///  1. forum_post_likes (post_id, user_id, created_at). The composite primary key is
///     the once-per-person rule. Both foreign keys cascade: an interest means nothing
///     once the post or the account is gone.
///  2. forum_posts.like_count stays the denormalised count the feeds sort and show.
///     It is recomputed from this table in the same statement that refreshes
///     hot_score (see ForumRepository.RefreshEngagementAsync), so it cannot drift
///     the way a +1/-1 counter does after a retried request.
///  3. Time indexes on forum_post_likes.created_at and event_interests.interested_at
///     (the entity's CreatedAt is mapped to that column). Trending counts
///     the interest rows created inside a window ("today", "past 3 days", "this
///     week"), so both tables are filtered on created_at first.
///
/// Hand-written in this folder's convention; the model snapshot is not regenerated.
/// </summary>
[DbContext(typeof(AppDbContext))]
[Migration("20261020010000_Add_Forum_Post_Likes")]
public partial class Add_Forum_Post_Likes : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "forum_post_likes",
            schema: "sportsmeet",
            columns: table => new
            {
                post_id = table.Column<Guid>(type: "uuid", nullable: false),
                user_id = table.Column<Guid>(type: "uuid", nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_forum_post_likes", x => new { x.post_id, x.user_id });
                table.ForeignKey(
                    name: "fk_forum_post_likes_forum_posts_post_id",
                    column: x => x.post_id,
                    principalSchema: "sportsmeet",
                    principalTable: "forum_posts",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "fk_forum_post_likes_users_user_id",
                    column: x => x.user_id,
                    principalSchema: "sportsmeet",
                    principalTable: "users",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "ix_forum_post_likes_user_id",
            schema: "sportsmeet",
            table: "forum_post_likes",
            column: "user_id");
        migrationBuilder.CreateIndex(
            name: "ix_forum_post_likes_created_at",
            schema: "sportsmeet",
            table: "forum_post_likes",
            column: "created_at");
        migrationBuilder.CreateIndex(
            name: "ix_event_interests_interested_at",
            schema: "sportsmeet",
            table: "event_interests",
            column: "interested_at");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "ix_event_interests_interested_at",
            schema: "sportsmeet",
            table: "event_interests");
        migrationBuilder.DropTable(name: "forum_post_likes", schema: "sportsmeet");
    }
}
