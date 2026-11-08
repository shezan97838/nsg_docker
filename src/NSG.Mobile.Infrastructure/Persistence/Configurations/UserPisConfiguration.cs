using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NSG.Mobile.Domain.Entities;
using NSG.Mobile.Domain.Enums;

namespace NSG.Mobile.Infrastructure.Persistence.Configurations;

/// <summary>
/// Self-contained personnel row: no foreign keys, lookup ids or navigations. Workbook text columns are strings, exactly
/// seven columns are DATE, and the four sensitive identifiers are ciphertext only (no hash columns, no unique index).
/// </summary>
public class UserPisConfiguration : IEntityTypeConfiguration<UserPis>
{
    public void Configure(EntityTypeBuilder<UserPis> builder)
    {
        builder.ToTable("users_pis", t =>
        {
            // Only two constraints besides the keys. There are deliberately NO date-order checks: the source workbook contains
            // records whose dates are out of the "expected" order (for example a superannuation date before the birth date), and a
            // personnel table that must accept those rows cannot reject them.

            // Onboarding state: 0 = Pending, 1 = Approved, 2 = Rejected.
            t.HasCheckConstraint("ck_users_pis_mobile_app_activation_status", "`mobile_app_activation_status` IN (0, 1, 2)");

            // The individual's mobile number is the unique login/contact key: exactly ten digits.
            t.HasCheckConstraint("ck_users_pis_mobile_number_format", "`mobile_number` REGEXP '^[0-9]{10}$'");
        });

        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).HasColumnType("bigint").ValueGeneratedOnAdd();

        // ── Identity and service ──
        builder.Property(p => p.NsgNumber).IsRequired().HasMaxLength(30);
        builder.Property(p => p.ServiceNumber).HasMaxLength(30);
        builder.Property(p => p.NsgRank).HasMaxLength(100);
        builder.Property(p => p.ParentDepartmentRank).HasMaxLength(100);
        builder.Property(p => p.FullName).HasMaxLength(200);
        builder.Property(p => p.NsgUnit).HasMaxLength(100);
        builder.Property(p => p.Education).HasMaxLength(100);
        builder.Property(p => p.SideSteppedDetails).HasMaxLength(255);

        // An NSG number is never changed by an ordinary update; EF throws if a tracked entity's value is modified.
        builder.Property(p => p.NsgNumber).Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Throw);

        // ── Dates (MySQL DATE): exactly these seven ──
        builder.Property(p => p.DateOfInductionInNsg).HasColumnType("date");
        builder.Property(p => p.DateOfRepatriation).HasColumnType("date");
        builder.Property(p => p.DateOfBirth).HasColumnType("date");
        builder.Property(p => p.DateOfEnrollmentInGovtService).HasColumnType("date");
        builder.Property(p => p.DateOfSuperannuation).HasColumnType("date");
        builder.Property(p => p.DateOfAme).HasColumnType("date");
        builder.Property(p => p.DateOfImmovablePropertyReturn).HasColumnType("date");

        // ── Personal and family (text) ──
        builder.Property(p => p.Gender).HasMaxLength(30);
        builder.Property(p => p.Category).HasMaxLength(30);
        builder.Property(p => p.Religion).HasMaxLength(100);
        builder.Property(p => p.BloodGroup).HasMaxLength(20);
        builder.Property(p => p.HeightCm).HasMaxLength(50);
        builder.Property(p => p.WeightKg).HasMaxLength(50);
        builder.Property(p => p.FatherName).HasMaxLength(200);
        builder.Property(p => p.MotherName).HasMaxLength(200);
        builder.Property(p => p.SpouseName).HasMaxLength(200);
        builder.Property(p => p.NextOfKinMobileNumber).HasMaxLength(15);
        builder.Property(p => p.NumberOfChildren).HasMaxLength(50);
        builder.Property(p => p.MobileNumber).IsRequired().HasMaxLength(10);

        // ── Sensitive (ciphertext: base64 of IV + AES-CBC block data). No hashes, no unique indexes. ──
        builder.Property(p => p.AadhaarNumberEncrypted).HasMaxLength(512);
        builder.Property(p => p.PanNumberEncrypted).HasMaxLength(512);
        builder.Property(p => p.PranNumberEncrypted).HasMaxLength(512);
        builder.Property(p => p.SalaryAccountNumberEncrypted).HasMaxLength(512);

        // ── Address ──
        builder.Property(p => p.PermanentVillage).HasMaxLength(150);
        builder.Property(p => p.PermanentPostOffice).HasMaxLength(150);
        builder.Property(p => p.PermanentPoliceStation).HasMaxLength(150);
        builder.Property(p => p.PermanentDistrict).HasMaxLength(100);
        builder.Property(p => p.PermanentState).HasMaxLength(100);
        builder.Property(p => p.PermanentPinCode).HasMaxLength(10);

        // ── Leave summaries (text) ──
        builder.Property(p => p.EarnedLeaveDue).HasMaxLength(100);
        builder.Property(p => p.HalfPayLeaveDue).HasMaxLength(100);
        builder.Property(p => p.PaternityLeaveAvailed).HasMaxLength(100);
        builder.Property(p => p.MaternityLeaveAvailed).HasMaxLength(100);
        builder.Property(p => p.ChildCareLeaveAvailed).HasMaxLength(100);

        // ── Mobile app activation (int; Pending = 0 is the database default) ──
        builder.Property(p => p.MobileAppActivationStatus).IsRequired().HasConversion<int>().HasDefaultValue(MobileAppActivationStatus.Pending);

        // ── Metadata ──
        builder.Property(p => p.IsActive).IsRequired().HasDefaultValue(true);
        builder.Property(p => p.CreatedAtIst).IsRequired().HasColumnType("datetime(6)");
        builder.Property(p => p.UpdatedAtIst).HasColumnType("datetime(6)");

        // ── Ownership: admin_profiles.id of the responsible PIS Admin (nullable, deliberately no FK like the rest of this table) ──
        builder.Property(p => p.AssignedTo).HasColumnType("bigint");

        // ── Indexes ──
        builder.HasIndex(p => p.NsgNumber).IsUnique().HasDatabaseName("ix_users_pis_nsg_number");
        builder.HasIndex(p => p.ServiceNumber).HasDatabaseName("ix_users_pis_service_number"); // deliberately NOT unique
        builder.HasIndex(p => p.MobileNumber).IsUnique().HasDatabaseName("ix_users_pis_mobile_number");
        builder.HasIndex(p => p.IsActive).HasDatabaseName("ix_users_pis_is_active");
        builder.HasIndex(p => p.AssignedTo).HasDatabaseName("ix_users_pis_assigned_to");
    }
}


