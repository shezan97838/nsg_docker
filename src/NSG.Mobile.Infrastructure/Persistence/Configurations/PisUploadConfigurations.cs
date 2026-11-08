using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NSG.Mobile.Domain.Entities;

namespace NSG.Mobile.Infrastructure.Persistence.Configurations;

/// <summary>Table <c>pis_upload_batches</c>: one row per PIS Excel upload attempt, whatever its outcome.</summary>
public class PisUploadBatchConfiguration : IEntityTypeConfiguration<PisUploadBatch>
{
    public void Configure(EntityTypeBuilder<PisUploadBatch> builder)
    {
        builder.ToTable("pis_upload_batches", t =>
        {
            t.HasCheckConstraint("ck_pis_upload_batches_upload_type", "`upload_type` IN (1, 2)");
            t.HasCheckConstraint("ck_pis_upload_batches_status", "`status` IN (1, 2, 3, 4, 5)");
            t.HasCheckConstraint("ck_pis_upload_batches_counts",
                "`total_rows` >= 0 AND `inserted_count` >= 0 AND `updated_count` >= 0 AND `failed_count` >= 0 AND `conflict_count` >= 0");
        });

        builder.HasKey(b => b.Id);
        builder.Property(b => b.Id).HasColumnType("bigint").ValueGeneratedOnAdd();

        builder.Property(b => b.FileName).IsRequired().HasMaxLength(255);
        builder.Property(b => b.UploadType).IsRequired().HasConversion<int>();
        builder.Property(b => b.Status).IsRequired().HasConversion<int>();
        builder.Property(b => b.TotalRows).IsRequired();
        builder.Property(b => b.InsertedCount).IsRequired();
        builder.Property(b => b.UpdatedCount).IsRequired();
        builder.Property(b => b.FailedCount).IsRequired();
        builder.Property(b => b.ConflictCount).IsRequired();
        builder.Property(b => b.FailureMessage).HasMaxLength(500);
        builder.Property(b => b.UploadedBy).IsRequired().HasColumnType("bigint");
        builder.Property(b => b.CreatedAtIst).IsRequired().HasColumnType("datetime(6)");
        builder.Property(b => b.CompletedAtIst).HasColumnType("datetime(6)");

        // Serves "my batches, newest first".
        builder.HasIndex(b => new { b.UploadedBy, b.CreatedAtIst }).HasDatabaseName("ix_pis_upload_batches_uploaded_by_created_at_ist");
    }
}

/// <summary>Table <c>pis_upload_batch_records</c>: the personnel records a batch inserted or updated. Foreign key to the batch is Restrict.</summary>
public class PisUploadBatchRecordConfiguration : IEntityTypeConfiguration<PisUploadBatchRecord>
{
    public void Configure(EntityTypeBuilder<PisUploadBatchRecord> builder)
    {
        builder.ToTable("pis_upload_batch_records", t =>
            t.HasCheckConstraint("ck_pis_upload_batch_records_operation", "`operation` IN (1, 2)"));

        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).HasColumnType("bigint").ValueGeneratedOnAdd();

        builder.Property(r => r.BatchId).IsRequired().HasColumnType("bigint");
        // "row_number" is a reserved word in MySQL 8, so the column is excel_row_number (as in the Leave module).
        builder.Property(r => r.RowNumber).IsRequired().HasColumnName("excel_row_number");
        // No foreign key: users_pis has none in either direction (ADR-020).
        builder.Property(r => r.PisRecordId).IsRequired().HasColumnType("bigint");
        builder.Property(r => r.NsgNumber).IsRequired().HasMaxLength(30);
        builder.Property(r => r.Operation).IsRequired().HasConversion<int>();
        builder.Property(r => r.CreatedAtIst).IsRequired().HasColumnType("datetime(6)");

        builder.HasOne<PisUploadBatch>().WithMany(b => b.Records).HasForeignKey(r => r.BatchId)
            .HasConstraintName("fk_pis_upload_batch_records_batch_id").OnDelete(DeleteBehavior.Restrict);

        // UNIQUE: a worksheet row is mapped at most once per batch. Serves a batch's records in file order and the foreign key; the second index answers "which batches touched this record".
        builder.HasIndex(r => new { r.BatchId, r.RowNumber }).IsUnique().HasDatabaseName("ix_pis_upload_batch_records_batch_id_excel_row_number");
        builder.HasIndex(r => r.PisRecordId).HasDatabaseName("ix_pis_upload_batch_records_pis_record_id");
    }
}

/// <summary>Table <c>pis_upload_batch_errors</c>: row-level rejections of a batch. Foreign key to the batch is Restrict.</summary>
public class PisUploadBatchErrorConfiguration : IEntityTypeConfiguration<PisUploadBatchError>
{
    public void Configure(EntityTypeBuilder<PisUploadBatchError> builder)
    {
        builder.ToTable("pis_upload_batch_errors", t =>
            t.HasCheckConstraint("ck_pis_upload_batch_errors_status", "`status` IN (1, 2)"));

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasColumnType("bigint").ValueGeneratedOnAdd();

        builder.Property(e => e.BatchId).IsRequired().HasColumnType("bigint");
        builder.Property(e => e.RowNumber).IsRequired().HasColumnName("excel_row_number");
        builder.Property(e => e.NsgNumber).HasMaxLength(30);
        builder.Property(e => e.Status).IsRequired().HasConversion<int>();
        builder.Property(e => e.ConflictType).IsRequired().HasMaxLength(50);
        builder.Property(e => e.Field).HasMaxLength(60);
        builder.Property(e => e.Value).HasMaxLength(200);
        builder.Property(e => e.Message).IsRequired().HasMaxLength(500);
        builder.Property(e => e.ConflictingNsgNumber).HasMaxLength(30);
        builder.Property(e => e.CreatedAtIst).IsRequired().HasColumnType("datetime(6)");

        builder.HasOne<PisUploadBatch>().WithMany(b => b.Errors).HasForeignKey(e => e.BatchId)
            .HasConstraintName("fk_pis_upload_batch_errors_batch_id").OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(e => new { e.BatchId, e.RowNumber }).HasDatabaseName("ix_pis_upload_batch_errors_batch_id_excel_row_number");
    }
}
