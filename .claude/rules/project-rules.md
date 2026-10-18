# Architecture & Coding Rules

## Architecture Rules
- Domain layer has ZERO NuGet/external dependencies
- Application references only Domain
- Infrastructure references Application + Domain
- API references Application + Infrastructure
- No circular dependencies between any projects
- All cross-layer communication via interfaces defined in Application
- Configuration via Options pattern only (never `IConfiguration` directly in services)
- Single DbContext (`AppDbContext`) — no secondary contexts without ADR

## Coding Standards
- C# 12 / .NET 8 target
- Nullable reference types enabled
- `async/await` with `CancellationToken` on all async methods
- `Result<T>` monad for operation outcomes (not exceptions for flow control)
- Domain exceptions only for true invariant violations
- XML documentation on all public interfaces and DTOs
- No `var` for non-obvious types in complex logic
- No magic strings — use `DomainConstants` or `ApiErrorCodes`

## API Rules
- Route pattern: `api/v1/{resource}`
- All endpoints explicitly `[Authorize]` or `[AllowAnonymous]`
- Error responses: `ProblemDetails` (RFC 7807)
- Controllers are thin — delegate to services
- No entity objects in API responses — DTOs only
- `ProducesResponseType` attributes on all actions
- Pagination required for all list endpoints
- No blank responses: always a message when there is no data or after a change — see "API Response Rules"

## Database Rules
- Explicit `IEntityTypeConfiguration<T>` for every entity
- No convention-only configuration
- Column lengths always specified
- Delete behavior: `Restrict` by default
- Enums stored as `int` (except AuditAction as `string`)
- Indexes for actual query patterns (not speculative)
- `AsNoTracking()` for read-only queries
- No lazy loading — explicit Include only
- Review migrations before applying. Startup auto-migration is controlled by `Database:MigrateOnStartup` in `appsettings.json` (currently `true`, see OQ-034)
- Table, column, index and constraint names: lowercase `snake_case` — see "Database Standards" and `docs/DATABASE-STANDARDS.md`

## Security Rules
- Secrets from configuration only — never source code
- JWT algorithm explicitly locked to HMAC-SHA256
- Passwords: PBKDF2-SHA256, 100K iterations minimum
- OTP: hashed, purpose-isolated, attempt-limited, time-limited
- User identity from JWT claims only — never request body
- Sensitive data excluded from: DTOs, logs, errors, audit
- Rate limiting on all public endpoints
- Path traversal prevention on all file operations
- Security headers on all responses

## Authentication Rules
- Phone number is login identifier (normalized to +91XXXXXXXXXX)
- Single device binding per user
- Refresh token rotation with family-based reuse detection
- Failed login tracking with configurable lockout
- Account state machine: PendingOtpVerification → PendingITApproval → Active

## Authorization Rules
- Policy-based authorization (not inline role checks)
- Policies defined in `Program.cs`, constants in `DomainConstants.Policies`
- IT Cell operations require `RequireITAdmin` policy
- Profile endpoints use authenticated user ID (IDOR protection)
- No endpoint without explicit auth attribute

## Logging Rules
- Serilog with structured logging
- Correlation ID on every log entry
- NEVER log: passwords, OTP values, JWT tokens, refresh tokens, encryption keys
- Log level: Info for operations, Warning for anomalies, Error for failures
- Audit logging via `IAuditService` for security events

## Git Rules
- Never commit: secrets, connection strings, API keys, encryption keys
- `.gitignore` covers: bin/, obj/, logs/, .vs/, appsettings.Production.json
- No git repository initialized yet (future task)

## Dependency Rules
- No new NuGet packages without justification
- Domain project: zero NuGet dependencies
- Prefer Microsoft-maintained packages
- Pin major versions to avoid breaking changes
- Review transitive dependencies for security

## Documentation Rules
- Documentation reflects current code (not aspirational)
- ADR for significant architectural decisions
- Open questions documented (never silently resolved)
- Requirement traceability updated when features added
- No claims of features or tests that don't exist

## Database Standards (MySQL first, Linux-safe)
Full rules, rationale and checklist: **`docs/DATABASE-STANDARDS.md`** (authoritative). Evidence for the current schema: `docs/DATABASE-SCHEMA-AUDIT.md`. Summary:
- The MySQL schema is the source of truth. Windows is for development, Ubuntu Linux for deployment; a green Windows build does not prove Linux compatibility.
- **Tables and columns: lowercase `snake_case`** (`a-z`, digits, `_`; start with a letter; at most 64 characters; never a MySQL reserved word such as `rank`). No exceptions for new or existing objects except EF's own `__EFMigrationsHistory`.
- Every table is mapped explicitly with `ToTable("...")`; columns, keys, indexes and foreign keys are named by `DatabaseNamingStandard` (`pk_`, `ix_`, `fk_`, `ck_`). C# names stay PascalCase; JSON and routes are public contracts and are not renamed to match the database.
- Every `DateTime` is an IST (Asia/Kolkata, UTC+05:30) instant stored as `datetime(6)` in a `*_ist` column and written with `IndianStandardTime.Now` (never `DateTime.UtcNow`/`DateTime.Now`); the API exposes it with the fixed `+05:30` offset (`IstTime.FromIst`). See "Timestamp Convention" in `CLAUDE.md`. Foreign key columns match the referenced key type exactly. Foreign keys use `Restrict`.
- Migrations: review the generated SQL; never edit or rename one applied to a shared database (use a corrective migration); never run at startup; test renames on a copy of real data; ask for approval before applying to development or production.
- Raw SQL only in tests/tools, parameterised, lowercase identifiers. `tests/NSG.Mobile.Tests/ExpectedDatabaseSchema.cs` pins the whole schema and `DatabaseNamingRuleTests` enforces the rules; update the pinned schema in the same change as any schema change.

## API Response Rules (no blank responses)
Adopted 2026-10-14. **Status: rule adopted; endpoints written before this date do not comply yet (see "Known deviations").**

- **No API may return a blank response:** no empty body, no HTTP 204 and no HTTP 200 without content, no `null`, and no empty array or empty collection (`[]`, `"items": []`) as the answer.
- **Data found:** return the data.
- **No data found** (a valid request or search that matches nothing): HTTP 200 with exactly one message, for example `{ "message": "No NSG trades found." }`. Never an empty list.
- **Create, update, deactivate/delete:** return HTTP 201 (create) or 200 with a message and the affected resource where one exists: `{ "message": "NSG trade updated successfully.", "data": { ... } }`. Deactivation returns `{ "message": "NSG trade deactivated successfully." }` (not 204). A repeated, idempotent request still answers with a message ("NSG trade is already inactive.").
- **A single record that does not exist** and **every error** (400, 401, 403, 404, 409, 500) carry a human-readable message in the `ProblemDetails` `detail`.
- **Messages:** plain, resource-specific English in the form "`<Resource> <created|updated|deactivated> successfully.`" or "`No <resources> found.`". They must never contain stack traces, SQL, internal identifiers of other records, or personal data.
- **Exempt:** `/health/*` and Swagger endpoints.
- **Implementation:** one shared response type in the Application/API layers (`ApiMessageResponse` and `ApiResult<T>`) used by every controller; services return the message with the `Result`. Tests for every endpoint must assert the message for the empty-list and the mutation cases.
- **Known deviations (to be migrated):** every paged list returns `items: []` when empty; every `DELETE` returns 204; `POST` and `PUT` return the bare resource without a message; `GET /api/v1/ranks/dropdown` and the other dropdowns return `[]` when empty.
