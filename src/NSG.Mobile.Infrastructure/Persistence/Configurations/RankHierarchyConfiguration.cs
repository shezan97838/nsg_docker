using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NSG.Mobile.Domain.Entities;

namespace NSG.Mobile.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core entity configuration for RankHierarchy entity targeting MySQL 8.x (Pomelo).
/// </summary>
public class RankHierarchyConfiguration : IEntityTypeConfiguration<RankHierarchy>
{
    public void Configure(EntityTypeBuilder<RankHierarchy> builder)
    {
        builder.ToTable("rank_hierarchy");

        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id)
            .ValueGeneratedOnAdd();

        builder.Property(r => r.SrNo)
            .IsRequired();

        builder.Property(r => r.RankName)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(r => r.ShortCode)
            .HasMaxLength(20);

        builder.Property(r => r.IsActive)
            .IsRequired()
            .HasDefaultValue(true)
            .HasColumnType("tinyint(1)");

        builder.Property(r => r.CreatedAt)
            .IsRequired()
            .HasColumnType("datetime(6)");

        builder.Property(r => r.UpdatedAt)
            .IsRequired()
            .HasColumnType("datetime(6)");

        // Unique index on SrNo
        builder.HasIndex(r => r.SrNo)
            .IsUnique()
            .HasDatabaseName("ix_rank_hierarchy_sr_no");

        // Unique index on RankName
        builder.HasIndex(r => r.RankName)
            .IsUnique()
            .HasDatabaseName("ix_rank_hierarchy_rank_name");

        // Composite unique index on (SrNo, RankName)
        builder.HasIndex(r => new { r.SrNo, r.RankName })
            .IsUnique()
            .HasDatabaseName("ix_rank_hierarchy_sr_no_rank_name");

        // Non-clustered index on IsActive for admin filtering
        builder.HasIndex(r => r.IsActive)
            .HasDatabaseName("ix_rank_hierarchy_is_active");
    }
}
