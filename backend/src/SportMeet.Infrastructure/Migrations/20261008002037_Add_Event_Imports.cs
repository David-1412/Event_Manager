using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SportMeet.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Add_Event_Imports : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "event_imports",
                schema: "sportsmeet",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    input_kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    source_text = table.Column<string>(type: "text", nullable: true),
                    extracted_data = table.Column<string>(type: "jsonb", nullable: true),
                    flags = table.Column<string>(type: "jsonb", nullable: true),
                    geocode = table.Column<string>(type: "jsonb", nullable: true),
                    confidence = table.Column<decimal>(type: "numeric(4,3)", nullable: false),
                    missing_fields = table.Column<List<string>>(type: "text[]", nullable: false),
                    model = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    prompt_version = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    latency_ms = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    published_event_id = table.Column<Guid>(type: "uuid", nullable: true),
                    published_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    final_data = table.Column<string>(type: "jsonb", nullable: true),
                    field_outcomes = table.Column<string>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_event_imports", x => x.id);
                    table.CheckConstraint("event_imports_confidence_check", "confidence >= 0 AND confidence <= 1");
                    table.ForeignKey(
                        name: "fk_event_imports_users_user_id",
                        column: x => x.user_id,
                        principalSchema: "sportsmeet",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "event_publish_metrics",
                schema: "sportsmeet",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    event_id = table.Column<Guid>(type: "uuid", nullable: false),
                    path = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    duration_ms = table.Column<int>(type: "integer", nullable: false),
                    import_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_event_publish_metrics", x => x.id);
                    table.CheckConstraint("event_publish_metrics_duration_check", "duration_ms >= 0");
                    table.CheckConstraint("event_publish_metrics_path_check", "path IN ('manual', 'import', 'draft')");
                    table.ForeignKey(
                        name: "fk_event_publish_metrics_event_imports_import_id",
                        column: x => x.import_id,
                        principalSchema: "sportsmeet",
                        principalTable: "event_imports",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_event_publish_metrics_events_event_id",
                        column: x => x.event_id,
                        principalSchema: "sportsmeet",
                        principalTable: "events",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_event_publish_metrics_users_user_id",
                        column: x => x.user_id,
                        principalSchema: "sportsmeet",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_event_imports_created_at",
                schema: "sportsmeet",
                table: "event_imports",
                column: "created_at");

            migrationBuilder.CreateIndex(
                name: "ix_event_imports_user_id_created_at",
                schema: "sportsmeet",
                table: "event_imports",
                columns: new[] { "user_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_event_publish_metrics_event_id",
                schema: "sportsmeet",
                table: "event_publish_metrics",
                column: "event_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_event_publish_metrics_import_id",
                schema: "sportsmeet",
                table: "event_publish_metrics",
                column: "import_id");

            migrationBuilder.CreateIndex(
                name: "ix_event_publish_metrics_path_created_at",
                schema: "sportsmeet",
                table: "event_publish_metrics",
                columns: new[] { "path", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_event_publish_metrics_user_id",
                schema: "sportsmeet",
                table: "event_publish_metrics",
                column: "user_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "event_publish_metrics",
                schema: "sportsmeet");

            migrationBuilder.DropTable(
                name: "event_imports",
                schema: "sportsmeet");
        }
    }
}
