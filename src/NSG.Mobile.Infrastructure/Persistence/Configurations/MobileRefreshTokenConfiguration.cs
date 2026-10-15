using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NSG.Mobile.Domain.Entities;

namespace NSG.Mobile.Infrastructure.Persistence.Configurations;

/// <summary>Table <c>mobile_refresh_tokens</c>: refresh tokens of mobile sessions, hash only. Foreign key to <c>mobile_user_profiles</c> (Restrict).</summary>
public class MobileRefreshTokenConfiguration : IEntityTypeConfiguration<MobileRefreshToken>
{
    public void Configure(EntityTypeBuilder<MobileRefreshToken> builder)
    {
        builder.ToTable("mobile_refresh_tokens");

        builder.HasKey(t => t.Id);
        builder.Property(t => t.Id).HasColumnType("bigint").ValueGeneratedOnAdd();

        builder.Property(t => t.ProfileId).IsRequired().HasColumnType("bigint");
        builder.Property(t => t.TokenHash).IsRequired().HasMaxLength(100);   // base64 SHA-256 is 44 characters
        builder.Property(t => t.FamilyId).IsRequired();
        builder.Property(t => t.ExpiresAtIst).IsRequired().HasColumnType("datetime(6)");
        builder.Property(t => t.CreatedAtIst).IsRequired().HasColumnType("datetime(6)");
        builder.Property(t => t.CreatedByIp).HasMaxLength(45);
        builder.Property(t => t.RevokedAtIst).HasColumnType("datetime(6)");
        builder.Property(t => t.RevokeReason).HasMaxLength(50);
        builder.Property(t => t.ReuseDetectedAtIst).HasColumnType("datetime(6)");

        builder.HasOne<MobileUserProfile>().WithMany().HasForeignKey(t => t.ProfileId).OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(t => t.TokenHash).IsUnique().HasDatabaseName("ix_mobile_refresh_tokens_token_hash");
        builder.HasIndex(t => t.FamilyId).HasDatabaseName("ix_mobile_refresh_tokens_family_id");
        // Serves "revoke every live session of this profile".
        builder.HasIndex(t => new { t.ProfileId, t.RevokedAtIst }).HasDatabaseName("ix_mobile_refresh_tokens_profile_id_revoked_at_ist");
    }
}
