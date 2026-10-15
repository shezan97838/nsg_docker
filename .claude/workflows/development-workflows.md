# Development Workflows

## New Feature Workflow

1. **Requirement** — Identify SOW section. If not in SOW, stop and document.
2. **Inspect** — Review existing code for related patterns, interfaces, entities.
3. **Impact Analysis** — Identify affected layers, entities, DTOs, configs, migrations.
4. **Plan** — Define: entities, interfaces, DTOs, validators, mappings, configs, endpoints.
5. **Implement** — Domain → Application → Infrastructure → API (dependency order).
6. **Validate** — `dotnet build` (0 warnings/errors), manual API validation via Swagger.
7. **Security Review** — Auth, authorization, data exposure, input validation, logging.
8. **Documentation** — Update traceability matrix, ADRs if needed, open questions.
9. **Final Review** — Self-review against Definition of Done.

## Bug Fix Workflow

1. **Reproduce** — Understand the exact failure condition.
2. **Inspect** — Read relevant code, identify root cause.
3. **Impact** — Assess regression risk and related functionality.
4. **Fix** — Minimal targeted change to address root cause.
5. **Validate** — Build succeeds, fix verified, no regressions introduced.
6. **Security** — If auth/crypto related, full security review.
7. **Document** — Update docs if behavior changes.

## Database Change Workflow

1. **Requirement** — What data change is needed and why.
2. **Design** — Entity changes, EF configuration, index strategy.
3. **Impact** — Existing data, FK constraints, migration safety.
4. **Implement** — Entity → Configuration → Repository (if interface changes).
5. **Migration** — Create EF migration, review SQL, verify reversibility.
6. **Validate** — Build succeeds, migration applies cleanly.
7. **Document** — Update database architecture docs.

## API Change Workflow

1. **Requirement** — What endpoint change is needed.
2. **Design** — Route, method, request/response DTOs, auth, rate limiting.
3. **Impact** — Breaking changes? API versioning needed?
4. **Implement** — DTO → Validator → Service → Controller.
5. **Validate** — Swagger accurate, manual testing, response codes correct.
6. **Security** — Authorization enforced, no data leakage, ProblemDetails on errors.
7. **Document** — Update API endpoint documentation.

## Security Fix Workflow

1. **Assess** — Severity, attack surface, current exposure.
2. **Inspect** — Read all related security code.
3. **Fix** — Targeted fix preserving defense-in-depth.
4. **Security Review** — Full review of fix and surrounding code.
5. **Validate** — Build succeeds, fix verified, no security regressions.
6. **Document** — Update security docs, record in known-decisions if significant.

## Refactoring Workflow

1. **Justification** — Why refactor? Technical debt, readability, pattern alignment?
2. **Scope** — Clearly bound the refactoring scope.
3. **Impact** — What could break?
4. **Implement** — Refactor with no behavior changes.
5. **Validate** — Build succeeds, existing behavior preserved.
6. **Review** — Architecture review if structural change.

## Code Review Workflow

Apply checklists from `.claude/quality/`:
1. Architecture compliance
2. Security review
3. API review
4. Code standards
5. Definition of Done

## Release Workflow

1. Build verification (`dotnet build` — 0 warnings, 0 errors)
2. Security review of changes since last release
3. Database migration review (if any)
4. Configuration review (no dev values in production config)
5. Secrets verification (none in source)
6. Documentation current
7. Release checklist completed

## Production Incident Workflow

1. **Assess** — Impact, affected users, security implications.
2. **Investigate** — Logs (CorrelationId), audit trail, recent changes.
3. **Mitigate** — Immediate action to reduce impact.
4. **Root Cause** — Identify underlying issue.
5. **Fix** — Follow Bug Fix or Security Fix workflow.
6. **Document** — Record incident, root cause, fix, prevention measures.
