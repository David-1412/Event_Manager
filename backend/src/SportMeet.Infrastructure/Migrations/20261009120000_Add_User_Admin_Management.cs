using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using SportMeet.Infrastructure.Persistence;

#nullable disable

namespace SportMeet.Infrastructure.Migrations;

/// <summary>
/// Two tables' worth of schema for administrator user management, plus the
/// reconciliation of one column that has been in the live database without ever
/// being migrated here.
///
/// <b>users.created_at.</b> The column is present in every development database —
/// NOT NULL, DEFAULT now() — but appears in no migration and in no model snapshot,
/// while <c>User.CreatedAt</c> is only now mapped. That combination is the reason
/// this migration is written defensively: <c>AddColumn</c> would fail with 42701
/// "column already exists" on a database that has it, and do nothing useful on one
/// that does not. The guarded form makes both true, which matters because the
/// column is read by the admin user list and a database that lacks it would fail
/// every admin page load with 42703.
///
/// <b>user_role_audit.</b> The append-only privilege trail. Written by
/// UserAdminService on a role change that actually landed. The two FKs are RESTRICT
/// so that removing a user cannot silently delete the record of who granted them
/// privilege; the check constraint stops the table recording a change that changed
/// nothing, which would otherwise make the trail lie about a no-op.
///
/// <b>No view change.</b> <c>v_event_feed</c> is the browse read model and reads no
/// user-management column, so the view is untouched and its definition stays
/// byte-identical — the reason <c>Add_Tags.Down()</c> fails today is that it drops
/// a column a view depends on, and this migration adds no such dependency.
/// </summary>
[DbContext(typeof(AppDbContext))]
[Migration("20261009120000_Add_User_Admin_Management")]
public partial class Add_User_Admin_Management : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Guarded because the column already exists in databases created before this
        // mapping existed. IF NOT EXISTS is Postgres-native; EF has no idempotent
        // AddColumn, so the raw form is the honest one here.
        migrationBuilder.Sql(
            """
            ALTER TABLE sportsmeet.users
                ADD COLUMN IF NOT EXISTS created_at timestamp with time zone NOT NULL DEFAULT now();
            """);

        migrationBuilder.Sql(
            """
            ALTER TABLE sportsmeet.users
                ALTER COLUMN created_at SET DEFAULT now();
            """);

        migrationBuilder.CreateTable(
            name: "user_role_audit",
            schema: "sportsmeet",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                target_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                actor_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                from_role = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                to_role = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                action = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_user_role_audit", x => x.id);

                table.CheckConstraint(
                    "user_role_audit_no_op_check", "from_role <> to_role");

                table.ForeignKey(
                    name: "fk_user_role_audit_users_target_user_id",
                    column: x => x.target_user_id,
                    principalSchema: "sportsmeet",
                    principalTable: "users",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);

                table.ForeignKey(
                    name: "fk_user_role_audit_users_actor_user_id",
                    column: x => x.actor_user_id,
                    principalSchema: "sportsmeet",
                    principalTable: "users",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "ix_user_role_audit_target_user_id_created_at",
            schema: "sportsmeet",
            table: "user_role_audit",
            columns: new[] { "target_user_id", "created_at" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Drops only what this migration created. users.created_at is deliberately
        // left in place: it predates this migration in every database that had it,
        // and dropping a NOT NULL column the seeder's INSERTs rely on would break
        // the database on the way back.
        migrationBuilder.DropTable(
            name: "user_role_audit",
            schema: "sportsmeet");
    }
}
