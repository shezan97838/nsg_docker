using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NSG.Mobile.Domain.Entities;

namespace NSG.Mobile.Infrastructure.Persistence.Configurations;

/// <summary>Table <c>digital_library</c>. Rows are never deleted; <c>is_active</c> hides a record from the mobile app. <c>file_path</c> is relative to the DigitalLibrary storage folder.</summary>
public class DigitalLibraryConfiguration : IEntityTypeConfiguration<DigitalLibraryItem>
{
    public void Configure(EntityTypeBuilder<DigitalLibraryItem> builder)
    {
        builder.ToTable("digital_library", t =>
        {
            t.HasCheckConstraint("ck_digital_library_description", "CHAR_LENGTH(`description`) > 0");
            t.HasCheckConstraint("ck_digital_library_file_type", "`file_type` IN ('PDF', 'JPG', 'JPEG', 'PNG')");
        });

        builder.HasKey(d => d.Id);
        builder.Property(d => d.Id).HasColumnType("bigint").ValueGeneratedOnAdd();

        builder.Property(d => d.Description).IsRequired().HasMaxLength(1000);
        builder.Property(d => d.FilePath).IsRequired().HasMaxLength(500);
        builder.Property(d => d.FileType).IsRequired().HasMaxLength(10);
        builder.Property(d => d.IsImportant).IsRequired().HasDefaultValue(false);
        builder.Property(d => d.IsActive).IsRequired().HasDefaultValue(true);
        builder.Property(d => d.CreatedAtIst).IsRequired().HasColumnType("datetime(6)");
        builder.Property(d => d.CreatedBy).IsRequired().HasColumnType("bigint");
        builder.Property(d => d.UpdatedAtIst).HasColumnType("datetime(6)");
        builder.Property(d => d.UpdatedBy).HasColumnType("bigint");

        // Mobile list: active records, newest first (the primary key is appended to every InnoDB index, which gives the id tie-break).
        builder.HasIndex(d => new { d.IsActive, d.CreatedAtIst }).HasDatabaseName("ix_digital_library_is_active_created_at_ist");
        // Mobile list filtered on the important flag.
        builder.HasIndex(d => new { d.IsActive, d.IsImportant, d.CreatedAtIst }).HasDatabaseName("ix_digital_library_is_active_is_important_created_at_ist");
    }
}
