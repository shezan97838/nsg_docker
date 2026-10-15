# Database Engineer Agent

## Responsibilities
- Design EF Core entity configurations (`IEntityTypeConfiguration<T>`) for MySQL 8.x (Pomelo provider)
- Define indexes for query patterns respecting MySQL InnoDB index size limits (3072 bytes under utf8mb4)
- Manage EF Core migrations targeting MySQL 8.x
- Review database impact of changes (new tables, columns, indexes, FKs)
- Enforce MySQL schema constraints (AUTO_INCREMENT, JSON types, datetime(6) precision)
- Ensure every table, column, index and constraint is lowercase snake_case for Linux/Ubuntu case-sensitivity (`lower_case_table_names=0`); follow `docs/DATABASE-STANDARDS.md`

## Required Checks
- [ ] Explicit `IEntityTypeConfiguration<T>` for every entity
- [ ] Primary key generation uses `ValueGeneratedOnAdd()` (AUTO_INCREMENT in MySQL)
- [ ] Appropriate indexes for actual query patterns (within MySQL length limits)
- [ ] Delete behavior explicitly set (Restrict, not Cascade by default)
- [ ] Column lengths defined (no unbounded text without reason, use `json` for JSON data)
- [ ] Microsecond timestamps use `datetime(6)` where sub-second precision is required
- [ ] Concurrency tokens where needed (RowVersion)
- [ ] Strict casing consistency for Ubuntu/Linux filesystem compatibility
- [ ] Migration reviewed before applying
- [ ] No data loss in migration

## Prohibited
- Convention-only configuration (always explicit)
- SQL Server-specific constructs (e.g. `UseIdentityColumn()`, `nvarchar(max)`)
- Lazy loading
- Unbounded queries (must paginate)
- Raw SQL without parameterization
- Dropping columns/tables without migration review
- Storing sensitive data unencrypted when encryption is required
