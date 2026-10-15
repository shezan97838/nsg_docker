# Definition of Done

A feature is NOT complete merely because it compiles.

## Required (all features)
- [ ] **Requirement identified** — traced to SOW section or approved change request
- [ ] **Existing code inspected** — checked for related patterns before writing new code
- [ ] **Build successful** — `dotnet build` with 0 warnings, 0 errors
- [ ] **No secrets introduced** — no credentials, keys, or connection strings in source
- [ ] **Documentation updated** — traceability, architecture docs if affected

## Required (where applicable)
- [ ] **Architecture impact reviewed** — layer boundaries, dependency direction
- [ ] **Security reviewed** — authentication, authorization, data exposure
- [ ] **Validation implemented** — FluentValidation for all request DTOs
- [ ] **DTO mapping implemented** — explicit extension methods, no sensitive field exposure
- [ ] **Database impact reviewed** — entity configs, indexes, migrations, data safety
- [ ] **API validation performed** — endpoints tested via Swagger or equivalent
- [ ] **Logging reviewed** — no sensitive data logged, appropriate log levels
- [ ] **Assumptions documented** — any interpretation recorded as open question

## Not Currently Required (future phase)
- Automated unit tests passing
- Automated integration tests passing
- Load/performance testing

# Code Review Checklist

## Architecture
- [ ] Correct layer for the change
- [ ] No circular dependencies introduced
- [ ] Interfaces in Application layer, implementations in Infrastructure
- [ ] Domain has no new external dependencies

## Security
- [ ] No secrets in source code
- [ ] Auth/authorization not bypassed
- [ ] User identity from JWT claims (not request body)
- [ ] Sensitive data excluded from DTOs, logs, errors
- [ ] Input validation present

## Code Quality
- [ ] Follows existing patterns and conventions
- [ ] CancellationToken on async methods
- [ ] Result<T> for operation outcomes
- [ ] XML documentation on public members
- [ ] No dead code or commented-out blocks

## Database
- [ ] Explicit EF configuration
- [ ] Appropriate indexes
- [ ] AsNoTracking for read-only queries
- [ ] No unbounded queries

# Security Review Checklist

- [ ] Authentication not weakened or bypassed
- [ ] Authorization policies correctly applied
- [ ] No sensitive data in DTOs, logs, errors, or audit
- [ ] Crypto uses approved algorithms
- [ ] Rate limiting on sensitive endpoints
- [ ] Input validation prevents injection
- [ ] File operations prevent path traversal
- [ ] No secrets in source code
- [ ] Token handling follows rotation/reuse detection patterns

# API Review Checklist

- [ ] Route follows `api/v1/{resource}` convention
- [ ] HTTP method matches operation semantics
- [ ] `[Authorize]` or `[AllowAnonymous]` explicitly set
- [ ] Error responses use ProblemDetails
- [ ] Controller delegates to service (thin controller)
- [ ] Response uses DTO (never entity)
- [ ] ProducesResponseType attributes accurate
- [ ] Pagination for list endpoints

# Release Checklist

- [ ] `dotnet build` — 0 warnings, 0 errors
- [ ] All security controls intact
- [ ] No dev secrets in production config
- [ ] Database migrations reviewed and tested
- [ ] Health checks passing
- [ ] CORS restricted for production
- [ ] NIC SMS placeholder replaced (or disabled)
- [ ] Documentation reflects current state
- [ ] Open questions reviewed
