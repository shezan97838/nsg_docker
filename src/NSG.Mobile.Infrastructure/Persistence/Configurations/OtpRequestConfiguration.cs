using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NSG.Mobile.Domain.Entities;
using NSG.Mobile.Domain.Enums;

namespace NSG.Mobile.Infrastructure.Persistence.Configurations;

public class OtpRequestConfiguration : IEntityTypeConfiguration<OtpRequest>
{
    public void Configure(EntityTypeBuilder<OtpRequest> builder)
    {
        builder.ToTable("otp_requests");

        builder.HasKey(o => o.Id);
        builder.Property(o => o.Id).ValueGeneratedOnAdd();

        builder.Property(o => o.ReferenceId)
            .IsRequired();

        builder.Property(o => o.MobileNumber)
            .IsRequired()
            .HasMaxLength(15);

        builder.Property(o => o.OtpHash)
            .IsRequired()
            .HasMaxLength(500);

        builder.Property(o => o.Purpose)
            .IsRequired()
            .HasConversion<int>();

        builder.Property(o => o.ExpiresAt)
            .IsRequired();

        builder.Property(o => o.CreatedAt)
            .IsRequired();

        // Indexes
        builder.HasIndex(o => o.ReferenceId)
            .IsUnique()
            .HasDatabaseName("ix_otp_requests_reference_id");

        builder.HasIndex(o => new { o.MobileNumber, o.Purpose, o.CreatedAt })
            .HasDatabaseName("ix_otp_requests_mobile_purpose_created");
    }
}
