using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NSG.Mobile.Domain.Entities;

namespace NSG.Mobile.Infrastructure.Persistence.Configurations;

/// <summary>Table <c>salary_slip_upload_batches</c>: one row per salary slip PDF upload attempt, whatever its outcome.</summary>
public class SalarySlipUploadBatchConfiguration : IEntityTypeConfiguration<SalarySlipUploadBatch>
{
    public void Configure(EntityTypeBuilder<SalarySlipUploadBatch> builder)
    {
        builder.ToTable("salary_slip_upload_batches", t =>
        {
            t.HasCheckConstraint("ck_salary_slip_upload_batches_status", "`status` IN (0, 1, 2, 3)");
            t.HasCheckConstraint("ck_salary_slip_upload_batches_pages",
                "`total_pages` >= 0 AND `processed_pages` >= 0 AND `unmatched_pages` >= 0");
        });

        builder.HasKey(b => b.Id);
        builder.Property(b => b.Id).HasColumnType("bigint").ValueGeneratedOnAdd();

        builder.Property(b => b.OriginalFileName).IsRequired().HasMaxLength(255);
        builder.Property(b => b.SalaryMonth).IsRequired().HasColumnType("date");
        builder.Property(b => b.TotalPages).IsRequired();
        builder.Property(b => b.ProcessedPages).IsRequired();
        builder.Property(b => b.UnmatchedPages).IsRequired();
        builder.Property(b => b.UnmatchedPageNumbers).HasColumnType("text");
        builder.Property(b => b.FailureMessage).HasMaxLength(500);
        builder.Property(b => b.UploadedBy).IsRequired().HasColumnType("bigint");
        builder.Property(b => b.Status).IsRequired().HasConversion<int>();
        builder.Property(b => b.CreatedAtIst).IsRequired().HasColumnType("datetime(6)");
        builder.Property(b => b.UpdatedAtIst).IsRequired().HasColumnType("datetime(6)");

        // Serves "my batches, newest first" and the salary month filter.
        builder.HasIndex(b => new { b.UploadedBy, b.SalaryMonth, b.CreatedAtIst })
            .HasDatabaseName("ix_salary_slip_batches_uploaded_by_salary_month_created_at_ist");
    }
}

/// <summary>Table <c>salary_slips</c>: one generated PDF per bank account and salary month. Foreign key to the batch is Restrict.</summary>
public class SalarySlipConfiguration : IEntityTypeConfiguration<SalarySlip>
{
    public void Configure(EntityTypeBuilder<SalarySlip> builder)
    {
        builder.ToTable("salary_slips", t =>
            t.HasCheckConstraint("ck_salary_slips_page_count", "`page_count` > 0"));

        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).HasColumnType("bigint").ValueGeneratedOnAdd();

        // Ciphertext of the Bank A/c No (same IEncryptionService and length as users_pis.salary_account_number_encrypted).
        builder.Property(s => s.AccountNumberEncrypted).IsRequired().HasMaxLength(512);
        builder.Property(s => s.SalaryMonth).IsRequired().HasColumnType("date");
        // Soft reference to users_pis.id: no foreign key (ADR-020).
        builder.Property(s => s.UserPisId).HasColumnType("bigint");
        builder.Property(s => s.FilePath).IsRequired().HasMaxLength(500);
        builder.Property(s => s.PageCount).IsRequired();
        builder.Property(s => s.OriginalBatchId).IsRequired().HasColumnType("bigint");
        builder.Property(s => s.CreatedAtIst).IsRequired().HasColumnType("datetime(6)");
        builder.Property(s => s.UpdatedAtIst).IsRequired().HasColumnType("datetime(6)");

        builder.HasOne(s => s.OriginalBatch).WithMany(b => b.SalarySlips).HasForeignKey(s => s.OriginalBatchId)
            .HasConstraintName("fk_salary_slips_original_batch_id").OnDelete(DeleteBehavior.Restrict);

        // Business rule: one slip per bank account and salary month. The account is randomly-IV ciphertext, so the database
        // cannot enforce it; the processing service must decrypt the month's slips, compare, and hold a per-month lock.
        // user_pis_id is only a reference to the matched PIS record (not unique, not used for mobile matching).
        builder.HasIndex(s => new { s.UserPisId, s.SalaryMonth }).HasDatabaseName("ix_salary_slips_user_pis_id_salary_month");
        // Serves a batch's slips and the foreign key.
        builder.HasIndex(s => s.OriginalBatchId).HasDatabaseName("ix_salary_slips_original_batch_id");
        // Serves the duplicate check for unmatched accounts and month filters.
        builder.HasIndex(s => s.SalaryMonth).HasDatabaseName("ix_salary_slips_salary_month");
    }
}