internal static class PisLookupMapping
{
    /// <summary>Table, key, Name and audit columns shared by every lookup (no index on Name).</summary>
    public static void ConfigureColumns<T>(EntityTypeBuilder<T> builder, string table) where T : PisLookup
    {
        builder.ToTable(table);
        builder.HasKey(l => l.Id);
        builder.Property(l => l.Id).ValueGeneratedOnAdd();
        builder.Property(l => l.Name).IsRequired().HasMaxLength(100);
        builder.Property(l => l.IsActive).IsRequired().HasDefaultValue(true);
        builder.Property(l => l.CreatedAtIst).IsRequired().HasColumnType("datetime(6)");
        builder.Property(l => l.UpdatedAtIst).HasColumnType("datetime(6)");
    }

    /// <summary>Standard lookup: shared columns plus a globally unique Name.</summary>
    public static void Configure<T>(EntityTypeBuilder<T> builder, string table) where T : PisLookup
    {
        ConfigureColumns(builder, table);
        builder.HasIndex(l => l.Name).IsUnique().HasDatabaseName($"ix_{table}_name");
    }
}

public class NsgUnitConfiguration : IEntityTypeConfiguration<NsgUnit>
{
    public void Configure(EntityTypeBuilder<NsgUnit> builder) => PisLookupMapping.Configure(builder, "nsg_units");
}

public class EducationQualificationConfiguration : IEntityTypeConfiguration<EducationQualification>
{
    public void Configure(EntityTypeBuilder<EducationQualification> builder)
    {
        builder.ToTable("education_qualifications");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedOnAdd();
        builder.Property(e => e.CourseName).IsRequired().HasMaxLength(100);
        builder.Property(e => e.CreatedAtIst).IsRequired().HasColumnType("datetime(6)");
        builder.Property(e => e.IsActive).IsRequired().HasDefaultValue(true);
        builder.HasIndex(e => e.CourseName).IsUnique().HasDatabaseName("ix_education_qualifications_course_name");
    }
}

public class ReligionConfiguration : IEntityTypeConfiguration<Religion>
{
    public void Configure(EntityTypeBuilder<Religion> builder) => PisLookupMapping.Configure(builder, "religions");
}

public class ParentDepartmentRankConfiguration : IEntityTypeConfiguration<ParentDepartmentRank>
{
    public void Configure(EntityTypeBuilder<ParentDepartmentRank> builder) => PisLookupMapping.Configure(builder, "parent_department_ranks");
}
