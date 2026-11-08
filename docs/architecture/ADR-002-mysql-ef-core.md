# ADR-002: MySQL 8.x + EF Core 10 (Microting.EntityFrameworkCore.MySql Provider)

**Status**: Accepted (Supersedes ADR-002-sql-server-ef-core)  
**Date**: 2026-10-12  
**SOW Reference**: §4 — Technical Requirements ("Database Strategy Pivot")

## Context
The project initially targeted Microsoft SQL Server per SOW §4. However, due to deployment and configuration issues on target infrastructure (including Linux/Ubuntu hosting environments and licensing/operational factors), the database strategy has been officially pivoted to MySQL 8.x.

## Decision
- Pivot the relational database engine to **MySQL 8.x**.
- Use **EF Core 10** (10.0.14) with the **Microting.EntityFrameworkCore.MySql** provider (10.0.14).
- Use Code First with explicit `IEntityTypeConfiguration<T>` classes.
- Primary key generation: `AUTO_INCREMENT` using `.ValueGeneratedOnAdd()`.
- Explicit column types where necessary:
  - Complex / unstructured data (e.g., `AuditLogs.AdditionalData`) stored using MySQL native `json` column type.
  - Timestamps stored with microsecond precision (`datetime(6)`).
  - Explicit string column lengths (`varchar(N)`) to stay well within MySQL index key length limits (3072 bytes in InnoDB / utf8mb4).
- Operating System & Table Casing: Target deployment on Ubuntu/Linux requires strict, consistent naming conventions due to `lower_case_table_names=0` case sensitivity.
- Resilient connection handling: `EnableRetryOnFailure(3)`.
- Query performance: `AsNoTracking()` for read-only queries and explicit `Include` loading (no lazy loading).
- Health check monitoring: `AspNetCore.HealthChecks.MySql`.

## Consequences
- Cost-effective, open, and native Linux/Ubuntu deployment compatibility.
- Schema remains fully version-controlled, auditable, and managed through EF Core migrations.
- High compatibility with containerized and Linux-based cloud infrastructure (NIC Cloud / NSG infrastructure).
- Supersedes the original SQL Server ADR.

## Addendum 2026-10-14: lowercase snake_case for all tables and columns

The original decision relied on every tool preserving PascalCase. Development runs on Windows (`lower_case_table_names=1`, table names stored in lowercase) while production runs on Linux (`lower_case_table_names=0`, case-sensitive), so a dump moved between them breaks mixed-case names. **Every** table, column, index and constraint is now lowercase `snake_case`, mapped explicitly and enforced by `DatabaseNamingStandard` and tests. The existing PascalCase tables were converted by the corrective migration `Standardize_Database_Names_To_Snake_Case`. See `docs/DATABASE-STANDARDS.md` and `docs/DATABASE-SCHEMA-AUDIT.md`.

## Addendum 2026-11-08: provider change from Pomelo to Microting (.NET 10 upgrade)

The project was originally built on `Pomelo.EntityFrameworkCore.MySql` 8.0.2 (EF Core 8). Pomelo 9.0.0 has no EF Core 10 release, so when the project moved to .NET 10 / EF Core 10 the provider was switched to `Microting.EntityFrameworkCore.MySql` 10.0.14, a community fork of Pomelo that keeps the same API (`UseMySql`, `ServerVersion`, `MySqlServerVersion` and the `MySql*` metadata extensions stay in the same namespaces). No historical migration or Designer file needed changes. The rest of this decision is unchanged.
