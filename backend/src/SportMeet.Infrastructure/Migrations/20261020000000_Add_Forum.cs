using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;
using SportMeet.Infrastructure.Persistence;

#nullable disable

namespace SportMeet.Infrastructure.Migrations;

/// <summary>
/// Campus Forum, phase 1: categories and posts. Comments, likes and bookmarks arrive
/// in their own migration with the endpoints that write them.
///
///  1. forum_categories, seeded here. "All" is not a row - it is the absence of a
///     filter. Ids are left to the identity column so a later hand-inserted category
///     cannot collide with a seeded one.
///  2. forum_posts.author_user_id references users(id), the id every other table
///     uses; the Firebase uid stays on users.auth_uid. No author_name column: a copied
///     name goes stale on rename, and an anonymous post must not store one at all.
///     ON DELETE RESTRICT, so removing an account has to decide what becomes of
///     threads other people replied in rather than silently deleting them.
///  3. seq, an identity column, is the keyset tiebreak for paging. uuid ids are not
///     orderable in EF and timestamps can tie; seq is unique and insertion-ordered.
///  4. hot_score is stored and indexed with seq, so the Hot feed is an index scan.
///     Its time term is fixed at insert (see ForumRanking.HotScore), so no job ever
///     needs to re-score old rows.
///  5. CHECKs keep the counters non-negative and the body within the API's limit,
///     so a bug in a counter update cannot leave a post with -1 likes.
///
/// Hand-written in this folder's convention; the model snapshot is not regenerated.
/// </summary>
[DbContext(typeof(AppDbContext))]
[Migration("20261020000000_Add_Forum")]
public partial class Add_Forum : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "forum_categories",
            schema: "sportsmeet",
            columns: table => new
            {
                id = table.Column<int>(type: "integer", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                name = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                slug = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                sort_order = table.Column<int>(type: "integer", nullable: false),
            },
            constraints: table => table.PrimaryKey("pk_forum_categories", x => x.id));

        migrationBuilder.CreateIndex(
            name: "ux_forum_categories_slug",
            schema: "sportsmeet",
            table: "forum_categories",
            column: "slug",
            unique: true);

        // Plain SQL rather than InsertData: InsertData resolves column types from the
        // model snapshot, which these hand-written migrations do not update, so it
        // fails at SQL generation with "no entity type mapped to the table".
        migrationBuilder.Sql("""
            INSERT INTO sportsmeet.forum_categories (name, slug, sort_order) VALUES
                ('Q&A / Help', 'qa', 1),
                ('Marketplace & Jobs', 'marketplace', 2),
                ('General Chat', 'general', 3),
                ('Study Groups', 'study-groups', 4);
            """);

        migrationBuilder.CreateTable(
            name: "forum_posts",
            schema: "sportsmeet",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                seq = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                author_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                category_id = table.Column<int>(type: "integer", nullable: false),
                title = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                content = table.Column<string>(type: "text", nullable: false),
                is_anonymous = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                like_count = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                comment_count = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                view_count = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                hot_score = table.Column<double>(type: "double precision", nullable: false, defaultValue: 0.0),
                is_pinned = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_forum_posts", x => x.id);
                table.ForeignKey(
                    name: "fk_forum_posts_users_author_user_id",
                    column: x => x.author_user_id,
                    principalSchema: "sportsmeet",
                    principalTable: "users",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "fk_forum_posts_forum_categories_category_id",
                    column: x => x.category_id,
                    principalSchema: "sportsmeet",
                    principalTable: "forum_categories",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
                table.CheckConstraint(
                    "forum_posts_content_length_check",
                    "char_length(content) BETWEEN 1 AND 10000");
                table.CheckConstraint(
                    "forum_posts_counters_check",
                    "like_count >= 0 AND comment_count >= 0 AND view_count >= 0");
            });

        migrationBuilder.CreateIndex(
            name: "ux_forum_posts_seq",
            schema: "sportsmeet",
            table: "forum_posts",
            column: "seq",
            unique: true);
        migrationBuilder.CreateIndex(
            name: "ix_forum_posts_hot",
            schema: "sportsmeet",
            table: "forum_posts",
            columns: new[] { "hot_score", "seq" });
        migrationBuilder.CreateIndex(
            name: "ix_forum_posts_category_hot",
            schema: "sportsmeet",
            table: "forum_posts",
            columns: new[] { "category_id", "hot_score", "seq" });
        migrationBuilder.CreateIndex(
            name: "ix_forum_posts_category_seq",
            schema: "sportsmeet",
            table: "forum_posts",
            columns: new[] { "category_id", "seq" });
        migrationBuilder.CreateIndex(
            name: "ix_forum_posts_author_created",
            schema: "sportsmeet",
            table: "forum_posts",
            columns: new[] { "author_user_id", "created_at" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "forum_posts", schema: "sportsmeet");
        migrationBuilder.DropTable(name: "forum_categories", schema: "sportsmeet");
    }
}
