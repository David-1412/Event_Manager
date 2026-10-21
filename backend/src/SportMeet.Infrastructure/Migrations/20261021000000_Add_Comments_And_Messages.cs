using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;
using SportMeet.Infrastructure.Persistence;

#nullable disable

namespace SportMeet.Infrastructure.Migrations;

/// <summary>
/// Forum comments and direct messages.
///
///  1. forum_comments. author_user_id references users(id) like every other table -
///     not a Firebase uid, and no copied author_name (stale on rename, and an
///     anonymous comment must not store one). Replies are one level deep; the
///     service enforces that the parent is a top-level comment on the same post.
///     Soft delete (deleted_at) so a removed comment's replies keep their context.
///  2. conversations: one row per PAIR of people. The pair is stored ordered
///     (user_a_id &lt; user_b_id, a CHECK) under a unique key, so the database itself
///     guarantees one thread per pair. A UNIQUE (p1, p2, event_id) would not: NULL
///     event ids are distinct in Postgres, so the same two people could open any
///     number of "no event" threads. Read state is a last-read timestamp per side.
///  3. direct_messages, paged by an identity seq (the keyset), indexed with the
///     conversation. Both foreign keys cascade: a message has no meaning once its
///     conversation or its sender's account is gone.
///  4. CHECKs cap lengths (comments 5,000, messages 2,000 characters) as a backstop to
///     the services' readable 422s.
///
/// Hand-written in this folder's convention; the model snapshot is not regenerated.
/// </summary>
[DbContext(typeof(AppDbContext))]
[Migration("20261021000000_Add_Comments_And_Messages")]
public partial class Add_Comments_And_Messages : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "forum_comments",
            schema: "sportsmeet",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                post_id = table.Column<Guid>(type: "uuid", nullable: false),
                author_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                parent_comment_id = table.Column<Guid>(type: "uuid", nullable: true),
                content = table.Column<string>(type: "text", nullable: false),
                is_anonymous = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_forum_comments", x => x.id);
                table.ForeignKey(
                    name: "fk_forum_comments_forum_posts_post_id",
                    column: x => x.post_id,
                    principalSchema: "sportsmeet",
                    principalTable: "forum_posts",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "fk_forum_comments_users_author_user_id",
                    column: x => x.author_user_id,
                    principalSchema: "sportsmeet",
                    principalTable: "users",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "fk_forum_comments_forum_comments_parent_comment_id",
                    column: x => x.parent_comment_id,
                    principalSchema: "sportsmeet",
                    principalTable: "forum_comments",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
                table.CheckConstraint(
                    "forum_comments_content_length_check",
                    "char_length(content) BETWEEN 1 AND 5000");
                table.CheckConstraint(
                    "forum_comments_not_own_parent_check",
                    "parent_comment_id IS NULL OR parent_comment_id <> id");
            });

        migrationBuilder.CreateIndex(
            name: "ix_forum_comments_post_created",
            schema: "sportsmeet",
            table: "forum_comments",
            columns: new[] { "post_id", "created_at" });

        migrationBuilder.CreateTable(
            name: "conversations",
            schema: "sportsmeet",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                user_a_id = table.Column<Guid>(type: "uuid", nullable: false),
                user_b_id = table.Column<Guid>(type: "uuid", nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                last_message_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                last_message_preview = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                last_sender_id = table.Column<Guid>(type: "uuid", nullable: true),
                user_a_last_read_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                user_b_last_read_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_conversations", x => x.id);
                table.ForeignKey(
                    name: "fk_conversations_users_user_a_id",
                    column: x => x.user_a_id,
                    principalSchema: "sportsmeet",
                    principalTable: "users",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "fk_conversations_users_user_b_id",
                    column: x => x.user_b_id,
                    principalSchema: "sportsmeet",
                    principalTable: "users",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
                table.CheckConstraint("conversations_pair_ordered_check", "user_a_id < user_b_id");
            });

        migrationBuilder.CreateIndex(
            name: "ux_conversations_pair",
            schema: "sportsmeet",
            table: "conversations",
            columns: new[] { "user_a_id", "user_b_id" },
            unique: true);
        migrationBuilder.CreateIndex(
            name: "ix_conversations_user_b_last",
            schema: "sportsmeet",
            table: "conversations",
            columns: new[] { "user_b_id", "last_message_at" });

        migrationBuilder.CreateTable(
            name: "direct_messages",
            schema: "sportsmeet",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                seq = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                conversation_id = table.Column<Guid>(type: "uuid", nullable: false),
                sender_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                content = table.Column<string>(type: "text", nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_direct_messages", x => x.id);
                table.ForeignKey(
                    name: "fk_direct_messages_conversations_conversation_id",
                    column: x => x.conversation_id,
                    principalSchema: "sportsmeet",
                    principalTable: "conversations",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "fk_direct_messages_users_sender_user_id",
                    column: x => x.sender_user_id,
                    principalSchema: "sportsmeet",
                    principalTable: "users",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
                table.CheckConstraint(
                    "direct_messages_content_length_check",
                    "char_length(content) BETWEEN 1 AND 2000");
            });

        migrationBuilder.CreateIndex(
            name: "ix_direct_messages_conversation_seq",
            schema: "sportsmeet",
            table: "direct_messages",
            columns: new[] { "conversation_id", "seq" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "direct_messages", schema: "sportsmeet");
        migrationBuilder.DropTable(name: "conversations", schema: "sportsmeet");
        migrationBuilder.DropTable(name: "forum_comments", schema: "sportsmeet");
    }
}
