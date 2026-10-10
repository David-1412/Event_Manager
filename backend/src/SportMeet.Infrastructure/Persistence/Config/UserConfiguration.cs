using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SportMeet.Domain.Entities;

namespace SportMeet.Infrastructure.Persistence.Config;

public sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> b)
    {
        b.ToTable("users", SportConfiguration.Schema);

        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.Name).HasMaxLength(120).IsRequired();
        b.Property(x => x.AuthUid).HasMaxLength(128);
        b.Property(x => x.Email).HasMaxLength(320);
        b.Property(x => x.PhotoUrl).HasMaxLength(2048);

        // The column predates this mapping (it has been in the live schema, NOT NULL
        // with DEFAULT now(), since before any migration here mentioned it). Mapping
        // it is what lets the admin user table show a signup date; HasDefaultValue
        // keeps the existing server default so a raw INSERT that omits created_at —
        // the seeded rows, or a psql fix — still works. No HasDefaultValue on the
        // CLR side for the enum above: see the Role remark for that trap.
        b.Property(x => x.CreatedAt).IsRequired().HasDefaultValueSql("now()");

        // Enum-as-text, like every other status column here, so psql shows role names
        // rather than 1. No HasDefaultValue in the model even though the column has
        // a server DEFAULT: EF validates a model default against the property's
        // CLR type before the value converter runs, which throws for an enum
        // (the same trap EventConfiguration documents for Visibility). The entity's
        // own initializer covers EF's INSERTs and the column default covers raw
        // INSERTs that omit the column.
        b.Property(x => x.Role).HasConversion<string>().HasMaxLength(20).IsRequired();

        b.HasIndex(x => x.AuthUid).IsUnique();
        b.HasIndex(x => x.Email).IsUnique();

        b.ToTable(t => t.HasCheckConstraint(
            "users_role_check",
            "role IN ('Member', 'Moderator', 'Admin')"));

        // AuthUid/Email stay nullable for Milestone 1: no Firebase auth, so the
        // only row is the seeded demo host. The auth milestone backfills them and
        // tightens nullability - that migration is the one place this changes.
    }
}
