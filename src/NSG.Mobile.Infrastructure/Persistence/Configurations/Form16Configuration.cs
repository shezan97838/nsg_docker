using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NSG.Mobile.Domain.Entities;

namespace NSG.Mobile.Infrastructure.Persistence.Configurations;

/// <summary>Table <c>personnel_form16</c>: one row per NSG number and assessment year, holding the relative paths of Part A and Part B.</summary>
public class PersonnelForm16Configuration : IEntityTypeConfiguration<PersonnelForm16>
{
    public void Configure(EntityTypeBuilder<PersonnelForm16> builder)
    {
        builder.ToTable("personnel_form16");

        builder.HasKey(f => f.Id);
        builder.Property(f => f.Id).HasColumnType("bigint").ValueGeneratedOnAdd();

        // Same length as users_pis.nsg_number. Soft reference: users_pis has no foreign keys in either direction (ADR-020).
        builder.Property(f => f.NsgNumber).IsRequired().HasMaxLength(30);
        builder.Property(f => f.AssessmentYear).IsRequired().HasMaxLength(7);
        builder.Property(f => f.PartAFilePath).HasMaxLength(500);
        builder.Property(f => f.PartBFilePath).HasMaxLength(500);
        builder.Property(f => f.PartAUploadedAtIst).HasColumnType("datetime(6)");
        builder.Property(f => f.PartBUploadedAtIst).HasColumnType("datetime(6)");
        builder.Property(f => f.UploadedBy).IsRequired().HasColumnType("bigint");
        builder.Property(f => f.CreatedAtIst).IsRequired().HasColumnType("datetime(6)");
        builder.Property(f => f.UpdatedAtIst).IsRequired().HasColumnType("datetime(6)");

        // Business key; also serves the mobile lookups by NSG number (leftmost column) and the admin filters.
        builder.HasIndex(f => new { f.NsgNumber, f.AssessmentYear }).IsUnique().HasDatabaseName("ix_personnel_form16_nsg_number_assessment_year");
    }
}
