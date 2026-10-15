using Microsoft.EntityFrameworkCore;
using NSG.Mobile.Domain.Entities;

namespace NSG.Mobile.Infrastructure.Persistence;

/// <summary>
/// Application database context — the single DbContext for the NSG Mobile API.
/// EF Core 8 Code First with MySQL 8.x (Pomelo).
/// </summary>
public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    public DbSet<User> Users => Set<User>();
    public DbSet<Device> Devices => Set<Device>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<OtpRequest> OtpRequests => Set<OtpRequest>();
    public DbSet<RankHierarchy> RankHierarchies => Set<RankHierarchy>();
    public DbSet<State> States => Set<State>();
    public DbSet<City> Cities => Set<City>();
    public DbSet<UserPis> UsersPis => Set<UserPis>();
    public DbSet<NsgUnit> NsgUnits => Set<NsgUnit>();
    public DbSet<EducationQualification> EducationQualifications => Set<EducationQualification>();
    public DbSet<Religion> Religions => Set<Religion>();
    public DbSet<ParentDepartmentRank> ParentDepartmentRanks => Set<ParentDepartmentRank>();
    public DbSet<NsgTrade> NsgTrades => Set<NsgTrade>();
    public DbSet<PdTrade> PdTrades => Set<PdTrade>();
    public DbSet<MobileUserProfile> MobileUserProfiles => Set<MobileUserProfile>();
    public DbSet<ApproveMobileUser> ApproveMobileUsers => Set<ApproveMobileUser>();
    public DbSet<MobileAuthGrant> MobileAuthGrants => Set<MobileAuthGrant>();
    public DbSet<MobileRefreshToken> MobileRefreshTokens => Set<MobileRefreshToken>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Apply all IEntityTypeConfiguration<T> classes from this assembly
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
        modelBuilder.ApplyConfiguration(new Configurations.RankHierarchyConfiguration());

        // Table names are explicit in each configuration; this validates them and names columns, keys, indexes and foreign keys.
        DatabaseNamingStandard.Apply(modelBuilder);
    }
}
