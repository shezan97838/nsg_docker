using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NSG.Mobile.Domain.Entities;
using NSG.Mobile.Domain.Enums;

namespace NSG.Mobile.Infrastructure.Persistence.Configurations;

/// <summary>Table <c>personnel_leave_records</c>: one row per person and calendar year (unique). Previous years are never touched.</summary>
public class PersonnelLeaveRecordConfiguration : IEntityTypeConfiguration<PersonnelLeaveRecord>
{
    public void Configure(EntityTypeBuilder<PersonnelLeaveRecord> builder)
    {
        builder.ToTable("personnel_leave_records", t =>
        {
            t.HasCheckConstraint("ck_personnel_leave_records_leave_taken", "`leave_taken` >= 0 AND `leave_taken` <= 366");
            t.HasCheckConstraint("ck_personnel_leave_records_leave_year", "`leave_year` >= 2000 AND `leave_year` <= 2999");
        });

        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).HasColumnType("bigint").ValueGeneratedOnAdd();

        builder.Property(r => r.NsgNumber).IsRequired().HasMaxLength(30);          // same length as users_pis.nsg_number
        builder.Property(r => r.LeaveYear).IsRequired();
        builder.Property(r => r.LeaveTaken).IsRequired();
        builder.Property(r => r.RowVersion).IsRequired().HasDefaultValue(1).IsConcurrencyToken();
        builder.Property(r => r.CreatedAtIst).IsRequired().HasColumnType("datetime(6)");
        builder.Property(r => r.CreatedBy).IsRequired().HasColumnType("bigint");
        builder.Property(r => r.UpdatedAtIst).IsRequired().HasColumnType("datetime(6)");
        builder.Property(r => r.UpdatedBy).IsRequired().HasColumnType("bigint");

        // One record per person and year; also serves "this person's records" and the batch look-up by NSG number.
        builder.HasIndex(r => new { r.NsgNumber, r.LeaveYear }).IsUnique().HasDatabaseName("ix_personnel_leave_records_nsg_number_leave_year");
    }
}

/// <summary>Table <c>personnel_leave_upload_batches</c>: one row per bulk upload attempt, whatever its outcome.</summary>
public class PersonnelLeaveUploadBatchConfiguration : IEntityTypeConfiguration<PersonnelLeaveUploadBatch>
{
    public void Configure(EntityTypeBuilder<PersonnelLeaveUploadBatch> builder)
    {
        builder.ToTable("personnel_leave_upload_batches", t =>
        {
            t.HasCheckConstraint("ck_personnel_leave_upload_batches_upload_status", "`upload_status` IN (1, 2, 3, 4)");
            t.HasCheckConstraint("ck_personnel_leave_upload_batches_counts",
                "`total_rows` >= 0 AND `created_count` >= 0 AND `updated_count` >= 0 AND `unchanged_count` >= 0 AND `failed_count` >= 0");
            // The lock is 1 exactly while the batch is Processing, so the unique index admits at most one running upload.
            t.HasCheckConstraint("ck_personnel_leave_upload_batches_processing_lock",
                "(`upload_status` = 1 AND `processing_lock` = 1) OR (`upload_status` <> 1 AND `processing_lock` IS NULL)");
        });

        builder.HasKey(b => b.Id);
        builder.Property(b => b.Id).HasColumnType("bigint").ValueGeneratedOnAdd();

        builder.Property(b => b.FileName).IsRequired().HasMaxLength(255);
        builder.Property(b => b.LeaveYear).IsRequired();
        builder.Property(b => b.TotalRows).IsRequired();
        builder.Property(b => b.CreatedCount).IsRequired();
        builder.Property(b => b.UpdatedCount).IsRequired();
        builder.Property(b => b.UnchangedCount).IsRequired();
        builder.Property(b => b.FailedCount).IsRequired();
        builder.Property(b => b.UploadStatus).IsRequired().HasConversion<int>();
        builder.Property(b => b.ProcessingLock);
        builder.Property(b => b.CreatedAtIst).IsRequired().HasColumnType("datetime(6)");
        builder.Property(b => b.CreatedBy).IsRequired().HasColumnType("bigint");
        builder.Property(b => b.CompletedAtIst).HasColumnType("datetime(6)");

        builder.HasIndex(b => b.ProcessingLock).IsUnique().HasDatabaseName("ix_personnel_leave_upload_batches_processing_lock");
        builder.HasIndex(b => b.CreatedAtIst).HasDatabaseName("ix_personnel_leave_upload_batches_created_at_ist");
    }
}

