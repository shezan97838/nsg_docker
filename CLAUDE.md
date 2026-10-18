# CLAUDE.md — NSG Mobile API

> Primary AI development instruction file. All AI sessions must read this first.

## Project

**NSG Mobile Application API** — ASP.NET Core 8 Web API for the National Security Guard.
Single-tenant, production-grade, serving 10,000+ users (Android + iOS).

**SOW**: Version 2.0, August 2025, IT Cell, NSG.

## Source of Truth (Priority Order)

1. NSG SOW / approved client requirements
2. Approved ADRs (`docs/architecture/`)
3. Approved security/VAPT requirements
4. Approved technical/integration specifications
5. Existing implementation in `src/`
6. General enterprise best practices

**If information is missing or ambiguous, document it — do NOT invent it.**

## Architecture

Clean Architecture with strict dependency direction:

```
API → Application → Domain
Infrastructure → Application + Domain
```

| Layer | Project | Dependencies |
|-------|---------|-------------|
| Domain | `NSG.Mobile.Domain` | None (pure C#) |
| Application | `NSG.Mobile.Application` | Domain only |
| Infrastructure | `NSG.Mobile.Infrastructure` | Application, Domain |
| API | `NSG.Mobile.API` | Application, Infrastructure |

**Domain MUST have zero external/NuGet dependencies.**

## Technology Stack

- .NET 8 / ASP.NET Core 8
- Entity Framework Core 8 (Code First, MySQL 8.x via Pomelo provider)
- FluentValidation
- Serilog (structured logging)
- JWT Bearer authentication (HMAC-SHA256)
- Redis distributed cache (optional, in-memory fallback)
- Swagger/OpenAPI

## Security-First Development

Every change must preserve these controls:

- JWT access tokens (short-lived, minimal claims, algorithm-locked)
- Refresh token rotation with family-based reuse detection
- PBKDF2-SHA256 password hashing (100K iterations)
- OTP hash storage with purpose isolation, attempt limits, cooldown
- Single device binding per user (hashed identifier)
- AES-256-CBC field encryption (key from config, never code)
- Rate limiting (global + per-endpoint policies)
- Security response headers (CSP, HSTS, X-Frame-Options)
- RFC 7807 ProblemDetails error responses
- Correlation ID tracing
- Comprehensive audit logging (no sensitive data in logs)

### Prohibited Shortcuts

- Disabling authentication/authorization
- Plaintext passwords, OTPs, or tokens
- Logging tokens, secrets, or sensitive PII
- Bypassing TLS validation
- Exposing internal storage paths
- Trusting client-supplied UserId/role/device values
- Committing credentials or secrets
- Disabling security controls to make code work

## Development Workflow

**Never jump directly from request to code.** Follow:

```
UNDERSTAND → VERIFY REQUIREMENT → INSPECT EXISTING CODE → IMPACT ANALYSIS
→ PLAN → IMPLEMENT → VALIDATE → SECURITY REVIEW → DOCUMENT → FINAL SELF-REVIEW
```

## Layer Rules

| Rule | Enforcement |
|------|-------------|
| Domain has no NuGet references | Domain .csproj |
| Application references only Domain | Application .csproj |
| No `DbContext` in Application layer | Code review |
| No HTTP/ASP.NET concepts in Application | Code review |
| Explicit DTO mapping only (no AutoMapper) | MappingExtensions.cs |
| Focused repositories (no generic CRUD) | Interface design |
| All config via Options pattern | ConfigurationOptions.cs |
| Secrets from config, never source code | appsettings.json |
| MySQL-first, Linux-safe database names (lowercase snake_case, explicit mappings, reviewed migrations) | `docs/DATABASE-STANDARDS.md`, `DatabaseNamingRuleTests` |

## Timestamp Convention (IST is authoritative)

**Every `DateTime` in this project is an Indian Standard Time (IST, Asia/Kolkata, UTC+05:30, no DST) instant.** This is the single rule; any older document, comment or template that says UTC is superseded by it. Authoritative detail: `docs/DATABASE-STANDARDS.md`.

- **Database:** `datetime(6)` columns whose name ends in `_ist` (`created_at_ist`, `expires_at_ist`). `DatabaseNamingStandard` appends `_ist` and `DatabaseNamingRuleTests` fails on any `DateTime` column that does not comply. No `*_utc` columns, no `DateTimeOffset`/`timestamp` columns. C# properties end in `Ist` (`CreatedAtIst`).
- **Creation and updates:** write `IndianStandardTime.Now` (`NSG.Mobile.Domain.Common`). Never `DateTime.Now` (host-local time) and never `DateTime.UtcNow` for a stored value.
- **Comparisons and expiry:** compare a stored value only with another IST value (`IndianStandardTime.Now`); never mix IST and UTC in one comparison. Convert explicitly with `IndianStandardTime.ConvertFromUtc` / `ConvertToUtc` when a library hands you UTC (for example JWT `exp`, which is a Unix-epoch value and is not stored). Nothing may depend on the server's local timezone or on MySQL's session `time_zone`.
- **API serialization:** responses expose stored IST instants as `DateTimeOffset` with the fixed `+05:30` offset via `IstTime.FromIst`; requests carrying an offset are converted to IST before storage. Never return an unmarked or UTC (`Z`) value for a stored timestamp.
- **Dates without time** (`date_of_birth` etc.) are `DateOnly` / `date`: no timezone, no conversion.

## Testing

**Current state: No dedicated test projects exist.**

- Require `dotnet build` success (0 warnings, 0 errors)
- API/manual validation via Swagger
- Security review for auth/crypto changes
- Regression analysis for database changes
- Architecture testable for future unit/integration tests

## Documentation

- Update `docs/` when architecture changes
- Update `docs/REQUIREMENT-TRACEABILITY.md` when adding features
- Record new architectural decisions as ADRs
- Document open questions, never silently resolve ambiguities

## Governance

See `.claude/` for detailed governance:
- `.claude/agents/` — Role-specific instructions
- `.claude/skills/` — Technology-specific guidance
- `.claude/workflows/` — Development process workflows
- `.claude/rules/` — Coding and architecture rules
- `.claude/security/` — Security baseline and controls
- `.claude/memory/` — Project context and decisions
- `.claude/requirements/` — Requirement documentation
- `.claude/architecture/` — Architecture documentation
- `.claude/quality/` — Review checklists and DoD
- `.claude/templates/` — Templates for common artifacts
