# NSG Mobile API — Security Architecture

## Authentication Flow

### Registration (SOW §3.7 Step 1)
1. User submits: Name, Rank, Force Number, Mobile Number
2. Server validates, normalizes mobile number, checks duplicates
3. Creates user in `PendingOtpVerification` status
4. Generates cryptographically secure 6-digit OTP
5. Stores OTP as SHA-256 hash (never plaintext)
6. Sends OTP via NIC SMS gateway
7. Returns OTP reference ID (not the OTP itself)

### OTP Verification (SOW §3.7 Step 1)
- Max 5 verification attempts per OTP
- OTP expires after 5 minutes (configurable)
- Resend cooldown: 60 seconds (configurable)
- Purpose isolation: Registration OTP cannot be used for Login
- Successful verification transitions user to `PendingITApproval`

### IT Approval (SOW §3.7 Step 2)
- IT Cell admin reviews pending registrations
- Can verify against HRMS records
- Approval activates account → `Active` status
- All approval/rejection actions are audited

### Login (SOW §3.8)
- Password verified using PBKDF2-SHA256 (100K iterations)
- Constant-time hash comparison prevents timing attacks
- Failed login tracking with configurable lockout (5 attempts → 30-min lock)
- Device binding enforced (single device per user)
- New device → revokes previous device and all its tokens
- Issues: JWT access token (15 min) + opaque refresh token (7 days)

## Token Security

### JWT Access Tokens
- Algorithm: HMAC-SHA256 (explicitly validated, rejects others)
- Claims: `sub`, `jti`, `iat`, `exp`, `role`, `device_id`, `force_number`
- NO sensitive PII in tokens (no salary, no password, no OTP)
- Short-lived: 15 minutes (configurable)
- Validated: issuer, audience, signature, expiration, not-before, algorithm

### Refresh Token Rotation
- Opaque cryptographically random tokens (64 bytes)
- Stored as SHA-256 hash (never plaintext)
- Family-based tracking for reuse detection
- On rotation: old token revoked, new token issued (same family)
- **Reuse Detection**: If a revoked token is used, entire family is revoked
- Device binding verified on every refresh

## Device Binding (SOW §3.8)
- One active device per user
- Device identifier hashed before storage
- Optional cryptographic key pair binding (public key stored server-side)
- Device revocation invalidates all refresh tokens
- All device changes audited

## Password Security
- PBKDF2-SHA256 with 100,000 iterations
- 16-byte random salt per password
- Self-contained hash format: `{iterations}.{salt_base64}.{hash_base64}`
- Constant-time comparison (CryptographicOperations.FixedTimeEquals)
- Passwords never appear in: logs, API responses, audit logs, exceptions

## Encryption
- AES-256-CBC for application-level field encryption
- Random IV per encryption operation
- Key from secure configuration (never source code)

## Rate Limiting
- Global: 100 requests/minute per IP
- Auth endpoints: 10 requests/minute per IP
- OTP endpoints: 5 requests/5 minutes per IP

## Authorization
- Policy-based (not just role-based)
- ITAdmin policy: required for onboarding management
- User identity derived from JWT claims (never from request body)
- IDOR protection: profile endpoints use authenticated user ID

## Audit Logging (SOW §3.8)
Audited events include:
- Login success/failure
- OTP request/verify/fail
- Registration submission
- IT approval/rejection
- Device registration/revocation
- Token refresh/reuse detection
- Logout
- Account lock/unlock

Audit logs are append-only and never contain sensitive data.

## Security Headers
- `X-Content-Type-Options: nosniff`
- `X-Frame-Options: DENY`
- `Referrer-Policy: strict-origin-when-cross-origin`
- `Content-Security-Policy: default-src 'none'`
- `Cache-Control: no-store`
- Server header removed

## OWASP API Security Alignment
| Risk | Mitigation |
|------|-----------|
| Broken Object Level Authorization | User ID from JWT, not request body |
| Broken Authentication | MFA, token rotation, reuse detection |
| Broken Function Level Authorization | Policy-based authorization |
| Unrestricted Resource Consumption | Rate limiting, pagination |
| Security Misconfiguration | Fail-fast on missing config, no defaults |
| Injection | Parameterized queries via EF Core |
| Sensitive Data Exposure | DTO projection, no PII in logs |
| Improper Error Handling | ProblemDetails, no stack traces in prod |
| Mass Assignment | Explicit DTOs, no entity exposure |

## Configuration and secrets

Secrets are never committed. See [CONFIGURATION.md](CONFIGURATION.md) for the required environment variables (database, JWT, encryption, blind-index keys), User Secrets for local development, the least-privilege MySQL account recommendation, and the rotation procedures (including the impact on encrypted data and uniqueness hashes).
