using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using SportMeet.Infrastructure.Persistence;

#nullable disable

namespace SportMeet.Infrastructure.Migrations;

/// <summary>
/// Recurring events.
///
/// A series is stored as one events row per session, linked by series_id (the first
/// session's id), so joining, capacity, reminders and Interested keep working per
/// session with no change. Each row also carries the rule it was expanded from
/// (frequency, days, end date or count) so any session can describe its series.
///
/// The view gains the six columns, appended last because CREATE OR REPLACE VIEW may
/// only add columns at the end. Down drops and recreates it instead, since a replace
/// cannot remove them.
///
/// Hand-written in this folder's convention; the model snapshot is not regenerated.
/// </summary>
[DbContext(typeof(AppDbContext))]
[Migration("20261022000000_Add_Recurring_Events")]
public partial class Add_Recurring_Events : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            ALTER TABLE sportsmeet.events
                ADD COLUMN is_recurring boolean NOT NULL DEFAULT false,
                ADD COLUMN recurrence_frequency character varying(10) NULL,
                ADD COLUMN recurrence_days character varying(27) NULL,
                ADD COLUMN recurrence_end_date timestamp with time zone NULL,
                ADD COLUMN recurrence_count integer NULL,
                ADD COLUMN series_id uuid NULL;

            ALTER TABLE sportsmeet.events
                ADD CONSTRAINT events_recurrence_frequency_check
                    CHECK (recurrence_frequency IN ('DAILY', 'WEEKLY', 'BIWEEKLY', 'MONTHLY')),
                ADD CONSTRAINT events_recurrence_count_check
                    CHECK (recurrence_count IS NULL OR recurrence_count BETWEEN 2 AND 52),
                ADD CONSTRAINT events_recurrence_shape_check
                    CHECK (NOT is_recurring OR (recurrence_frequency IS NOT NULL AND series_id IS NOT NULL));

            CREATE INDEX events_series_idx ON sportsmeet.events (series_id, start_at)
                WHERE series_id IS NOT NULL;
            """);

        migrationBuilder.Sql(EventViewSql.Definition);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("DROP VIEW IF EXISTS sportsmeet.v_event_feed;");
        migrationBuilder.Sql(EventViewSql.AddEventJoinApprovalView);
        migrationBuilder.Sql("""
            DROP INDEX IF EXISTS sportsmeet.events_series_idx;
            ALTER TABLE sportsmeet.events
                DROP CONSTRAINT IF EXISTS events_recurrence_shape_check,
                DROP CONSTRAINT IF EXISTS events_recurrence_count_check,
                DROP CONSTRAINT IF EXISTS events_recurrence_frequency_check,
                DROP COLUMN series_id,
                DROP COLUMN recurrence_count,
                DROP COLUMN recurrence_end_date,
                DROP COLUMN recurrence_days,
                DROP COLUMN recurrence_frequency,
                DROP COLUMN is_recurring;
            """);
    }
}
