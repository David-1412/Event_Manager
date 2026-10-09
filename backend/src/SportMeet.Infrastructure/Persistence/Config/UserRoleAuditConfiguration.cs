using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SportMeet.Domain.Entities;

namespace SportMeet.Infrastructure.Persistence.Config;

public sealed class UserRoleAuditConfiguration : IEntityTypeConfiguration<UserRoleAudit>
{
    public void Configure(EntityTypeBuilder<UserRoleAudit> b)
    {
        b.ToTable("user_role_audit", SportConfiguration.Schema);

        b.Property(x => x.Id).ValueGeneratedNever();

        // Enum-as-text on all three columns, like users.role, so the trail reads
        // as English in psql. FromRole/ToRole are stored as text rather than
        // derived from Action so a row stays truthful even if the action vocabulary
        // ever grows beyond a two-way toggle.
        b.Property(x => x.FromRole).HasConversion<string>().HasMaxLength(20).IsRequired();
        b.Property(x => x.ToRole).HasConversion<string>().HasMaxLength(20).IsRequired();
        b.Property(x => x.Action).HasConversion<string>().HasMaxLength(20).IsRequired();

        b.Property(x => x.CreatedAt).IsRequired();

        // Restrict, not cascade: deleting a user must not erase the record of who
        // promoted them. The application never deletes users today, so this is a
        // guard against a future feature rather than a live path.
        b.HasOne(x => x.TargetUser).WithMany().HasForeignKey(x => x.TargetUserId)
            .OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.ActorUser).WithMany().HasForeignKey(x => x.ActorUserId)
            .OnDelete(DeleteBehavior.Restrict);

        // The read is "the trail for one account", newest first.
        b.HasIndex(x => new { x.TargetUserId, x.CreatedAt })
            .HasDatabaseName("ix_user_role_audit_target_user_id_created_at");

        b.ToTable(t => t.HasCheckConstraint(
            "user_role_audit_no_op_check",
            "from_role <> to_role"));
    }
}
