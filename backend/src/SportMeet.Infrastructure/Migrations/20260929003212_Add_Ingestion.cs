using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SportMeet.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Add_Ingestion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ingested_emails",
                schema: "sportsmeet",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    mailbox = table.Column<string>(type: "text", nullable: false),
                    message_uid = table.Column<string>(type: "text", nullable: false),
                    message_id = table.Column<string>(type: "text", nullable: false),
                    sent_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    from_addr = table.Column<string>(type: "text", nullable: false),
                    subject = table.Column<string>(type: "text", nullable: false),
                    body_text = table.Column<string>(type: "text", nullable: false),
                    body_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    processed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    extraction_status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    extract_error = table.Column<string>(type: "text", nullable: true),
                    model = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    prompt_version = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    latency_ms = table.Column<int>(type: "integer", nullable: true),
                    token_usage = table.Column<int>(type: "integer", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ingested_emails", x => x.id);
                    table.CheckConstraint("ingested_emails_body_hash_length_check", "char_length(body_hash) = 64");
                    table.CheckConstraint("ingested_emails_body_hash_lower_check", "body_hash = lower(body_hash)");
                    table.CheckConstraint("ingested_emails_extraction_status_check", "extraction_status IN ('Pending', 'Extracted', 'NoEvent', 'Error')");
                });

            migrationBuilder.CreateTable(
                name: "event_drafts",
                schema: "sportsmeet",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    ingested_email_id = table.Column<Guid>(type: "uuid", nullable: false),
                    payload = table.Column<string>(type: "jsonb", nullable: false),
                    confidence = table.Column<decimal>(type: "numeric(4,3)", nullable: false),
                    missing_fields = table.Column<List<string>>(type: "text[]", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    event_id = table.Column<Guid>(type: "uuid", nullable: true),
                    duplicate_of_event_id = table.Column<Guid>(type: "uuid", nullable: true),
                    reviewed_by = table.Column<Guid>(type: "uuid", nullable: true),
                    reviewed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    review_note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_event_drafts", x => x.id);
                    table.CheckConstraint("event_drafts_approved_event_check", "(status = 'Approved') = (event_id IS NOT NULL)");
                    table.CheckConstraint("event_drafts_confidence_check", "confidence >= 0 AND confidence <= 1");
                    table.CheckConstraint("event_drafts_status_check", "status IN ('Pending', 'Approved', 'Rejected', 'Duplicate')");
                    table.ForeignKey(
                        name: "fk_event_drafts_ingested_emails_ingested_email_id",
                        column: x => x.ingested_email_id,
                        principalSchema: "sportsmeet",
                        principalTable: "ingested_emails",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "event_drafts_email_idx",
                schema: "sportsmeet",
                table: "event_drafts",
                column: "ingested_email_id");

            migrationBuilder.CreateIndex(
                name: "event_drafts_pending_idx",
                schema: "sportsmeet",
                table: "event_drafts",
                column: "created_at",
                filter: "status = 'Pending'");

            migrationBuilder.CreateIndex(
                name: "ingested_emails_body_hash_idx",
                schema: "sportsmeet",
                table: "ingested_emails",
                column: "body_hash");

            migrationBuilder.CreateIndex(
                name: "ingested_emails_mailbox_uid_key",
                schema: "sportsmeet",
                table: "ingested_emails",
                columns: new[] { "mailbox", "message_uid" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ingested_emails_pending_idx",
                schema: "sportsmeet",
                table: "ingested_emails",
                column: "created_at",
                filter: "processed_at IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "event_drafts",
                schema: "sportsmeet");

            migrationBuilder.DropTable(
                name: "ingested_emails",
                schema: "sportsmeet");
        }
    }
}
