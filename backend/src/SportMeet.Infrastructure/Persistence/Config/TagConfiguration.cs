using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SportMeet.Domain.Entities;

namespace SportMeet.Infrastructure.Persistence.Config;

public sealed class TagConfiguration : IEntityTypeConfiguration<Tag>
{
    public void Configure(EntityTypeBuilder<Tag> b)
    {
        b.ToTable("tags", SportConfiguration.Schema);

        b.Property(x => x.Id).UseIdentityByDefaultColumn();
        b.Property(x => x.Name).HasMaxLength(TagNormalizer.MaxTagLength).IsRequired();

        // Ordinal, matching the client: 'tennis' is one tag and the index is what
        // makes the create path's "find existing or insert" a single unique
        // lookup rather than a table scan.
        b.HasIndex(x => x.Name).IsUnique().HasDatabaseName("tags_name_key");

        b.Property(x => x.CreatedAt).IsRequired();

        // Normalization promises lowercase; enforced in the schema for the same
        // reason sports_slug_lower_check exists - a hand-inserted 'Tennis' would
        // create a second popular tag the client can never produce.
        b.ToTable(t => t.HasCheckConstraint(
            "tags_name_lower_check",
            "name = lower(name)"));

        b.ToTable(t => t.HasCheckConstraint(
            "tags_name_length_check",
            $"char_length(name) BETWEEN {TagNormalizer.MinTagLength} AND {TagNormalizer.MaxTagLength}"));
    }
}
