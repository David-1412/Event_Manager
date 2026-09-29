using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SportMeet.Domain.Entities;

namespace SportMeet.Infrastructure.Persistence.Config;

public sealed class SportConfiguration : IEntityTypeConfiguration<Sport>
{
    public const string Schema = "sportsmeet";

    public void Configure(EntityTypeBuilder<Sport> b)
    {
        b.ToTable("sports", Schema);

        b.Property(x => x.Id).UseIdentityByDefaultColumn();
        b.Property(x => x.Name).HasMaxLength(60).IsRequired();
        b.Property(x => x.Slug).HasMaxLength(60).IsRequired();
        b.Property(x => x.Icon).HasMaxLength(8);

        b.HasIndex(x => x.Name).IsUnique();
        b.HasIndex(x => x.Slug).IsUnique();

        // The client's SportKey union and the z.enum in create-event-schema.ts are
        // lowercase slugs; enforce it here so a bad seed insert cannot break
        // browse filters with a value the client will never send.
        b.ToTable(t => t.HasCheckConstraint(
            "sports_slug_lower_check",
            "slug = lower(slug)"));
    }
}
