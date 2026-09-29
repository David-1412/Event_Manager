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

        b.HasIndex(x => x.AuthUid).IsUnique();
        b.HasIndex(x => x.Email).IsUnique();

        // AuthUid/Email stay nullable for Milestone 1: no Firebase auth, so the
        // only row is the seeded demo host. The auth milestone backfills them and
        // tightens nullability - that migration is the one place this changes.
    }
}
