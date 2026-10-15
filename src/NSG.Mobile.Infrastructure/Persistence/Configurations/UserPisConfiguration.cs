using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NSG.Mobile.Domain.Entities;
using NSG.Mobile.Domain.Enums;

namespace NSG.Mobile.Infrastructure.Persistence.Configurations;

public class UserPisConfiguration : IEntityTypeConfiguration<UserPis>
{
    public void Configure(EntityTypeBuilder<UserPis> builder)
    {
        builder.ToTable("users_pis", t =>
        {
            // Date sanity: logical impossibilities only — no NSG service rules are invented. NULLs are allowed.
            t.HasCheckConstraint("ck_users_pis_enrollment_after_birth", "`date_of_enrollment_in_govt_service` IS NULL OR `date_of_birth` IS NULL OR `date_of_enrollment_in_govt_service` >= `date_of_birth`");
            t.HasCheckConstraint("ck_users_pis_superannuation_after_birth", "`date_of_superannuation` IS NULL OR `date_of_birth` IS NULL OR `date_of_superannuation` > `date_of_birth`");
            t.HasCheckConstraint("ck_users_pis_repatriation_after_induction", "`date_of_repatriation` IS NULL OR `date_of_induction_in_nsg` IS NULL OR `date_of_repatriation` >= `date_of_induction_in_nsg`");

            // Numeric sanity.
            t.HasCheckConstraint("ck_users_pis_numbers", "(`height_cm` IS NULL OR `height_cm` > 0) AND (`weight_kg` IS NULL OR `weight_kg` > 0) AND (`number_of_children` IS NULL OR `number_of_children` >= 0) " +
                "AND (`earned_leave_due` IS NULL OR `earned_leave_due` >= 0) AND (`half_pay_leave_due` IS NULL OR `half_pay_leave_due` >= 0)");

            // Closed value sets.
            t.HasCheckConstraint("ck_users_pis_category", "`category` IS NULL OR `category` IN (1, 2, 3, 4)");
            t.HasCheckConstraint("ck_users_pis_blood_group", "`blood_group` IS NULL OR `blood_group` BETWEEN 1 AND 8");
            t.HasCheckConstraint("ck_users_pis_mobile_app_activation_status", "`mobile_app_activation_status` IN (0, 1, 2)");

            // Formats: ten-digit Indian mobile (no country code) and six-digit PIN.
            t.HasCheckConstraint("ck_users_pis_mobile_number_format", "`mobile_number` IS NULL OR `mobile_number` REGEXP '^[6-9][0-9]{9}$'");
            t.HasCheckConstraint("ck_users_pis_pin_code_format", "`permanent_pin_code` IS NULL OR `permanent_pin_code` REGEXP '^[1-9][0-9]{5}$'");

            // Blind indexes are 64 lower-case hex characters (HMAC-SHA256).
            t.HasCheckConstraint("ck_users_pis_hashes_format", "(`pan_number_hash` IS NULL OR `pan_number_hash` REGEXP '^[0-9a-f]{64}$') AND (`salary_account_number_hash` IS NULL OR `salary_account_number_hash` REGEXP '^[0-9a-f]{64}$')");
        });

        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).HasColumnType("bigint").ValueGeneratedOnAdd();

        // ── Identity ──
        builder.Property(p => p.NsgNumber).IsRequired().HasMaxLength(30);
        builder.Property(p => p.ServiceNumber).IsRequired().HasMaxLength(30);
        builder.Property(p => p.FullName).HasMaxLength(200);

