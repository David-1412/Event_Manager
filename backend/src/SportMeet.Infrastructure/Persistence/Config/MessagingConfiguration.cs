using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SportMeet.Domain.Entities;

namespace SportMeet.Infrastructure.Persistence.Config;

public sealed class ForumCommentConfiguration : IEntityTypeConfiguration<ForumComment>
{
    public void Configure(EntityTypeBuilder<ForumComment> b)
    {
        b.ToTable("forum_comments", SportConfiguration.Schema);
        b.HasKey(x => x.Id);
        b.Property(x => x.Content).IsRequired();
        b.Property(x => x.CreatedAt).IsRequired();

        b.HasOne(x => x.Post).WithMany().HasForeignKey(x => x.PostId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne(x => x.Author).WithMany().HasForeignKey(x => x.AuthorUserId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.ParentComment).WithMany().HasForeignKey(x => x.ParentCommentId)
            .OnDelete(DeleteBehavior.Cascade);

        b.HasIndex(x => new { x.PostId, x.CreatedAt }).HasDatabaseName("ix_forum_comments_post_created");
    }
}

public sealed class ConversationConfiguration : IEntityTypeConfiguration<Conversation>
{
    public void Configure(EntityTypeBuilder<Conversation> b)
    {
        b.ToTable("conversations", SportConfiguration.Schema);
        b.HasKey(x => x.Id);
        b.Property(x => x.CreatedAt).IsRequired();
        b.Property(x => x.LastMessagePreview).HasMaxLength(200);

        b.HasOne(x => x.UserA).WithMany().HasForeignKey(x => x.UserAId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne(x => x.UserB).WithMany().HasForeignKey(x => x.UserBId).OnDelete(DeleteBehavior.Cascade);

        b.HasIndex(x => new { x.UserAId, x.UserBId }).IsUnique().HasDatabaseName("ux_conversations_pair");
        b.HasIndex(x => new { x.UserBId, x.LastMessageAt }).HasDatabaseName("ix_conversations_user_b_last");
    }
}

public sealed class DirectMessageConfiguration : IEntityTypeConfiguration<DirectMessage>
{
    public void Configure(EntityTypeBuilder<DirectMessage> b)
    {
        b.ToTable("direct_messages", SportConfiguration.Schema);
        b.HasKey(x => x.Id);
        b.Property(x => x.Seq).UseIdentityAlwaysColumn().ValueGeneratedOnAdd();
        b.Property(x => x.Content).IsRequired();
        b.Property(x => x.CreatedAt).IsRequired();

        b.HasOne(x => x.Conversation).WithMany().HasForeignKey(x => x.ConversationId)
            .OnDelete(DeleteBehavior.Cascade);
        b.HasOne(x => x.Sender).WithMany().HasForeignKey(x => x.SenderUserId).OnDelete(DeleteBehavior.Cascade);

        b.HasIndex(x => new { x.ConversationId, x.Seq }).HasDatabaseName("ix_direct_messages_conversation_seq");
    }
}

public sealed class ActivityReadConfiguration : IEntityTypeConfiguration<ActivityRead>
{
    public void Configure(EntityTypeBuilder<ActivityRead> b)
    {
        b.ToTable("activity_reads", SportConfiguration.Schema);
        b.HasKey(x => new { x.UserId, x.ItemKey });
        b.Property(x => x.ItemKey).HasMaxLength(200);
        b.Property(x => x.ReadAt).IsRequired();
        b.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}
