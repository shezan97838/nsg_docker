using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NSG.Mobile.Domain.Entities;

namespace NSG.Mobile.Infrastructure.Persistence.Configurations;

/// <summary>Table <c>banners</c>. Rows are never deleted; <c>is_active</c> hides a banner from the mobile app.</summary>
public class BannerConfiguration : IEntityTypeConfiguration<Banner>
{
    public void Configure(EntityTypeBuilder<Banner> builder)
    {
        builder.ToTable("banners", t =>
        {
            t.HasCheckConstraint("ck_banners_title", "CHAR_LENGTH(`title`) > 0");
            t.HasCheckConstraint("ck_banners_show_only_image", "`show_only_image` = 0 OR `image_storage_key` IS NOT NULL");
        });

        builder.HasKey(b => b.Id);
        builder.Property(b => b.Id).HasColumnType("int").ValueGeneratedOnAdd();

        builder.Property(b => b.Title).IsRequired().HasMaxLength(200);
        builder.Property(b => b.Description).IsRequired().HasMaxLength(1000);
        builder.Property(b => b.Category).IsRequired().HasMaxLength(100);
        builder.Property(b => b.ImageStorageKey).HasMaxLength(500);
        builder.Property(b => b.ImageAltText).HasMaxLength(250);
        builder.Property(b => b.ShowOnlyImage).IsRequired().HasDefaultValue(false);
        builder.Property(b => b.NsgUnitId).HasColumnType("int");
        builder.Property(b => b.Priority).HasColumnType("int");
        builder.Property(b => b.IsActive).IsRequired();

        builder.Property(b => b.CreatedAtIst).IsRequired().HasColumnType("datetime(6)");
        builder.Property(b => b.UpdatedAtIst).IsRequired().HasColumnType("datetime(6)");

        // Saved for future unit-wise targeting; nothing filters on it yet.
        builder.HasOne<NsgUnit>().WithMany().HasForeignKey(b => b.NsgUnitId).OnDelete(DeleteBehavior.Restrict);

        // Serves the mobile list: active banners in priority order.
        builder.HasIndex(b => new { b.IsActive, b.Priority }).HasDatabaseName("ix_banners_is_active_priority");
    }
}
