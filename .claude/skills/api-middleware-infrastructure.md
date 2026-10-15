# API, Middleware & Infrastructure Skills

## API Conventions
- Base route: `api/v1/{resource}`
- Auth endpoints: `api/v1/mobile-auth/*` (AllowAnonymous)
- Protected endpoints: `[Authorize]` with specific policies
- Error responses: ProblemDetails (RFC 7807)
- Client IP: `X-Forwarded-For` header or `RemoteIpAddress`

## Middleware Pipeline (order matters)
1. CorrelationIdMiddleware → X-Correlation-Id
2. SecurityHeadersMiddleware → security response headers
3. GlobalExceptionHandlingMiddleware → ProblemDetails
4. HSTS / HTTPS Redirection
5. Swagger (dev only)
6. CORS
7. RateLimiter
8. SerilogRequestLogging
9. Authentication → Authorization
10. HealthChecks → Controllers

## Rate Limiting
| Policy | Limit |
|--------|-------|
| Global | 100/min per IP |
| `auth` | 10/min per IP |
| `otp` | 5/5min per IP |

## Redis Cache
- Interface: `ICacheService` in Application layer
- Implementation: `RedisCacheService` wrapping `IDistributedCache`
- Fallback: in-memory distributed cache when Redis not configured
- Config: `RedisOptions` in `Redis` section

## File Storage
- Interface: `IFileStorageService` in Application layer
- Implementation: `LocalFileStorageService`
- Path traversal protection via `Path.GetFullPath` validation
- Server-generated file names (GUID-based)
- Config: `FileStorageOptions` in `FileStorage` section

## Audit Logging
- Interface: `IAuditService` in Application layer
- Implementation: `AuditService` — swallows exceptions (never breaks primary ops)
- Fields: Action, UserId, EntityType, EntityId, Result, IpAddress, DeviceId, CorrelationId
- Never log: passwords, OTPs, tokens, encryption keys

## External Service Placeholders
All external integrations use placeholder implementations:
- `NicSmsServicePlaceholder` — logs OTP (dev only, MUST disable in prod)
- `FcmPushNotificationServicePlaceholder` — logs notification
- `HrmsServicePlaceholder` — returns "found"
- `PayrollServicePlaceholder` — empty (TBD)

## Health Checks
- `/health/live` — self check (always healthy)
- `/health/ready` — MySQL connectivity
