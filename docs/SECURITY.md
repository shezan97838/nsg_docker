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

Secrets are never committed. See [CONFIGURATION.md](CONFIGURATION.md) for the required environment variables (database, JWT, encryption keys), User Secrets for local development, the least-privilege MySQL account recommendation, and the rotation procedures (including the impact on encrypted data).

## Single active mobile session - `SURAKSHA_1002` (ADR-025, 2026-11-06)
- Latest successful login wins: `StartSessionAsync` revokes every live refresh token of the profile (reason `NewLogin`) and stores the new session in one transaction behind the profile row lock, so concurrent logins serialize and exactly one session stays live. A failed login replaces nothing.
- A replaced session is reported as `SURAKSHA_1002`: 401 for its access token (database check on every request), 403 for its refresh token; no token is ever issued to it. It is reported only when the family's latest row carries `NewLogin` and the profile is not blocked; a PIS Admin block always wins (`SURAKSHA_1001` unchanged, never 1002).
- Logout, MPIN change/reset, expiry alone, genuine token reuse and unknown tokens keep their old answers. An old rotated token of an already replaced session is answered as replaced (not as theft) and does not rewrite the revoke reason.
- `SURAKSHA_1001` and `SURAKSHA_1002` are the only entries of `ApiErrorCodes.PublicCodes`. The Admin Portal and staff authentication are separate schemes and are unchanged.

## Remote block of mobile access (ADR-024, 2026-11-06)
- A PIS Admin (Admin Portal, `RequirePisAdminPortal`: AdminBearer + `PIS_ADMIN` only) can block a person's mobile access by NSG number: the profile becomes inactive with a block marker and every refresh token is revoked in one transaction (reason `AdminBlocked`).
- Access tokens are checked against the database on every request and stop working on the next one; refresh and login are refused; a login that races a block cannot store a session (conditional row lock + read in the same transaction).
- Restore clears the marker only for a profile blocked this way, never revives a revoked session, and never reactivates a profile deactivated for another reason.
- The client-visible code `SURAKSHA_1001` is allow-listed (`ApiErrorCodes.PublicCodes`) and reserved for that one cause; no other internal code reaches a response body.
- Block and restore are audited (admin, profile, NSG number, reason, IST time); an audit failure is logged and never undoes the change. The server cannot erase data on the phone: the app must clear it when it receives the code.
## Registration approval by the assigned PIS Admin (ADR-026, 2026-11-07)
- `GET` and `PUT api/v1/mobile-approval-requests` require `RequirePisAdminPortal` (AdminBearer + `PIS_ADMIN`). Legacy staff Bearer tokens (401), Super Admin and Registration Admin (403) and anonymous callers (401) are refused. The reviewer is the token's `admin_profile_id` (long); a request body cannot name one.
- Ownership is `users_pis.assigned_to`, read live at list time (in the SQL join, before paging) and at decision time (a conditional row lock as the first statement of the decision transaction), so a transfer racing with a decision cannot authorise the previous owner. A non-owner and an unassigned record get the same 403, before any status is revealed.
- `approved_by` is an int column: an admin id above `int.MaxValue` is refused instead of truncated.

- Ownership transfer (`POST api/v1/admin/pis-personnel/transfer-ownership`) is `RequireSuperAdmin` only and grants no PIS read, update, delete, upload or approval access. It changes `users_pis.assigned_to` alone inside a transaction that locks the personnel row first, and appends an audit-quality row (previous owner, new owner, acting Super Admin from the token, reason, IST time) to `pis_ownership_transfers`.
- PIS Admin edits and deactivations (`PUT`/`DELETE api/v1/users-pis`) re-check ownership under the same row lock when they save, so an edit loaded before a transfer cannot be saved after it.
