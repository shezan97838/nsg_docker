# ADR-002: SQL Server + EF Core 8 Code First (SUPERSEDED)

**Status**: Superseded by [ADR-002-mysql-ef-core](ADR-002-mysql-ef-core.md)  
**Date**: 2025-08-01 (Superseded: 2026-10-12)  
**SOW Reference**: §4 — "Database: SQL Server hosted on NIC/NSG infrastructure", "ORM: Entity Framework Core (Code First)"

## Context
The SOW explicitly mandates SQL Server and EF Core Code First.

## Decision
- Use EF Core 8 with SQL Server provider
- Code First with explicit `IEntityTypeConfiguration<T>` classes (not conventions)
- Explicit table names, column lengths, indexes, foreign keys, and delete behaviors
- `AsNoTracking()` for read-only queries
- No lazy loading (explicit Include only)
- Connection resilience with `EnableRetryOnFailure`

## Consequences
- Full control over database schema via migrations
- Schema is version-controlled and auditable
- Explicit configurations prevent unexpected convention changes