/// <summary>Table <c>personnel_leave_upload_errors</c>: validation and processing problems of a batch. Foreign key to the batch is Restrict.</summary>
public class PersonnelLeaveUploadErrorConfiguration : IEntityTypeConfiguration<PersonnelLeaveUploadError>
{
    public void Configure(EntityTypeBuilder<PersonnelLeaveUploadError> builder)
    {
        builder.ToTable("personnel_leave_upload_errors");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasColumnType("bigint").ValueGeneratedOnAdd();

        builder.Property(e => e.BatchId).IsRequired().HasColumnType("bigint");
        // "row_number" is a reserved word in MySQL 8 (window function), so the column is excel_row_number.
        builder.Property(e => e.RowNumber).HasColumnName("excel_row_number");
        builder.Property(e => e.NsgNumber).HasMaxLength(30);
        builder.Property(e => e.ErrorMessage).IsRequired().HasMaxLength(500);
        builder.Property(e => e.CreatedAtIst).IsRequired().HasColumnType("datetime(6)");

        builder.HasOne<PersonnelLeaveUploadBatch>().WithMany(b => b.Errors).HasForeignKey(e => e.BatchId)
            .HasConstraintName("fk_personnel_leave_upload_errors_batch_id").OnDelete(DeleteBehavior.Restrict);

        // Serves the error report (rows in file order) and the foreign key.
        builder.HasIndex(e => new { e.BatchId, e.RowNumber }).HasDatabaseName("ix_personnel_leave_upload_errors_batch_id_excel_row_number");
    }
}

/// <summary>Table <c>personnel_leave_history</c>: append-only audit of every actual leave count change. Foreign keys are Restrict.</summary>
public class PersonnelLeaveHistoryConfiguration : IEntityTypeConfiguration<PersonnelLeaveHistory>
{
    public void Configure(EntityTypeBuilder<PersonnelLeaveHistory> builder)
    {
        builder.ToTable("personnel_leave_history", t =>
        {
            t.HasCheckConstraint("ck_personnel_leave_history_change_source", "`change_source` IN (1, 2)");
            t.HasCheckConstraint("ck_personnel_leave_history_counts", "(`old_leave_taken` IS NULL OR `old_leave_taken` >= 0) AND `new_leave_taken` >= 0");
            // (A bulk change always names its batch and a manual one never does: guaranteed by the repository. MySQL forbids CHECK constraints on foreign-key columns with referential actions, so it is not a constraint.)
        });

        builder.HasKey(h => h.Id);
        builder.Property(h => h.Id).HasColumnType("bigint").ValueGeneratedOnAdd();

        builder.Property(h => h.LeaveRecordId).IsRequired().HasColumnType("bigint");
        builder.Property(h => h.NsgNumber).IsRequired().HasMaxLength(30);
        builder.Property(h => h.LeaveYear).IsRequired();
        builder.Property(h => h.OldLeaveTaken);
        builder.Property(h => h.NewLeaveTaken).IsRequired();
        builder.Property(h => h.ChangeSource).IsRequired().HasConversion<int>();
        builder.Property(h => h.UploadBatchId).HasColumnType("bigint");
        builder.Property(h => h.ChangedBy).IsRequired().HasColumnType("bigint");
        builder.Property(h => h.ChangedAtIst).IsRequired().HasColumnType("datetime(6)");

        builder.HasOne<PersonnelLeaveRecord>().WithMany().HasForeignKey(h => h.LeaveRecordId)
            .HasConstraintName("fk_personnel_leave_history_leave_record_id").OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<PersonnelLeaveUploadBatch>().WithMany().HasForeignKey(h => h.UploadBatchId)
            .HasConstraintName("fk_personnel_leave_history_upload_batch_id").OnDelete(DeleteBehavior.Restrict);

        // Serves one person's history (newest first); ix_personnel_leave_history_upload_batch_id (for the foreign key) serves "changes of a batch".
        builder.HasIndex(h => new { h.NsgNumber, h.ChangedAtIst }).HasDatabaseName("ix_personnel_leave_history_nsg_number_changed_at_ist");
    }
}
