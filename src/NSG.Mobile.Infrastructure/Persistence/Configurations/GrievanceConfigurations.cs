using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NSG.Mobile.Domain.Entities;
using NSG.Mobile.Domain.Enums;

namespace NSG.Mobile.Infrastructure.Persistence.Configurations;

/// <summary>Table <c>grievance_category</c>. Unique name; rows are deactivated or soft-deleted (<c>is_deleted</c>), never physically deleted.</summary>
public class GrievanceCategoryConfiguration : IEntityTypeConfiguration<GrievanceCategory>
{
    public void Configure(EntityTypeBuilder<GrievanceCategory> builder)
    {
        builder.ToTable("grievance_category", t =>
        {
            t.HasCheckConstraint("ck_grievance_category_name", "CHAR_LENGTH(`grievance_name`) > 0");
        });

        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).HasColumnType("int").ValueGeneratedOnAdd();

        builder.Property(c => c.GrievanceName).IsRequired().HasMaxLength(150);
        builder.Property(c => c.DepartmentId);
        builder.Property(c => c.IsDeleted).IsRequired().HasDefaultValue(false);
        builder.Property(c => c.IsActive).IsRequired().HasDefaultValue(true);
        builder.Property(c => c.CreatedAtIst).IsRequired().HasColumnType("datetime(6)");
        builder.Property(c => c.CreatedBy).HasColumnType("bigint");
        builder.Property(c => c.UpdatedAtIst).HasColumnType("datetime(6)");
        builder.Property(c => c.UpdatedBy).HasColumnType("bigint");

        builder.HasIndex(c => c.GrievanceName).IsUnique().HasDatabaseName("ix_grievance_category_grievance_name");
    }
}

/// <summary>Table <c>grievance</c>. Foreign keys to the category and to the employee profile are Restrict.</summary>
public class GrievanceConfiguration : IEntityTypeConfiguration<Grievance>
{
    public void Configure(EntityTypeBuilder<Grievance> builder)
    {
        builder.ToTable("grievance", t =>
        {
            t.HasCheckConstraint("ck_grievance_current_status", "`current_status` IN (1, 2, 3, 4)");
            t.HasCheckConstraint("ck_grievance_subject", "CHAR_LENGTH(`subject`) > 0");
            t.HasCheckConstraint("ck_grievance_documents", "`document_urls` IS NULL OR JSON_VALID(`document_urls`)");

            // Action Taken and Closed always record who acted, when, and (for Action Taken) the remarks.
            t.HasCheckConstraint("ck_grievance_action_fields",
                "`current_status` < 3 OR (`action_taken_by` IS NOT NULL AND `action_taken_at_ist` IS NOT NULL AND `action_remarks` IS NOT NULL)");
            t.HasCheckConstraint("ck_grievance_closed_fields",
                "`current_status` <> 4 OR (`closed_by` IS NOT NULL AND `closed_at_ist` IS NOT NULL)");
        });

        builder.HasKey(g => g.Id);
        builder.Property(g => g.Id).HasColumnType("bigint").ValueGeneratedOnAdd();

        builder.Property(g => g.TicketNumber).HasColumnName("grievance_id").IsRequired().HasMaxLength(30);
        builder.Property(g => g.CategoryId).IsRequired();
        builder.Property(g => g.Subject).IsRequired().HasMaxLength(200);
        builder.Property(g => g.GrievanceNote).IsRequired().HasMaxLength(2000);
        builder.Property(g => g.DocumentsJson).HasColumnName("document_urls").HasColumnType("json");
        builder.Property(g => g.CurrentStatus).IsRequired().HasConversion<int>();

        builder.Property(g => g.CreatedBy).IsRequired().HasColumnType("bigint");
        builder.Property(g => g.CreatedAtIst).IsRequired().HasColumnType("datetime(6)");
        builder.Property(g => g.UpdatedAtIst).HasColumnType("datetime(6)");

        builder.Property(g => g.AssignedTo).HasColumnType("bigint");
        builder.Property(g => g.ActionTakenBy).HasColumnType("bigint");
        builder.Property(g => g.ActionTakenAtIst).HasColumnType("datetime(6)");
        builder.Property(g => g.ActionRemarks).HasMaxLength(2000);
        builder.Property(g => g.ClosedBy).HasColumnType("bigint");
        builder.Property(g => g.ClosedAtIst).HasColumnType("datetime(6)");
        builder.Property(g => g.ClosedRemarks).HasMaxLength(1000);

        builder.Property(g => g.IsActive).IsRequired().HasDefaultValue(true);

        builder.HasOne(g => g.Category).WithMany().HasForeignKey(g => g.CategoryId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<MobileUserProfile>().WithMany().HasForeignKey(g => g.CreatedBy).OnDelete(DeleteBehavior.Restrict);

        // ix_grievance_category_id (for the foreign key) serves "grievances of a category".
        builder.HasIndex(g => g.TicketNumber).IsUnique().HasDatabaseName("ix_grievance_grievance_id");
        builder.HasIndex(g => new { g.CreatedBy, g.CurrentStatus }).HasDatabaseName("ix_grievance_created_by_current_status");
        builder.HasIndex(g => new { g.CurrentStatus, g.CreatedAtIst }).HasDatabaseName("ix_grievance_current_status_created_at_ist");
    }
}

/// <summary>Table <c>grievance_status_log</c>: append-only history (creation, status changes, comments, forwards). Foreign key to <c>grievance</c> is Restrict.</summary>
public class GrievanceStatusLogConfiguration : IEntityTypeConfiguration<GrievanceStatusLog>
{
    public void Configure(EntityTypeBuilder<GrievanceStatusLog> builder)
    {
        builder.ToTable("grievance_status_log", t =>
        {
            t.HasCheckConstraint("ck_grievance_status_log_from_status", "`from_status` IS NULL OR `from_status` IN (1, 2, 3, 4)");
            t.HasCheckConstraint("ck_grievance_status_log_to_status", "`to_status` IN (1, 2, 3, 4)");
            t.HasCheckConstraint("ck_grievance_status_log_changed_by_type", "`changed_by_type` IN (1, 2)");
            t.HasCheckConstraint("ck_grievance_status_log_log_action", "`log_action` IN (1, 2, 3)");
        });

        builder.HasKey(l => l.Id);
        builder.Property(l => l.Id).HasColumnType("bigint").ValueGeneratedOnAdd();

        builder.Property(l => l.GrievancePk).IsRequired().HasColumnType("bigint");
        builder.Property(l => l.LogAction).IsRequired().HasConversion<int>();
        builder.Property(l => l.ForwardedTo).HasColumnType("bigint");
        builder.Property(l => l.FromStatus).HasConversion<int?>();
        builder.Property(l => l.ToStatus).IsRequired().HasConversion<int>();
        builder.Property(l => l.Remarks).HasMaxLength(1000);
        builder.Property(l => l.ChangedBy).IsRequired().HasColumnType("bigint");
        builder.Property(l => l.ChangedByType).IsRequired().HasConversion<int>();
        builder.Property(l => l.ChangedAtIst).IsRequired().HasColumnType("datetime(6)");

        builder.HasOne<Grievance>().WithMany().HasForeignKey(l => l.GrievancePk).OnDelete(DeleteBehavior.Restrict);

        // Serves the timeline (oldest first) and the foreign key.
        builder.HasIndex(l => new { l.GrievancePk, l.ChangedAtIst }).HasDatabaseName("ix_grievance_status_log_grievance_pk_changed_at_ist");
    }
}
