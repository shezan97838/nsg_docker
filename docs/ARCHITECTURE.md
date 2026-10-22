# NSG Mobile API — Architecture

## 1. Overview

The NSG Mobile API is built using **Clean Architecture** with a strict unidirectional dependency flow. It targets **.NET 8**, **MySQL 8.x**, **EF Core 8 Code First (Pomelo)**, and **Redis** for distributed caching.

```
┌──────────────────────┐
│     NSG.Mobile.API   │   ← HTTP concerns: Controllers, Middleware, Swagger, Program.cs
│   (ASP.NET Core 8)   │
└──────────┬───────────┘
           │
┌──────────▼───────────┐
│  NSG.Mobile.Application │  ← Business logic: Services, DTOs, Validators, Interfaces
│  (Class Library)         │
└──────────┬───────────┘
           │
┌──────────▼───────────┐
│   NSG.Mobile.Domain  │   ← Pure domain: Entities, Enums, Constants, Exceptions
│   (Class Library)    │     NO external dependencies
└──────────────────────┘

┌───────────────────────────┐
│  NSG.Mobile.Infrastructure │  ← Implementation: EF Core, MySQL 8.x, Redis, JWT,
│  (Class Library)            │    SMS, FCM, HRMS, Payroll, File Storage
└───────────────────────────┘
   References: Application + Domain
```

### Dependency Rule

| Project | May Reference | Must NOT Reference |
|---------|--------------|-------------------|
| Domain | — (nothing) | API, Application, Infrastructure, ASP.NET, EF Core |
| Application | Domain | API, Infrastructure, MySQL, Redis, HTTP clients |
| Infrastructure | Application, Domain | API |
| API | Application, Infrastructure | — |

## 2. Project Structure

```
NSG.Mobile.API.sln
src/
├── NSG.Mobile.Domain/
│   ├── Entities/          User, Device, RefreshToken, AuditLog, OtpRequest
│   ├── Enums/             UserStatus, DeviceStatus, UserRole, OtpPurpose, AuditAction, DevicePlatform
│   ├── Constants/         DomainConstants (Roles, Policies, AuditResults, TokenRevokeReasons)
│   └── Exceptions/        DomainException, EntityNotFoundException, BusinessRuleException
│
├── NSG.Mobile.Application/
│   ├── Common/            Result<T>, PaginatedResponse<T>, ApiErrorCodes, MobileNumberNormalizer
│   ├── Authentication/        (shared token/password abstractions + User/Device mappings only)
│   │   ├── Interfaces/    ITokenService, IPasswordHashingService
│   │   └── Mappings/      UserMappingExtensions, DeviceMappingExtensions
│   ├── MobileAuth/
│   │   ├── DTOs/          MobileLoginRequestDto, MobileLoginResponseDto, MobileOtp*Dto, etc.
│   │   ├── Services/      IMobileAuthService, MobileAuthService, MobileApprovalService
│   │   └── Validators/    MobileLoginRequestValidator, MobileLogoutRequestValidator, etc.
│   ├── Onboarding/
│   │   ├── DTOs/          PendingRegistrationDto, ApprovalRequestDto
│   │   └── Services/      OnboardingService, IOnboardingService
│   ├── Profile/DTOs/      UserProfileResponseDto
│   ├── Devices/DTOs/      DeviceResponseDto
│   ├── Interfaces/
│   │   ├── Repositories/  IUserRepository, IDeviceRepository, IRefreshTokenRepository, etc.
│   │   ├── Services/      IOtpService, IAuditService, ICurrentUserService
│   │   └── Infrastructure/ ICacheService, IEncryptionService, INicSmsService, etc.
│   └── DependencyInjection.cs
│
├── NSG.Mobile.Infrastructure/
│   ├── Options/           JwtOptions, SecurityOptions, RedisOptions, SmsOptions, etc.
│   ├── Persistence/
│   │   ├── AppDbContext.cs
│   │   └── Configurations/  UserConfiguration, DeviceConfiguration, etc.
│   ├── Repositories/      UserRepository, DeviceRepository, RefreshTokenRepository, etc.
│   ├── Security/          JwtTokenService, PasswordHashingService, OtpService, AesEncryptionService
│   ├── Services/          AuditService, CurrentUserService, RedisCacheService, LocalFileStorageService
│   ├── External/          NicSmsServicePlaceholder, FcmPlaceholder, HrmsPlaceholder, PayrollPlaceholder
│   └── DependencyInjection.cs
│
└── NSG.Mobile.API/
    ├── Controllers/       MobileAuthController, MobileApprovalRequestsController, OnboardingController, ProfileController, DeviceController
    ├── Middleware/         GlobalExceptionHandlingMiddleware, CorrelationIdMiddleware, SecurityHeadersMiddleware
    ├── Program.cs
    ├── appsettings.json
    └── appsettings.Development.json
```

