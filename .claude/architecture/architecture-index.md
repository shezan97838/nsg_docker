# Architecture — Current State

See `docs/ARCHITECTURE.md` for full architecture documentation.
See `docs/architecture/ADR-*.md` for Architecture Decision Records.

## ADR Index

| ADR | Title | Status |
|-----|-------|--------|
| [ADR-001](../../docs/architecture/ADR-001-clean-architecture.md) | Clean Architecture | Accepted |
| [ADR-002](../../docs/architecture/ADR-002-mysql-ef-core.md) | MySQL 8.x + EF Core 8 (Pomelo) | Accepted (Supersedes SQL Server) |
| [ADR-003](../../docs/architecture/ADR-003-jwt-access-tokens.md) | JWT Access Tokens | Accepted |
| [ADR-004](../../docs/architecture/ADR-004-refresh-token-rotation.md) | Refresh Token Rotation | Accepted |
| [ADR-005](../../docs/architecture/ADR-005-phone-number-authentication.md) | Phone Number Authentication | Accepted |
| [ADR-006](../../docs/architecture/ADR-006-single-device-binding.md) | Single Device Binding | Accepted |
| [ADR-007](../../docs/architecture/ADR-007-biometric-authentication-boundary.md) | Biometric Auth Boundary | Accepted |
| [ADR-008](../../docs/architecture/ADR-008-redis-distributed-cache.md) | Redis Distributed Cache | Accepted |
| [ADR-009](../../docs/architecture/ADR-009-audit-logging.md) | Audit Logging | Accepted |
| [ADR-010](../../docs/architecture/ADR-010-single-tenant-architecture.md) | Single-Tenant Architecture | Accepted |
| [ADR-011](../../docs/architecture/ADR-011-external-integration-abstraction.md) | External Integration Abstraction | Accepted |
| [ADR-012](../../docs/architecture/ADR-012-file-security.md) | File Security | Accepted |
| [ADR-013](../../docs/architecture/ADR-013-sow-ambiguities.md) | SOW Ambiguities | Open |

## New ADR Template

When creating a new ADR, use this format in `docs/architecture/`:

```markdown
# ADR-NNN: Title

**Status**: Proposed | Accepted | Deprecated | Superseded
**Date**: YYYY-MM-DD
**SOW Reference**: §X.Y (if applicable)

## Context
Why is this decision needed?

## Decision
What was decided?

## Consequences
What are the trade-offs?
```
