using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NSG.Mobile.Domain.Constants;
using NSG.Mobile.Domain.Entities;
using NSG.Mobile.Domain.Enums;

namespace NSG.Mobile.Infrastructure.Persistence.Configurations;

/// <summary>Table <c>mobile_auth_grants</c>: short-lived single-use grants of the mobile registration / MPIN flows. Hash only; never the secret.</summary>
public class MobileAuthGrantConfiguration : IEntityTypeConfiguration<MobileAuthGrant>
{
    public void Configure(EntityTypeBuilder<MobileAuthGrant> builder)
    {
        builder.ToTable("mobile_auth_grants", t =>
        {
            t.HasCheckConstraint("ck_mobile_auth_grants_purpose", "`purpose` IN (0, 1, 2)");
            t.HasCheckConstraint("ck_mobile_auth_grants_mobile_number_format", $"`mobile_number` REGEXP '{DomainConstants.PisFormats.MobileNumberPattern}'");
        });

        builder.HasKey(g => g.Id);
        builder.Property(g => g.Id).HasColumnType("bigint").ValueGeneratedOnAdd();

        builder.Property(g => g.TokenHash).IsRequired().HasMaxLength(100);   // base64 SHA-256 is 44 characters
        builder.Property(g => g.Purpose).IsRequired().HasConversion<int>();
        builder.Property(g => g.NsgNumber).IsRequired().HasMaxLength(30);
        builder.Property(g => g.MobileNumber).IsRequired().HasMaxLength(15);
        builder.Property(g => g.ExpiresAtIst).IsRequired().HasColumnType("datetime(6)");
        builder.Property(g => g.ConsumedAtIst).HasColumnType("datetime(6)");
        builder.Property(g => g.CreatedAtIst).IsRequired().HasColumnType("datetime(6)");

        builder.HasIndex(g => g.TokenHash).IsUnique().HasDatabaseName("ix_mobile_auth_grants_token_hash");

        // Serves "invalidate every outstanding grant of this NSG number".
        builder.HasIndex(g => new { g.NsgNumber, g.ConsumedAtIst }).HasDatabaseName("ix_mobile_auth_grants_nsg_number_consumed_at_ist");
    }
}
