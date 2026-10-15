using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NSG.Mobile.Domain.Entities;
using NSG.Mobile.Domain.Enums;

namespace NSG.Mobile.Infrastructure.Persistence.Configurations;

public class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("users");

        builder.HasKey(u => u.Id);
        builder.Property(u => u.Id).ValueGeneratedOnAdd();

        builder.Property(u => u.Name)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(u => u.Rank)
            .HasColumnName("rank_name") // "rank" is a MySQL 8 reserved word
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(u => u.ForceNumber)
            .IsRequired()
            .HasMaxLength(50);

        builder.Property(u => u.MobileNumber)
            .IsRequired()
            .HasMaxLength(15);

        builder.Property(u => u.PasswordHash)
            .HasMaxLength(500);

        builder.Property(u => u.Status)
            .IsRequired()
            .HasConversion<int>();

        builder.Property(u => u.Role)
            .IsRequired()
            .HasConversion<int>();

        builder.Property(u => u.RowVersion)
            .IsRowVersion();

        builder.Property(u => u.CreatedAt)
            .IsRequired();

        builder.Property(u => u.UpdatedAt)
            .IsRequired();

        // Indexes
        builder.HasIndex(u => u.MobileNumber)
            .IsUnique()
            .HasDatabaseName("ix_users_mobile_number");

        builder.HasIndex(u => u.ForceNumber)
            .IsUnique()
            .HasDatabaseName("ix_users_force_number");

        builder.HasIndex(u => u.Status)
            .HasDatabaseName("ix_users_status");

        // Relationships
        builder.HasMany(u => u.Devices)
            .WithOne(d => d.User)
            .HasForeignKey(d => d.UserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(u => u.RefreshTokens)
            .WithOne(rt => rt.User)
            .HasForeignKey(rt => rt.UserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
