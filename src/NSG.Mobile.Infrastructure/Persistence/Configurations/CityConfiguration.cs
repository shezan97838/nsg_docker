using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NSG.Mobile.Domain.Entities;

namespace NSG.Mobile.Infrastructure.Persistence.Configurations;

public class CityConfiguration : IEntityTypeConfiguration<City>
{
    public void Configure(EntityTypeBuilder<City> builder)
    {
        builder.ToTable("cities");

        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).ValueGeneratedOnAdd();

        builder.Property(c => c.Name).IsRequired().HasMaxLength(200);
        builder.Property(c => c.Code).HasMaxLength(50);
        builder.Property(c => c.Classification).HasConversion<int?>();
        builder.Property(c => c.IsActive).IsRequired().HasDefaultValue(true);
        builder.Property(c => c.SourceSystem).HasMaxLength(50);
        builder.Property(c => c.SourceKey).HasMaxLength(250);
        builder.Property(c => c.CreatedAtIst).IsRequired().HasColumnType("datetime(6)");
        builder.Property(c => c.UpdatedAtIst).HasColumnType("datetime(6)");

        // Deleting a state must never cascade to its cities.
        builder.HasOne(c => c.State)
            .WithMany(s => s.Cities)
            .HasForeignKey(c => c.StateId)
            .OnDelete(DeleteBehavior.Restrict);

        // Idempotent import key. City names are NOT unique, so the source key is the identity.
        builder.HasIndex(c => new { c.SourceSystem, c.SourceKey }).IsUnique().HasDatabaseName("ix_cities_source_system_source_key");
        // State-based city lookups and name search.
        builder.HasIndex(c => new { c.StateId, c.Name }).HasDatabaseName("ix_cities_state_id_name");
        builder.HasIndex(c => new { c.StateId, c.IsActive }).HasDatabaseName("ix_cities_state_id_is_active");
    }
}
