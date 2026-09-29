using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace SportMeet.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Init_Tables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "sportsmeet");

            migrationBuilder.CreateTable(
                name: "sports",
                schema: "sportsmeet",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    name = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    slug = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    icon = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sports", x => x.id);
                    table.CheckConstraint("sports_slug_lower_check", "slug = lower(slug)");
                });

            migrationBuilder.CreateTable(
                name: "users",
                schema: "sportsmeet",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    auth_uid = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    email = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: true),
                    photo_url = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_users", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "events",
                schema: "sportsmeet",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    host_id = table.Column<Guid>(type: "uuid", nullable: false),
                    title = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    sport_id = table.Column<int>(type: "integer", nullable: false),
                    venue_name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    address = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                    place_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    lat = table.Column<double>(type: "double precision", nullable: false),
                    lng = table.Column<double>(type: "double precision", nullable: false),
                    timezone = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false, defaultValue: "Australia/Melbourne"),
                    start_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    end_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    max_participants = table.Column<int>(type: "integer", nullable: false),
                    skill_level = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    cost = table.Column<decimal>(type: "numeric(6,2)", precision: 6, scale: 2, nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    cancelled_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_events", x => x.id);
                    table.CheckConstraint("events_cost_check", "cost IS NULL OR cost >= 0");
                    table.CheckConstraint("events_description_length_check", "description IS NULL OR char_length(description) <= 2000");
                    table.CheckConstraint("events_lat_check", "lat BETWEEN -90 AND 90");
                    table.CheckConstraint("events_lng_check", "lng BETWEEN -180 AND 180");
                    table.CheckConstraint("events_participants_check", "max_participants BETWEEN 2 AND 50");
                    table.CheckConstraint("events_skill_level_check", "skill_level IN ('Beginner', 'Intermediate', 'Advanced')");
                    table.CheckConstraint("events_status_check", "status IN ('Scheduled', 'Cancelled', 'Completed')");
                    table.CheckConstraint("events_time_range_check", "end_at > start_at");
                    table.CheckConstraint("events_title_length_check", "char_length(title) BETWEEN 3 AND 80");
                    table.CheckConstraint("events_venue_length_check", "char_length(venue_name) BETWEEN 1 AND 120");
                    table.ForeignKey(
                        name: "fk_events_sports_sport_id",
                        column: x => x.sport_id,
                        principalSchema: "sportsmeet",
                        principalTable: "sports",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_events_users_host_id",
                        column: x => x.host_id,
                        principalSchema: "sportsmeet",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "event_participants",
                schema: "sportsmeet",
                columns: table => new
                {
                    event_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    joined_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_event_participants", x => new { x.event_id, x.user_id });
                    table.ForeignKey(
                        name: "fk_event_participants_events_event_id",
                        column: x => x.event_id,
                        principalSchema: "sportsmeet",
                        principalTable: "events",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_event_participants_users_user_id",
                        column: x => x.user_id,
                        principalSchema: "sportsmeet",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_event_participants_user_id",
                schema: "sportsmeet",
                table: "event_participants",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "events_geo_bbox_idx",
                schema: "sportsmeet",
                table: "events",
                columns: new[] { "lat", "lng" });

            migrationBuilder.CreateIndex(
                name: "events_sport_start_idx",
                schema: "sportsmeet",
                table: "events",
                columns: new[] { "sport_id", "start_at" });

            migrationBuilder.CreateIndex(
                name: "events_start_at_idx",
                schema: "sportsmeet",
                table: "events",
                column: "start_at",
                filter: "status = 'Scheduled'");

            migrationBuilder.CreateIndex(
                name: "ix_events_host_id",
                schema: "sportsmeet",
                table: "events",
                column: "host_id");

            migrationBuilder.CreateIndex(
                name: "ix_sports_name",
                schema: "sportsmeet",
                table: "sports",
                column: "name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_sports_slug",
                schema: "sportsmeet",
                table: "sports",
                column: "slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_users_auth_uid",
                schema: "sportsmeet",
                table: "users",
                column: "auth_uid",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_users_email",
                schema: "sportsmeet",
                table: "users",
                column: "email",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "event_participants",
                schema: "sportsmeet");

            migrationBuilder.DropTable(
                name: "events",
                schema: "sportsmeet");

            migrationBuilder.DropTable(
                name: "sports",
                schema: "sportsmeet");

            migrationBuilder.DropTable(
                name: "users",
                schema: "sportsmeet");
        }
    }
}
