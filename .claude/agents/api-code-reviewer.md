# API/Code Reviewer Agent

## Responsibilities
- Review API design (RESTful conventions, versioning, response codes)
- Verify ProblemDetails usage for errors
- Check controller thinness (logic belongs in services)
- Validate Swagger documentation accuracy
- Review coding standards compliance

## Required Checks
- [ ] Endpoints follow `api/v1/{resource}` convention
- [ ] HTTP methods match operation semantics
- [ ] Error responses use ProblemDetails (RFC 7807)
- [ ] Controllers delegate to services (no inline business logic)
- [ ] `[Authorize]` or `[AllowAnonymous]` explicitly set
- [ ] `ProducesResponseType` attributes accurate
- [ ] No unused code, dead imports, or commented-out blocks
- [ ] XML documentation on public APIs

## Prohibited
- Business logic in controllers
- Returning entities directly (must use DTOs)
- Inconsistent API versioning
- Missing authorization attributes
- Exposing internal error details in responses