        // An NSG number is never changed by an ordinary update; EF throws if a tracked entity's value is modified.
        builder.Property(p => p.NsgNumber).Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Throw);

        // ── Dates (MySQL DATE) ──
        builder.Property(p => p.DateOfInductionInNsg).HasColumnType("date");
        builder.Property(p => p.SideSteppedOn).HasColumnType("date");
        builder.Property(p => p.DateOfRepatriation).HasColumnType("date");
        builder.Property(p => p.DateOfBirth).HasColumnType("date");
        builder.Property(p => p.DateOfEnrollmentInGovtService).HasColumnType("date");
        builder.Property(p => p.DateOfSuperannuation).HasColumnType("date");
        builder.Property(p => p.DateOfAme).HasColumnType("date");
        builder.Property(p => p.DateOfImmovablePropertyReturn).HasColumnType("date");

        // ── Personal and family ──
        builder.Property(p => p.Category).HasConversion<int?>();
        builder.Property(p => p.BloodGroup).HasConversion<int?>();
        builder.Property(p => p.FatherName).HasMaxLength(200);
        builder.Property(p => p.MotherName).HasMaxLength(200);
        builder.Property(p => p.SpouseName).HasMaxLength(200);
        builder.Property(p => p.NextOfKinMobileNumber).HasMaxLength(15);
        builder.Property(p => p.MobileNumber).HasMaxLength(15);
        builder.Property(p => p.HeightCm).HasColumnType("tinyint unsigned");
        builder.Property(p => p.WeightKg).HasColumnType("decimal(5,2)");

        // ── Sensitive (ciphertext: base64 of IV + AES-CBC block data) ──
        builder.Property(p => p.AadhaarNumberEncrypted).HasMaxLength(256);
        builder.Property(p => p.PanNumberEncrypted).HasMaxLength(256);
        builder.Property(p => p.PranNumberEncrypted).HasMaxLength(256);
        builder.Property(p => p.SalaryAccountNumberEncrypted).HasMaxLength(256);
        builder.Property(p => p.PanNumberHash).HasMaxLength(64);
        builder.Property(p => p.SalaryAccountNumberHash).HasMaxLength(64);

        // ── Address ──
        builder.Property(p => p.PermanentVillage).HasMaxLength(150);
        builder.Property(p => p.PermanentPostOffice).HasMaxLength(150);
        builder.Property(p => p.PermanentPoliceStation).HasMaxLength(150);
        builder.Property(p => p.PermanentDistrict).HasMaxLength(100);
        builder.Property(p => p.PermanentPinCode).HasMaxLength(6);

        // ── Leave summaries ──
        builder.Property(p => p.EarnedLeaveDue).HasColumnType("decimal(7,2)");
        builder.Property(p => p.HalfPayLeaveDue).HasColumnType("decimal(7,2)");
        builder.Property(p => p.PaternityLeaveAvailed).HasMaxLength(100);
        builder.Property(p => p.MaternityLeaveAvailed).HasMaxLength(100);
        builder.Property(p => p.ChildCareLeaveAvailed).HasMaxLength(100);

        // ── Mobile app activation (int; Pending = 0 is the database default) ──
        builder.Property(p => p.MobileAppActivationStatus).IsRequired().HasConversion<int>().HasDefaultValue(MobileAppActivationStatus.Pending);

        // ── Metadata ──
        builder.Property(p => p.IsActive).IsRequired().HasDefaultValue(true);
        builder.Property(p => p.CreatedAtIst).IsRequired().HasColumnType("datetime(6)");
        builder.Property(p => p.UpdatedAtIst).HasColumnType("datetime(6)");

        // ── Indexes ──
        builder.HasIndex(p => p.NsgNumber).IsUnique().HasDatabaseName("ix_users_pis_nsg_number");
        builder.HasIndex(p => p.ServiceNumber).HasDatabaseName("ix_users_pis_service_number"); // deliberately NOT unique
        builder.HasIndex(p => p.MobileNumber).IsUnique().HasDatabaseName("ix_users_pis_mobile_number"); // NULLs may repeat
        builder.HasIndex(p => p.IsActive).HasDatabaseName("ix_users_pis_is_active");
        builder.HasIndex(p => p.PanNumberHash).IsUnique().HasDatabaseName("ix_users_pis_pan_number_hash"); // NULLs may repeat
        builder.HasIndex(p => p.SalaryAccountNumberHash).IsUnique().HasDatabaseName("ix_users_pis_salary_account_number_hash"); // NULLs may repeat

        // ── Relationships (all Restrict: lookups and ranks are never cascaded away) ──
        builder.HasOne(p => p.NsgRank).WithMany().HasForeignKey(p => p.NsgRankId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(p => p.ParentDepartmentRank).WithMany().HasForeignKey(p => p.ParentDepartmentRankId).OnDelete(DeleteBehavior.Restrict);
        // Column and constraint names come from DatabaseNamingStandard (nsg_trade_id, pd_trade_id, fk_users_pis_nsg_trade_nsg_trade_id).
        builder.HasIndex(p => p.NsgTradeId).HasDatabaseName("ix_users_pis_nsg_trade_id");
        builder.HasIndex(p => p.PdTradeId).HasDatabaseName("ix_users_pis_pd_trade_id");
        builder.HasOne(p => p.NsgTrade).WithMany().HasForeignKey(p => p.NsgTradeId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(p => p.PdTrade).WithMany().HasForeignKey(p => p.PdTradeId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(p => p.NsgUnit).WithMany().HasForeignKey(p => p.NsgUnitId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(p => p.SideSteppedFromNsgUnit).WithMany().HasForeignKey(p => p.SideSteppedFromNsgUnitId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(p => p.Education).WithMany().HasForeignKey(p => p.EducationId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(p => p.Religion).WithMany().HasForeignKey(p => p.ReligionId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(p => p.PermanentState).WithMany().HasForeignKey(p => p.PermanentStateId).OnDelete(DeleteBehavior.Restrict);
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
