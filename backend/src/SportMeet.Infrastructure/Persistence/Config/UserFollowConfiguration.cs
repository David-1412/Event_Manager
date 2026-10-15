using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SportMeet.Domain.Entities;

namespace SportMeet.Infrastructure.Persistence.Config;

public sealed class UserFollowConfiguration : IEntityTypeConfiguration<UserFollow>
{
    public void Configure(EntityTypeBuilder<UserFollow> b)
    {
        b.ToTable("user_follows", SportConfiguration.Schema);

        // The direction matters and is easy to get backwards: a follower edge points
        // at the person being followed. Reads ask both directions - "how many follow
        // this host" filters the followed column, "am I following them" filters the
        // follower column with the caller's id - which is why both sides are indexed
        // rather than only the key's leading one.
        b.HasKey(x => new { x.FollowerUserId, x.FollowedUserId });

        b.Property(x => x.CreatedAt).IsRequired().HasColumnName("created_at");

        b.HasOne(x => x.FollowerUser).WithMany().HasForeignKey(x => x.FollowerUserId)
            .OnDelete(DeleteBehavior.Cascade);
        b.HasOne(x => x.FollowedUser).WithMany().HasForeignKey(x => x.FollowedUserId)
            .OnDelete(DeleteBehavior.Cascade);

        // The followers count and the follower list both filter on the followed side,
        // which the composite key cannot serve (it is not the leading column).
        b.HasIndex(x => x.FollowedUserId).HasDatabaseName("ix_user_follows_followed_user_id");
    }
}
