using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NSG.Mobile.Domain.Entities;

namespace NSG.Mobile.Infrastructure.Persistence.Configurations;

public class StateConfiguration : IEntityTypeConfiguration<State>
{
    public void Configure(EntityTypeBuilder<State> builder)
    {
        builder.ToTable("states", t =>
        {
            // Database-level guards in addition to application validation.
            t.HasCheckConstraint("ck_states_administrative_type", "`administrative_type` IN (1, 2)");
            // Official LGD state codes are exactly two digits; the string type preserves leading zeros ("01").
            t.HasCheckConstraint("ck_states_code_format", "`code` IS NULL OR `code` REGEXP '^[0-9]{2}$'");
        });

        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).ValueGeneratedOnAdd();

        builder.Property(s => s.Name).IsRequired().HasMaxLength(100);
        builder.Property(s => s.Code).HasMaxLength(10);
        builder.Property(s => s.AdministrativeType).IsRequired().HasConversion<int>();
        builder.Property(s => s.IsActive).IsRequired().HasDefaultValue(true);
        builder.Property(s => s.CreatedAtIst).IsRequired().HasColumnType("datetime(6)");
        builder.Property(s => s.UpdatedAtIst).HasColumnType("datetime(6)");

        // Unique official code (multiple NULLs are permitted by MySQL unique indexes).
        builder.HasIndex(s => s.Code).IsUnique().HasDatabaseName("ix_states_code");
        builder.HasIndex(s => s.Name).IsUnique().HasDatabaseName("ix_states_name");
        builder.HasIndex(s => new { s.AdministrativeType, s.IsActive }).HasDatabaseName("ix_states_administrative_type_is_active");
    }
}
