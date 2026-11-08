using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NSG.Mobile.Domain.Entities;

namespace NSG.Mobile.Infrastructure.Persistence.Configurations;

/// <summary>Table <c>pis_ownership_transfers</c>: append-only history of <c>users_pis.assigned_to</c> changes. No foreign keys (see the entity).</summary>
public class PisOwnershipTransferConfiguration : IEntityTypeConfiguration<PisOwnershipTransfer>
{
    public void Configure(EntityTypeBuilder<PisOwnershipTransfer> builder)
    {
        builder.ToTable("pis_ownership_transfers");

        builder.HasKey(t => t.Id);
        builder.Property(t => t.Id).HasColumnType("bigint").ValueGeneratedOnAdd();

        builder.Property(t => t.PisRecordId).IsRequired().HasColumnType("bigint");
        builder.Property(t => t.NsgNumber).IsRequired().HasMaxLength(30);
        builder.Property(t => t.PreviousAssignedTo).HasColumnType("bigint");
        builder.Property(t => t.NewAssignedTo).IsRequired().HasColumnType("bigint");
        builder.Property(t => t.TransferredBy).IsRequired().HasColumnType("bigint");
        builder.Property(t => t.Reason).HasMaxLength(500);
        builder.Property(t => t.TransferredAtIst).IsRequired().HasColumnType("datetime(6)");

        // "History of one record, newest first".
        builder.HasIndex(t => new { t.PisRecordId, t.TransferredAtIst }).HasDatabaseName("ix_pis_ownership_transfers_pis_record_id_transferred_at_ist");
    }
}
