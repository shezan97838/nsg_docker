using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NSG.Mobile.Domain.Entities;
using NSG.Mobile.Domain.Enums;

namespace NSG.Mobile.Infrastructure.Persistence.Configurations;

public class AuditLogConfiguration : IEntityTypeConfiguration<AuditLog>
{
    public void Configure(EntityTypeBuilder<AuditLog> builder)
    {
        builder.ToTable("audit_logs");

        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).ValueGeneratedOnAdd();

        builder.Property(a => a.Action)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(100);

        builder.Property(a => a.EntityType)
            .HasMaxLength(100);

        builder.Property(a => a.EntityId)
            .HasMaxLength(100);

        builder.Property(a => a.Result)
            .IsRequired()
            .HasMaxLength(50);

        builder.Property(a => a.IpAddress)
            .HasMaxLength(45);

        builder.Property(a => a.CorrelationId)
            .HasMaxLength(100);

        builder.Property(a => a.AdditionalData)
            .HasColumnType("json");

        builder.Property(a => a.CreatedAt)
            .IsRequired();

        // Indexes for common query patterns
        builder.HasIndex(a => a.CreatedAt)
            .HasDatabaseName("ix_audit_logs_created_at");

        builder.HasIndex(a => new { a.UserId, a.CreatedAt })
            .HasDatabaseName("ix_audit_logs_user_id_created_at");

        builder.HasIndex(a => a.Action)
            .HasDatabaseName("ix_audit_logs_action");
    }
}
