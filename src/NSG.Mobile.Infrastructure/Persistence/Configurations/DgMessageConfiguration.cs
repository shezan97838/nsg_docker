using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NSG.Mobile.Domain.Entities;

namespace NSG.Mobile.Infrastructure.Persistence.Configurations;

/// <summary>Table <c>dg_message</c>. Rows are soft-deleted (<c>is_deleted</c>), never physically deleted.</summary>
public class DgMessageConfiguration : IEntityTypeConfiguration<DgMessage>
{
    public void Configure(EntityTypeBuilder<DgMessage> builder)
    {
        builder.ToTable("dg_message", t =>
        {
            t.HasCheckConstraint("ck_dg_message_dg_name", "CHAR_LENGTH(`dg_name`) > 0");
            t.HasCheckConstraint("ck_dg_message_designation", "CHAR_LENGTH(`designation`) > 0");
            t.HasCheckConstraint("ck_dg_message_message", "CHAR_LENGTH(`message`) > 0");
        });

        builder.HasKey(m => m.Id);
        builder.Property(m => m.Id).HasColumnType("int").ValueGeneratedOnAdd();

        builder.Property(m => m.DgName).IsRequired().HasMaxLength(150);
        builder.Property(m => m.Designation).IsRequired().HasMaxLength(150);
        builder.Property(m => m.Message).IsRequired().HasColumnType("text");
        builder.Property(m => m.ImageStorageKey).HasMaxLength(500);
        builder.Property(m => m.IsActive).IsRequired().HasDefaultValue(true);
        builder.Property(m => m.IsDeleted).IsRequired().HasDefaultValue(false);

        builder.Property(m => m.CreatedAtIst).IsRequired().HasColumnType("datetime(6)");
        builder.Property(m => m.CreatedBy).HasColumnType("bigint");
        builder.Property(m => m.UpdatedAtIst).HasColumnType("datetime(6)");
        builder.Property(m => m.UpdatedBy).HasColumnType("bigint");

        // Serves the mobile list: active, not deleted, newest first.
        builder.HasIndex(m => new { m.IsDeleted, m.IsActive, m.CreatedAtIst }).HasDatabaseName("ix_dg_message_is_deleted_is_active_created_at_ist");
    }
}
