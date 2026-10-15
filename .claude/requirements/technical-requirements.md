# Technical Requirements (from SOW §4)

| Requirement | SOW Spec | Implementation | Status |
|-------------|----------|---------------|--------|
| Framework | ASP.NET Core Web API / .NET 8 | `net8.0` target | ✅ |
| ORM | Entity Framework Core (Code First) | EF Core 8, explicit configs | ✅ |
| Database | MySQL 8.x on NIC/NSG infra | Pomelo MySQL provider, connection resilience | ✅ |
| Auth | JWT + OAuth 2.0 | JWT Bearer (HMAC-SHA256). Full OAuth TBD (OQ-002) | ✅ Partial |
| Caching | Redis or equivalent | Redis via IDistributedCache, in-memory fallback | ✅ |
| Push notifications | FCM (Android) + APNs (iOS) | Placeholder implementation | Stub |
| SMS | NIC-approved SMS gateway | Placeholder implementation | Stub |
| Logging | Structured logging | Serilog with correlation ID | ✅ |
| API docs | Swagger/OpenAPI | SwaggerGen with JWT auth | ✅ |
| Health checks | Readiness + liveness | `/health/ready` (MySQL), `/health/live` (self) | ✅ |
| HTTPS | TLS 1.2+ | RequireHttpsAttribute, HSTS | ✅ |
| CORS | Mobile app origins | Configured (TODO: restrict for production) | ⚠️ |
| Rate limiting | API abuse prevention | Global + auth + OTP policies | ✅ |

# Non-Functional Requirements

| Requirement | Target | Implementation | Status |
|-------------|--------|---------------|--------|
| Users | 10,000+ | Pagination, async, indexes, Redis | ✅ Architecture |
| Availability | Production-grade | Health checks, retry, connection resilience | ✅ |
| Security | CERT-In VAPT ready | OWASP API alignment, defense-in-depth | ✅ |
| Scalability | Horizontal | Stateless API, distributed cache | ✅ Architecture |
| Auditability | Comprehensive | AuditLog with correlation ID | ✅ |
| Maintainability | Clean Architecture | 4-layer separation, DI, interfaces | ✅ |

# Security Requirements (SOW §3.8)

| Requirement | Implementation | Status |
|-------------|---------------|--------|
| MFA: Password + OTP + Biometric | MobileAuthService, OtpService | ✅ |
| JWT-based authentication | JwtTokenService (HMAC-SHA256) | ✅ |
| Configurable session timeout | JwtOptions.AccessTokenExpirationMinutes | ✅ |
| Single device binding | DeviceRepository, login flow | ✅ |
| IT Cell approval before activation | OnboardingService | ✅ |
| Secure logout | Refresh token revocation | ✅ |
| HTTPS with TLS 1.2+ | RequireHttps, HSTS | ✅ |
| AES-256 encryption at rest | AesEncryptionService | ✅ |
| No plaintext sensitive data on device | Mobile app concern | N/A |
| Comprehensive audit logging | AuditService, AuditLog entity | ✅ |
| CERT-In VAPT compliance | OWASP alignment | ✅ Architecture |
