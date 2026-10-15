using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NSG.Mobile.Domain.Constants;
using NSG.Mobile.Domain.Entities;
using NSG.Mobile.Domain.Enums;

namespace NSG.Mobile.Infrastructure.Persistence.Configurations;

/// <summary>Table <c>mobile_user_profiles</c>. Unique NSG number and mobile number; date of birth is not unique.</summary>
public class MobileUserProfileConfiguration : IEntityTypeConfiguration<MobileUserProfile>
{
    public void Configure(EntityTypeBuilder<MobileUserProfile> builder)
    {
        builder.ToTable("mobile_user_profiles", t =>
        {
            t.HasCheckConstraint("ck_mobile_user_profiles_role", "`role` IN (0, 1, 2, 3)");
            t.HasCheckConstraint("ck_mobile_user_profiles_mobile_number_format", $"`mobile_number` REGEXP '{DomainConstants.PisFormats.MobileNumberPattern}'");
            t.HasCheckConstraint("ck_mobile_user_profiles_failed_login_attempts", "`failed_login_attempts` >= 0");

            // A credential column is either NULL (not set up) or a non-empty hash; never an empty string.
            t.HasCheckConstraint("ck_mobile_user_profiles_credentials", "(`mpin_hash` IS NULL OR CHAR_LENGTH(`mpin_hash`) > 0) AND (`password_hash` IS NULL OR CHAR_LENGTH(`password_hash`) > 0)");
        });

        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).HasColumnType("bigint").ValueGeneratedOnAdd();

        builder.Property(p => p.NsgNumber).IsRequired().HasMaxLength(30);          // same length as users_pis.nsg_number
        builder.Property(p => p.MobileNumber).IsRequired().HasMaxLength(15);       // same length as users_pis.mobile_number
        builder.Property(p => p.DateOfBirth).IsRequired().HasColumnType("date");
        builder.Property(p => p.Role).IsRequired().HasConversion<int>();

        builder.Property(p => p.MpinHash).HasMaxLength(500);                       // same length as users.password_hash
        builder.Property(p => p.PasswordHash).HasMaxLength(500);

        builder.Property(p => p.IsActive).IsRequired().HasDefaultValue(true);
        builder.Property(p => p.FailedLoginAttempts).IsRequired().HasDefaultValue(0);
        builder.Property(p => p.LockedUntilIst).HasColumnType("datetime(6)");
        builder.Property(p => p.LastLoginAtIst).HasColumnType("datetime(6)");

        builder.Property(p => p.CreatedAtIst).IsRequired().HasColumnType("datetime(6)");
        builder.Property(p => p.UpdatedAtIst).HasColumnType("datetime(6)");

        builder.HasIndex(p => p.NsgNumber).IsUnique().HasDatabaseName("ix_mobile_user_profiles_nsg_number");
        builder.HasIndex(p => p.MobileNumber).IsUnique().HasDatabaseName("ix_mobile_user_profiles_mobile_number");
    }
}

/// <summary>
/// Table <c>approve_mobile_users</c>: history of activation requests. No unique index on NSG number, mobile number or date
/// of birth; the only unique index is on the generated <c>pending_nsg_number</c> (one pending request per NSG number).
/// </summary>
public class ApproveMobileUserConfiguration : IEntityTypeConfiguration<ApproveMobileUser>
{
    public void Configure(EntityTypeBuilder<ApproveMobileUser> builder)
    {
        builder.ToTable("approve_mobile_users", t =>
        {
            t.HasCheckConstraint("ck_approve_mobile_users_approval_status", "`approval_status` IN (0, 1, 2)");

            // A Pending request has no reviewer yet; an Approved or Rejected one always records who reviewed it and when.
            t.HasCheckConstraint("ck_approve_mobile_users_review_fields",
                "(`approval_status` = 0 AND `approved_by` IS NULL AND `approved_at_ist` IS NULL) OR (`approval_status` <> 0 AND `approved_by` IS NOT NULL AND `approved_at_ist` IS NOT NULL)");
            t.HasCheckConstraint("ck_approve_mobile_users_mobile_number_format", $"`mobile_number` REGEXP '{DomainConstants.PisFormats.MobileNumberPattern}'");
        });

        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).HasColumnType("bigint").ValueGeneratedOnAdd();

        builder.Property(a => a.NsgNumber).IsRequired().HasMaxLength(30);
        builder.Property(a => a.MobileNumber).IsRequired().HasMaxLength(15);
        builder.Property(a => a.DateOfBirth).IsRequired().HasColumnType("date");

        // int; Pending = 0 is the database default (same storage as users_pis.mobile_app_activation_status).
        builder.Property(a => a.ApprovalStatus).IsRequired().HasConversion<int>().HasDefaultValue(MobileAppActivationStatus.Pending);

        // Generated column behind the "one pending request per NSG number" rule: NSG number while Pending, else NULL.
        // STORED so it can be indexed on every supported MySQL 8 version; EF never writes it.
        builder.Property(a => a.PendingNsgNumber).HasMaxLength(30)
            .HasComputedColumnSql($"IF(`approval_status` = {(int)MobileAppActivationStatus.Pending}, `nsg_number`, NULL)", stored: true);

        builder.Property(a => a.RequestedAtIst).IsRequired().HasColumnType("datetime(6)");
        builder.Property(a => a.ApprovedAtIst).HasColumnType("datetime(6)");
        builder.Property(a => a.Remarks).HasMaxLength(500);

        builder.Property(a => a.CreatedAtIst).IsRequired().HasColumnType("datetime(6)");
        builder.Property(a => a.UpdatedAtIst).HasColumnType("datetime(6)");

        // Composite serves "requests of this NSG number" and "pending request of this NSG number"; the second serves the reviewer's queue.
        builder.HasIndex(a => new { a.NsgNumber, a.ApprovalStatus }).HasDatabaseName("ix_approve_mobile_users_nsg_number_approval_status");
        builder.HasIndex(a => a.ApprovalStatus).HasDatabaseName("ix_approve_mobile_users_approval_status");

        // At most one pending request per NSG number. NULLs (every non-pending row) may repeat in a MySQL unique index.
        builder.HasIndex(a => a.PendingNsgNumber).IsUnique().HasDatabaseName("ix_approve_mobile_users_pending_nsg_number");
    }
}
