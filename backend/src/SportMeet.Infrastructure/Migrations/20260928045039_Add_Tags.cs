using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace SportMeet.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Add_Tags : Migration
    {
        /// <summary>
        /// The v_event_feed definition as of the tag change: LEFT JOIN on sports
        /// (sport_id became nullable) plus the comma-joined tag aggregate.
        ///
        /// Duplicated from Init_Views rather than referenced, and it MUST be kept
        /// in step by hand: EF cannot snapshot a view, so editing Init_Views does
        /// not regenerate anything here, and a live database that ran Init_Views
        /// only ever picks up the new shape through this statement.
        /// </summary>
        private const string ViewSql = EventViewSql.Definition;

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "events_sport_start_idx",
                schema: "sportsmeet",
                table: "events");

            migrationBuilder.AlterColumn<int>(
                name: "sport_id",
                schema: "sportsmeet",
                table: "events",
                type: "integer",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AlterColumn<string>(
                name: "skill_level",
                schema: "sportsmeet",
                table: "events",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(20)",
                oldMaxLength: 20);

            migrationBuilder.CreateTable(
                name: "tags",
                schema: "sportsmeet",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    name = table.Column<string>(type: "character varying(25)", maxLength: 25, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tags", x => x.id);
                    table.CheckConstraint("tags_name_length_check", "char_length(name) BETWEEN 2 AND 25");
                    table.CheckConstraint("tags_name_lower_check", "name = lower(name)");
                });

            migrationBuilder.CreateTable(
                name: "event_tags",
                schema: "sportsmeet",
                columns: table => new
                {
                    event_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tag_id = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_event_tags", x => new { x.event_id, x.tag_id });
                    table.ForeignKey(
                        name: "fk_event_tags_events_event_id",
                        column: x => x.event_id,
                        principalSchema: "sportsmeet",
                        principalTable: "events",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_event_tags_tags_tag_id",
                        column: x => x.tag_id,
                        principalSchema: "sportsmeet",
                        principalTable: "tags",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_events_sport_id",
                schema: "sportsmeet",
                table: "events",
                column: "sport_id");

            migrationBuilder.CreateIndex(
                name: "event_tags_tag_id_idx",
                schema: "sportsmeet",
                table: "event_tags",
                column: "tag_id");

            migrationBuilder.CreateIndex(
                name: "tags_name_key",
                schema: "sportsmeet",
                table: "tags",
                column: "name",
                unique: true);

            // Last, not first: the replacement view's SELECT references tags and
            // event_tags, and Postgres parses a view body when the view is created,
            // so this fails 42P01 if it runs before those tables exist. Verified
            // against a real fresh database rather than assumed.
            migrationBuilder.Sql(ViewSql);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Restore the pre-tag view before dropping the tables it no longer
            // references; the current definition's subquery selects from event_tags,
            // so leaving it in place would leave the view broken mid-rollback.
            migrationBuilder.Sql(EventViewSql.PreTagsDefinition);

            migrationBuilder.DropTable(
                name: "event_tags",
                schema: "sportsmeet");

            migrationBuilder.DropTable(
                name: "tags",
                schema: "sportsmeet");

            migrationBuilder.DropIndex(
                name: "ix_events_sport_id",
                schema: "sportsmeet",
                table: "events");

            migrationBuilder.AlterColumn<int>(
                name: "sport_id",
                schema: "sportsmeet",
                table: "events",
                type: "integer",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "skill_level",
                schema: "sportsmeet",
                table: "events",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(20)",
                oldMaxLength: 20,
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "events_sport_start_idx",
                schema: "sportsmeet",
                table: "events",
                columns: new[] { "sport_id", "start_at" });
        }
    }
}
