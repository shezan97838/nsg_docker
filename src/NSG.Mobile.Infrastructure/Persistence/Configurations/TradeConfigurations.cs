using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NSG.Mobile.Domain.Entities;

namespace NSG.Mobile.Infrastructure.Persistence.Configurations;

/// <summary>Table <c>nsg_trade</c>: id, nsg_trade_name (unique), created_at_utc, is_active. Nothing else.</summary>
public class NsgTradeConfiguration : IEntityTypeConfiguration<NsgTrade>
{
    public void Configure(EntityTypeBuilder<NsgTrade> builder)
    {
        builder.ToTable("nsg_trade");
        builder.HasKey(t => t.Id);
        builder.Property(t => t.Id).ValueGeneratedOnAdd();
        builder.Property(t => t.NsgTradeName).IsRequired().HasMaxLength(100);
        builder.Property(t => t.CreatedAtIst).IsRequired().HasColumnType("datetime(6)");
        builder.Property(t => t.IsActive).IsRequired().HasDefaultValue(true);
        builder.HasIndex(t => t.NsgTradeName).IsUnique().HasDatabaseName("ix_nsg_trade_nsg_trade_name");
    }
}

/// <summary>Table <c>pd_trade</c>: id, pd_trade_name (unique), created_at_ist, is_active. Nothing else.</summary>
public class PdTradeConfiguration : IEntityTypeConfiguration<PdTrade>
{
    public void Configure(EntityTypeBuilder<PdTrade> builder)
    {
        builder.ToTable("pd_trade");
        builder.HasKey(t => t.Id);
        builder.Property(t => t.Id).ValueGeneratedOnAdd();
        builder.Property(t => t.PdTradeName).IsRequired().HasMaxLength(100);
        builder.Property(t => t.CreatedAtIst).IsRequired().HasColumnType("datetime(6)");
        builder.Property(t => t.IsActive).IsRequired().HasDefaultValue(true);
        builder.HasIndex(t => t.PdTradeName).IsUnique().HasDatabaseName("ix_pd_trade_pd_trade_name");
    }
}
