using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using SportMeet.Infrastructure.Persistence;

#nullable disable

namespace SportMeet.Infrastructure.Migrations;

/// <summary>
/// The last column the review workflow needs: the reviewer's sentence to the
/// creator, stored on the rejected event.
///
/// This migration was missing from the tree while <c>Event.RejectionReason</c>
/// was already mapped (EventConfiguration: HasMaxLength(500)) and already present
/// in the model snapshot. EF therefore emitted <c>rejection_reason</c> in every
/// INSERT into events, and the column did not exist: every event create died with
/// 42703 "column \"rejection_reason\" of relation \"events\" does not exist",
/// surfaced to the user as a generic 500. The column is nullable with no default,
/// so existing rows and every non-rejected event simply carry NULL.
///
/// No view change, deliberately. <c>v_event_feed</c> is the browse read model and
/// its SELECT does not read rejection_reason - the reason is only ever shown on the
/// detail page of an event its creator owns or an admin is reviewing, which loads
/// the events row. <see cref="EventViewSql.AddEventRejectionReasonView"/> is
/// therefore unused and left untouched: replaying it would add a column to the
/// view that nothing reads, and a view column can never be removed afterwards
/// without the DROP/CREATE dance Postgres forces on a shrinking view.
///
/// No index either. events_pending_review_idx already exists (Add_Event_Review_
/// Workflow) and its filter - status = 'PendingReview' - matches the model, the
/// snapshot and the live database, so there is nothing to widen here.
///
/// Down() drops only the column. It does not touch the view, so a rollback cannot
/// recreate a stale view shape - the defect the earlier migrations guard against
/// by keeping a private constant.
/// </summary>
[DbContext(typeof(AppDbContext))]
[Migration("20261009000000_Add_Event_Rejection_Reason")]
public partial class Add_Event_Rejection_Reason : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "rejection_reason",
            schema: "sportsmeet",
            table: "events",
            type: "character varying(500)",
            maxLength: 500,
            nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "rejection_reason",
            schema: "sportsmeet",
            table: "events");
    }
}
