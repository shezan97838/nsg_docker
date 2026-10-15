using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NSG.Mobile.Domain.Entities;
using NSG.Mobile.Domain.Enums;

namespace NSG.Mobile.Infrastructure.Persistence.Configurations;

public class DeviceConfiguration : IEntityTypeConfiguration<Device>
{
    public void Configure(EntityTypeBuilder<Device> builder)
    {
        builder.ToTable("devices");

        builder.HasKey(d => d.Id);
        builder.Property(d => d.Id).ValueGeneratedOnAdd();

        builder.Property(d => d.Platform)
            .IsRequired()
            .HasConversion<int>();

        builder.Property(d => d.DeviceName)
            .HasMaxLength(200);

        builder.Property(d => d.DeviceIdentifierHash)
            .IsRequired()
            .HasMaxLength(500);

        builder.Property(d => d.PublicKey)
            .HasMaxLength(2000);

        builder.Property(d => d.PushToken)
            .HasMaxLength(500);

        builder.Property(d => d.Status)
            .IsRequired()
            .HasConversion<int>();

        builder.Property(d => d.AppVersion)
            .HasMaxLength(50);

        builder.Property(d => d.OsVersion)
            .HasMaxLength(50);

        builder.Property(d => d.LastIpAddress)
            .HasMaxLength(45);

        builder.Property(d => d.CreatedAt).IsRequired();
        builder.Property(d => d.UpdatedAt).IsRequired();

        // Indexes
        builder.HasIndex(d => new { d.UserId, d.Status })
            .HasDatabaseName("ix_devices_user_id_status");

        builder.HasIndex(d => new { d.UserId, d.DeviceIdentifierHash })
            .HasDatabaseName("ix_devices_user_id_device_hash");

        // Relationships
        builder.HasMany(d => d.RefreshTokens)
            .WithOne(rt => rt.Device)
            .HasForeignKey(rt => rt.DeviceId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
