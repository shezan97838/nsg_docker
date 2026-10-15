# Security Documentation

## Security Baseline (SOW §3.8)

The NSG Mobile API implements defense-in-depth security aligned with:
- SOW §3.8 security requirements
- OWASP API Security Top 10
- CERT-In VAPT readiness

## Threat Model

| Threat | Mitigation | Implementation |
|--------|-----------|----------------|
| Credential stuffing | Rate limiting, account lockout | `RateLimiter` (10/min auth), `SecurityOptions.MaxFailedLoginAttempts` |
| Token theft | Short-lived JWT (15min), refresh rotation | `JwtTokenService`, `RefreshTokenRepository.RevokeFamilyAsync` |
| Refresh token replay | Family-based reuse detection | `MobileAuthService.RefreshTokenAsync` |
| Session hijacking | Device binding, IP tracking | `Device` entity, `RefreshToken.CreatedByIp` |
| IDOR | User ID from JWT claims only | `CurrentUserService`, `ProfileController` |
| Brute force OTP | Attempt limits, expiry, cooldown | `OtpService`, `SecurityOptions` |
| OTP cross-purpose reuse | Purpose isolation enum | `OtpPurpose` enum on `OtpRequest` |
| Unauthorized device | Single device binding, revocation | `DeviceRepository.RevokeAllUserDevicesAsync` |
| Injection | EF Core parameterized queries | All repository queries |
| Path traversal | `Path.GetFullPath` validation | `LocalFileStorageService` |
| Sensitive data exposure | DTO projection, no PII in logs | `MappingExtensions`, audit service |
| Missing auth | Explicit `[Authorize]`/`[AllowAnonymous]` | All controllers |

## Authentication Architecture

### Flow: Registration (SOW §3.7)
```
User → POST /mobile-auth/registration/submit → Validate → Create User(PendingOtp) → Send OTP → Return ReferenceId
User → POST /mobile-auth/otp/verify → Verify Hash → Update User(PendingITApproval)
IT Admin → POST /onboarding/approve/{id} → Update User(Active) → Audit
```

### Flow: Login (SOW §3.8)
```
User → POST /mobile-auth/login → Verify Password → Check Device Binding → Issue JWT + Refresh → Audit
User → POST /mobile-auth/token/refresh → Verify Hash → Check Reuse → Rotate → Issue New Pair
User → POST /mobile-auth/logout → Revoke Refresh Token → Audit
```

### User State Machine
```
PendingOtpVerification → PendingITApproval → Active → Locked (auto-unlock) → Active
                                             Active → Suspended (admin action)
```

## JWT Security
- Algorithm: HMAC-SHA256 (explicitly locked, rejects others)
- Validation: issuer, audience, lifetime, signing key, algorithm
- Claims: `sub`(userId), `jti`, `iat`, `role`, `device_id`, `force_number`
- Excluded from claims: password, OTP, mobile number, salary data
- ClockSkew: 30 seconds (reduced from default 5 minutes)

## Refresh Token Security
- 64-byte cryptographically random (`RandomNumberGenerator`)
- Stored as SHA-256 hash (never plaintext)
- Family tracking: all tokens in rotation chain share `FamilyId`
- Reuse detection: revoked token presented → entire family revoked + audit alert
- Device-bound: verified on every refresh

## OTP Security
- 6-digit cryptographically random (`RandomNumberGenerator.GetInt32`)
- SHA-256 hashed before database storage
- Purpose-isolated: Registration OTP cannot verify Login
- Max 5 verification attempts per OTP
- Expires after 5 minutes (configurable)
- Resend cooldown: 60 seconds (configurable)
- Never logged in production (dev placeholder logs with WARNING)

## Device Binding
- One active device per user
- Device identifier: platform-safe hash (not raw IMEI — restricted on modern OS)
- New device login → revokes previous device + all its refresh tokens
- Optional public key binding for future cryptographic challenges

## Data Protection
- AES-256-CBC encryption for sensitive fields at rest
- Random IV per encryption operation
- Key from `EncryptionOptions.Key` (Base64 32-byte key from secure config)
- TLS 1.2+ required in production (`RequireHttpsAttribute`)
- HSTS enabled with 365-day max-age

## File Security
- Stored outside web root
- Server-generated file names (GUID-based)
- Extension whitelist: `.pdf`, `.jpg`, `.jpeg`, `.png`, `.xml`
- Max file size: 10MB (configurable)
- Path traversal prevention via `Path.GetFullPath` validation
- Authorization checked before every download

## API Security
- Rate limiting: Global (100/min), Auth (10/min), OTP (5/5min)
- CORS configured (TODO: restrict origins for production)
- Security headers: X-Content-Type-Options, X-Frame-Options, CSP, Cache-Control, Referrer-Policy
- Server header removed
- ProblemDetails for all errors (no stack traces in production)
- Correlation ID for request tracing

## Audit Logging
- Append-only `audit_logs` table (no update/delete)
- Events: login, logout, OTP, registration, approval, device, token refresh/reuse, lockout
- Fields: Action, UserId, EntityType, EntityId, Result, IpAddress, DeviceId, CorrelationId
- Never contains: passwords, OTP values, tokens, encryption keys, salary data
- Service swallows exceptions (audit failure never breaks primary operations)

## Secrets Management
- All secrets via `appsettings.json` / environment variables / secure store
- Development config has placeholder values
- `appsettings.Production.json` in `.gitignore`
- No secrets with real defaults in Options classes
- JWT secret: `[Required]`, fails fast if missing

## VAPT Readiness
| CERT-In Requirement | Status |
|---------------------|--------|
| HTTPS/TLS 1.2+ | ✅ RequireHttps, HSTS |
| MFA | ✅ Password + OTP + Device binding |
| Session management | ✅ JWT + refresh rotation |
| Input validation | ✅ FluentValidation |
| Output encoding | ✅ JSON serialization |
| Audit trail | ✅ Comprehensive AuditLog |
| Encryption at rest | ✅ AES-256-CBC |
| Access control | ✅ Policy-based authorization |
| Error handling | ✅ ProblemDetails, no stack traces |
| Rate limiting | ✅ Per-IP rate limiting |
