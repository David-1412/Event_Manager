using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SportMeet.Domain.Entities;

namespace SportMeet.Infrastructure.Persistence.Config;

public sealed class NotificationConfiguration : IEntityTypeConfiguration<Notification>
{
    public void Configure(EntityTypeBuilder<Notification> b)
    {
        b.ToTable("notifications", SportConfiguration.Schema);

        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.Title).HasMaxLength(120).IsRequired();
        b.Property(x => x.Message).HasMaxLength(500).IsRequired();
        b.Property(x => x.Type).HasMaxLength(40).IsRequired();
        b.Property(x => x.Link).HasMaxLength(2048);
        b.Property(x => x.Read).HasColumnName("read").HasDefaultValue(false).IsRequired();
        b.Property(x => x.CreatedAt).IsRequired();

        b.HasOne(x => x.User)
            .WithMany(x => x.Notifications)
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        b.HasIndex(x => new { x.UserId, x.CreatedAt })
            .HasDatabaseName("ix_notifications_user_id_created_at");
        b.HasIndex(x => new { x.UserId, x.Read })
            .HasDatabaseName("ix_notifications_user_id_read");
    }
}
