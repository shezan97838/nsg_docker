# EF Core & MySQL 8.x Skills

## DbContext & Provider
- Provider: `Pomelo.EntityFrameworkCore.MySql` (v8.0.2)
- Server target: MySQL 8.x (InnoDB engine)
- Single context: `AppDbContext` in `Infrastructure/Persistence/`
- Configurations discovered via `ApplyConfigurationsFromAssembly()`
- Registration pattern:
  ```csharp
  var connectionString = configuration.GetConnectionString("MySql");
  services.AddDbContext<AppDbContext>(options =>
      options.UseMySql(connectionString, ServerVersion.AutoDetect(connectionString),
          my =>
          {
              my.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName);
              my.EnableRetryOnFailure(3);
          }));
  ```

## Entity Configuration Pattern
```csharp
public class MyEntityConfiguration : IEntityTypeConfiguration<MyEntity> {
    public void Configure(EntityTypeBuilder<MyEntity> builder) {
        builder.ToTable("my_entities");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedOnAdd(); // AUTO_INCREMENT in MySQL
        builder.Property(e => e.Name).IsRequired().HasMaxLength(200);
        builder.Property(e => e.Status).HasConversion<int>();
        builder.Property(e => e.Payload).HasColumnType("json"); // MySQL native JSON
        builder.Property(e => e.CreatedAt).HasColumnType("datetime(6)"); // Microsecond precision
        builder.HasIndex(e => e.Name).HasDatabaseName("IX_MyEntities_Name");
    }
}
```

## Current Tables
| Table | Key Indexes | Key Column Types / Features |
|-------|------------|-----------------------------|
| Users | MobileNumber (unique), ForceNumber (unique), Status | AUTO_INCREMENT PK, RowVersion |
| Devices | UserId+Status, UserId+DeviceIdentifierHash | AUTO_INCREMENT PK |
| refresh_tokens | token_hash (unique), family_id, user_id+revoked_at_utc | AUTO_INCREMENT PK, rotation chain |
| audit_logs | created_at_utc, user_id+created_at_utc, action | AUTO_INCREMENT PK, `additional_data` (json) |
| OtpRequests | ReferenceId (unique), MobileNumber+Purpose+CreatedAt | AUTO_INCREMENT PK |

## MySQL 8.x Specific Rules
1. **Primary Keys & AUTO_INCREMENT**:
   - Use `builder.Property(e => e.Id).ValueGeneratedOnAdd();` for integer PKs.
   - Do NOT use SQL Server-specific `.UseIdentityColumn()`.
2. **Case Sensitivity & Operating System**:
   - Linux/Ubuntu deployments have case-sensitive filesystems where `lower_case_table_names=0`.
   - All table, column, index and constraint names are lowercase `snake_case` (for example `.ToTable("users_pis")`); tables are mapped explicitly and the rest is derived by `DatabaseNamingStandard`. Full rules: `docs/DATABASE-STANDARDS.md`.
3. **JSON Types**:
   - For unstructured/semi-structured data payloads (e.g., `audit_logs.additional_data`), use `.HasColumnType("json")`.
   - Never use SQL Server `nvarchar(max)` data types.
4. **Timestamps & Precision**:
   - MySQL default `datetime` has second precision; use `datetime(6)` for microsecond precision on audit and timestamp properties where sub-second precision is required.
5. **Index Length Limits**:
   - Under MySQL InnoDB `utf8mb4`, the maximum index key length is 3072 bytes (768 characters for 4-byte characters).
   - Ensure indexed `varchar` columns have explicit lengths (e.g., `HasMaxLength(200)`).
6. **Connection Resilience**:
   - Connection resilience configured with `.EnableRetryOnFailure(3)` via Pomelo.
7. **Query Rules**:
   - `AsNoTracking()` for all read-only queries.
   - Explicit `Include` (no lazy loading).
   - Delete behavior: `Restrict` (not Cascade).

## Migration State
**No migrations have been created yet.** Initial migration is a future task.
