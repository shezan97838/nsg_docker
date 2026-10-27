using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NSG.Mobile.Domain.Constants;
using NSG.Mobile.Domain.Entities;
using NSG.Mobile.Domain.Enums;

namespace NSG.Mobile.Infrastructure.Persistence.Configurations;

/// <summary>Table <c>admin_roles</c>: the seven fixed Admin Portal roles, seeded here and changed only by a reviewed migration.</summary>
public class AdminRoleConfiguration : IEntityTypeConfiguration<AdminRole>
{
    /// <summary>Constant seed timestamp (IST). HasData needs fixed values so the model snapshot stays stable.</summary>
    private static readonly DateTime SeededAtIst = new(2026, 10, 18, 0, 0, 0, DateTimeKind.Unspecified);

    public void Configure(EntityTypeBuilder<AdminRole> builder)
    {
        builder.ToTable("admin_roles", t =>
        {
            t.HasCheckConstraint("ck_admin_roles_role_code_format", "`role_code` REGEXP '^[A-Z][A-Z_]*$'");
        });

        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).HasColumnType("int").ValueGeneratedOnAdd();

        builder.Property(r => r.RoleCode).IsRequired().HasMaxLength(50);
        builder.Property(r => r.RoleName).IsRequired().HasMaxLength(100);
        builder.Property(r => r.Description).HasMaxLength(500);
        builder.Property(r => r.IsActive).IsRequired().HasDefaultValue(true);
        builder.Property(r => r.CreatedAtIst).IsRequired().HasColumnType("datetime(6)");
        builder.Property(r => r.UpdatedAtIst).HasColumnType("datetime(6)");

        builder.HasIndex(r => r.RoleCode).IsUnique().HasDatabaseName("ix_admin_roles_role_code");

        // Fixed ids: admin_profiles.role_id and the seed rows must never drift.
        builder.HasData(
            Role(1, AdminRoleCodes.SuperAdmin, "Super Admin", "Provisions and manages Admin Portal accounts."),
            Role(2, AdminRoleCodes.RegistrationAdmin, "Registration Admin", "Reviews mobile-app registration requests."),
            Role(3, AdminRoleCodes.PisAdmin, "PIS Admin", "Maintains personnel information system records."),
            Role(4, AdminRoleCodes.GrievanceAdmin, "Grievance Admin", "Handles grievances."),
            Role(5, AdminRoleCodes.MediaCellAdmin, "Media Cell Admin", "Manages media cell content."),
            Role(6, AdminRoleCodes.SalaryManagementAdmin, "Salary Management Admin", "Manages salary information."),
            Role(7, AdminRoleCodes.LeaveAdmin, "Leave Admin", "Maintains personnel leave records (manual entry and Excel bulk upload)."));
    }

    private static AdminRole Role(int id, string code, string name, string description) => new()
    {
        Id = id, RoleCode = code, RoleName = name, Description = description, IsActive = true, CreatedAtIst = SeededAtIst
    };
}

/// <summary>Table <c>admin_refresh_tokens</c>: refresh tokens of Admin Portal sessions, hash only. Foreign key to <c>admin_profiles</c> (Restrict).</summary>
public class AdminRefreshTokenConfiguration : IEntityTypeConfiguration<AdminRefreshToken>
{
    public void Configure(EntityTypeBuilder<AdminRefreshToken> builder)
    {
        builder.ToTable("admin_refresh_tokens");

        builder.HasKey(t => t.Id);
        builder.Property(t => t.Id).HasColumnType("bigint").ValueGeneratedOnAdd();

        builder.Property(t => t.AdminProfileId).IsRequired().HasColumnType("bigint");
        builder.Property(t => t.TokenHash).IsRequired().HasMaxLength(100);   // base64 SHA-256 is 44 characters
        builder.Property(t => t.FamilyId).IsRequired();
        builder.Property(t => t.ExpiresAtIst).IsRequired().HasColumnType("datetime(6)");
        builder.Property(t => t.CreatedAtIst).IsRequired().HasColumnType("datetime(6)");
        builder.Property(t => t.CreatedByIp).HasMaxLength(45);
        builder.Property(t => t.RevokedAtIst).HasColumnType("datetime(6)");
        builder.Property(t => t.RevokeReason).HasMaxLength(50);
        builder.Property(t => t.ReuseDetectedAtIst).HasColumnType("datetime(6)");

        builder.HasOne<AdminProfile>().WithMany().HasForeignKey(t => t.AdminProfileId).OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(t => t.TokenHash).IsUnique().HasDatabaseName("ix_admin_refresh_tokens_token_hash");
        builder.HasIndex(t => t.FamilyId).HasDatabaseName("ix_admin_refresh_tokens_family_id");
        // Serves "revoke every live session of this admin".
        builder.HasIndex(t => new { t.AdminProfileId, t.RevokedAtIst }).HasDatabaseName("ix_admin_refresh_tokens_admin_profile_id_revoked_at_ist");
    }
}

/// <summary>
/// Table <c>admin_profiles</c>. One account per NSG number. No foreign key to <c>users_pis</c> (see <see cref="AdminProfile"/>);
/// the foreign key to <c>admin_roles</c> is Restrict, so a role in use cannot be deleted.
/// </summary>
public class AdminProfileConfiguration : IEntityTypeConfiguration<AdminProfile>
{
    public void Configure(EntityTypeBuilder<AdminProfile> builder)
    {
        builder.ToTable("admin_profiles", t =>
        {
            t.HasCheckConstraint("ck_admin_profiles_activation_status", "`activation_status` IN (0, 1)");
            t.HasCheckConstraint("ck_admin_profiles_failed_login_attempts", "`failed_login_attempts` >= 0");

            // A hash column is either NULL (no password yet) or non-empty; never an empty string.
            t.HasCheckConstraint("ck_admin_profiles_password_hash", "`password_hash` IS NULL OR CHAR_LENGTH(`password_hash`) > 0");

            // An Active account always has a password: activation sets the password and the status together.
            t.HasCheckConstraint("ck_admin_profiles_active_has_password", "`activation_status` = 0 OR `password_hash` IS NOT NULL");
        });

        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).HasColumnType("bigint").ValueGeneratedOnAdd();

        builder.Property(p => p.NsgNumber).IsRequired().HasMaxLength(30);          // same length as users_pis.nsg_number
        builder.Property(p => p.RoleId).IsRequired();
        builder.Property(p => p.PasswordHash).HasMaxLength(500);                   // same length as mobile_user_profiles.password_hash
        builder.Property(p => p.ActivationStatus).IsRequired().HasConversion<int>().HasDefaultValue(AdminActivationStatus.PendingActivation);
        builder.Property(p => p.IsActive).IsRequired().HasDefaultValue(true);
        builder.Property(p => p.FailedLoginAttempts).IsRequired().HasDefaultValue(0);
        builder.Property(p => p.LockoutEndIst).HasColumnType("datetime(6)");
        builder.Property(p => p.LastLoginAtIst).HasColumnType("datetime(6)");

        builder.Property(p => p.CreatedAtIst).IsRequired().HasColumnType("datetime(6)");
        builder.Property(p => p.UpdatedAtIst).HasColumnType("datetime(6)");

        builder.HasOne(p => p.Role).WithMany().HasForeignKey(p => p.RoleId).OnDelete(DeleteBehavior.Restrict);

        // ix_admin_profiles_role_id (created for the foreign key) serves "admins of this role".
        builder.HasIndex(p => p.NsgNumber).IsUnique().HasDatabaseName("ix_admin_profiles_nsg_number");
    }
}
