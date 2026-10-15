# Known Decisions

Decisions made during foundation implementation. See `docs/architecture/ADR-*` for full ADRs.

| ID | Decision | Rationale |
|----|----------|-----------|
| ADR-001 | Clean Architecture (4 projects) | Testability, maintainability, VAPT readiness |
| ADR-002 | MySQL 8.x + EF Core 8 (Pomelo) | Strategic pivot to MySQL 8.x for Linux/container deployment |
| ADR-003 | JWT access tokens (HMAC-SHA256) | SOW §3.8, stateless auth for scaling |
| ADR-004 | Refresh token rotation with reuse detection | Protect against token theft |
| ADR-005 | Phone number as login identifier | SOW §3.7 |
| ADR-006 | Hashed device identifier (not raw IMEI) | IMEI restricted on modern OS |
| ADR-007 | Biometric on-device only (no raw data to server) | Privacy, security best practice |
| ADR-008 | Redis distributed cache (optional fallback) | SOW §4 |
| ADR-009 | Append-only audit logging | SOW §3.8 |
| ADR-010 | Single-tenant architecture | SOW scope is NSG only |
| ADR-011 | External integration abstraction with placeholders | API contracts not in SOW |
| ADR-012 | File storage outside web root | Prevent direct URL access |
| ADR-013 | SOW ambiguities documented | Never silently resolved |

## Implementation Decisions (not ADR-level)
- `Result<T>` monad for service return types (not exceptions for flow control)
- Explicit DTO mapping via extension methods (no AutoMapper)
- Focused repositories (not generic CRUD)
- PBKDF2-SHA256 for passwords (100K iterations)
- SHA-256 for OTP and token hashing
- AuditService swallows exceptions (never breaks primary operations)
- ProblemDetails for all API error responses