## 3. Database Design

### Entity Relationship

```
Users 1──* Devices
users 1──* refresh_tokens
devices 1──* refresh_tokens
refresh_tokens *──1 refresh_tokens (self-ref: rotation chain)
```

### Key Tables

| Table | PK | Key Indexes | Concurrency |
|-------|-----|------------|-------------|
| Users | int Id (identity) | MobileNumber (unique), ForceNumber (unique), Status | rowversion |
| Devices | int Id (identity) | UserId+Status, UserId+DeviceIdentifierHash | — |
| refresh_tokens | int id (identity) | token_hash (unique), family_id, user_id+revoked_at_ist | — |
| audit_logs | bigint id (identity) | created_at_ist, user_id+created_at_ist, action | — (append-only) |
| OtpRequests | int Id (identity) | ReferenceId (unique), MobileNumber+Purpose+CreatedAt | — |

## 4. Configuration Architecture

All configuration uses the **Options Pattern** with strongly typed classes:

| Options Class | Config Section | Purpose |
|---------------|---------------|---------|
| JwtOptions | `Jwt` | JWT signing, issuer, audience, expiration |
| SecurityOptions | `Security` | OTP policy, lockout, password rules |
| RedisOptions | `Redis` | Distributed cache connection |
| EncryptionOptions | `Encryption` | AES-256 key |
| SmsOptions | `Sms` | NIC SMS gateway |
| FcmOptions | `Fcm` | Push notifications |
| HrmsOptions | `Hrms` | HRMS integration |
| PayrollOptions | `Payroll` | Payroll integration |
| FileStorageOptions | `FileStorage` | Secure file storage |

**Critical secrets** (JWT key, encryption key, DB password, API keys) must come from environment variables or a secure secret store — **never from source code**.

## 5. Middleware Pipeline

Order (top = first executed):

1. `CorrelationIdMiddleware` — assigns/propagates X-Correlation-Id
2. `SecurityHeadersMiddleware` — X-Content-Type-Options, X-Frame-Options, CSP, Cache-Control
3. `GlobalExceptionHandlingMiddleware` — catches all exceptions, returns the `{ success, message, data }` envelope (also writes it for bodyless 401/403/404/405/429); controller results are wrapped by `ApiResponseEnvelopeFilter`
4. HTTPS Redirection / HSTS
5. Swagger (all environments; `Swagger__Enabled=false` disables it)
6. CORS
7. Rate Limiter
8. Serilog Request Logging
9. Authentication
10. Authorization
11. Health Checks
12. Controllers

## 6. Scalability Design

Designed for **10,000+ users** with horizontal scaling support:

- **Stateless API**: No in-process session state
- **Distributed cache**: Redis for OTP rate limiting, ephemeral state
- **Database**: MySQL 8.x with connection pooling and retry (Pomelo)
- **Async I/O**: All database and HTTP calls are async with CancellationToken
- **Pagination**: All list endpoints paginated (max 100 items)
- **Projection**: AsNoTracking for read-only queries
- **Indexed queries**: All frequent access patterns indexed

## 7. Single-Tenant Architecture

This is a **single-tenant** application for NSG. See ADR-010.

- No TenantId columns
- No multi-tenant middleware
- Organizational boundaries (Office/Group) are business data, not security tenancy
- User isolation via role-based authorization and authenticated identity
