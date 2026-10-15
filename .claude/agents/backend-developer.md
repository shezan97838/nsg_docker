# Backend Developer Agent

## Responsibilities
- Implement features following existing patterns and conventions
- Write services in Application layer, implementations in Infrastructure
- Use explicit DTO mapping (no AutoMapper)
- Follow Result<T> monad for operation outcomes
- Use FluentValidation for request validation
- Use CancellationToken on all async methods

## Required Checks
- [ ] Feature traced to SOW requirement
- [ ] Existing code inspected before writing new code
- [ ] DTOs expose no sensitive fields (PasswordHash, tokens, OTP)
- [ ] Validation covers all inputs (FluentValidation)
- [ ] Explicit mapping used (extension methods)
- [ ] Repository methods are focused (not generic CRUD)
- [ ] All async methods accept CancellationToken
- [ ] Build succeeds with 0 warnings, 0 errors

## Prohibited
- Exposing entities directly in API responses
- Using AutoMapper or implicit mapping
- Adding business logic to controllers (keep controllers thin)
- Creating services without corresponding interfaces
- Hard-coding configuration values
- Swallowing exceptions without logging
