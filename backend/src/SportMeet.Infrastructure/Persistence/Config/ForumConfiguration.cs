using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SportMeet.Domain.Entities;

namespace SportMeet.Infrastructure.Persistence.Config;

public sealed class ForumCategoryConfiguration : IEntityTypeConfiguration<ForumCategory>
{
    public void Configure(EntityTypeBuilder<ForumCategory> b)
    {
        b.ToTable("forum_categories", SportConfiguration.Schema);
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).UseIdentityByDefaultColumn();
        b.Property(x => x.Name).IsRequired().HasMaxLength(50);
        b.Property(x => x.Slug).IsRequired().HasMaxLength(50);
        b.HasIndex(x => x.Slug).IsUnique().HasDatabaseName("ux_forum_categories_slug");
    }
}

public sealed class ForumPostLikeConfiguration : IEntityTypeConfiguration<ForumPostLike>
{
    public void Configure(EntityTypeBuilder<ForumPostLike> b)
    {
        b.ToTable("forum_post_likes", SportConfiguration.Schema);
        b.HasKey(x => new { x.PostId, x.UserId });
        b.Property(x => x.CreatedAt).IsRequired();

        b.HasOne(x => x.Post).WithMany().HasForeignKey(x => x.PostId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);

        // "Which posts on this page am I interested in" filters by user.
        b.HasIndex(x => x.UserId).HasDatabaseName("ix_forum_post_likes_user_id");
        // Trending counts the rows created inside a time window.
        b.HasIndex(x => x.CreatedAt).HasDatabaseName("ix_forum_post_likes_created_at");
    }
}

public sealed class ForumPostConfiguration : IEntityTypeConfiguration<ForumPost>
{
    public void Configure(EntityTypeBuilder<ForumPost> b)
    {
        b.ToTable("forum_posts", SportConfiguration.Schema);
        b.HasKey(x => x.Id);

        b.Property(x => x.Seq).UseIdentityAlwaysColumn().ValueGeneratedOnAdd();
        b.Property(x => x.Title).HasMaxLength(SportMeet.Application.Forum.ForumService.TitleMaxLength);
        b.Property(x => x.Content).IsRequired();
        b.Property(x => x.CreatedAt).IsRequired();

        // Restrict, not cascade: deleting an account must not silently delete the
        // threads other people replied in. Account deletion has to decide what happens
        // to those posts explicitly.
        b.HasOne(x => x.Author).WithMany().HasForeignKey(x => x.AuthorUserId)
            .OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Category).WithMany().HasForeignKey(x => x.CategoryId)
            .OnDelete(DeleteBehavior.Restrict);

        // The feed indexes, matching the two sorts' keysets exactly.
        b.HasIndex(x => x.Seq).IsUnique().HasDatabaseName("ux_forum_posts_seq");
        b.HasIndex(x => new { x.HotScore, x.Seq }).HasDatabaseName("ix_forum_posts_hot");
        b.HasIndex(x => new { x.CategoryId, x.HotScore, x.Seq }).HasDatabaseName("ix_forum_posts_category_hot");
        b.HasIndex(x => new { x.CategoryId, x.Seq }).HasDatabaseName("ix_forum_posts_category_seq");
        b.HasIndex(x => new { x.AuthorUserId, x.CreatedAt }).HasDatabaseName("ix_forum_posts_author_created");
    }
}
