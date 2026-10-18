# ADR-017: Admin Portal account management and authentication

Status: Accepted (implemented; migration `Add_Admin_Refresh_Tokens` created, **not yet applied to any shared database**)
Date: 2026-10-18. Builds on ADR-016 (`admin_roles`, `admin_profiles`). All timestamps are IST (`CLAUDE.md`, "Timestamp Convention").

## Decision

### Identity and token separation
- Admin identity is `admin_profiles`; it never touches `mobile_user_profiles` or `users`.
- Admin access tokens use the same key, issuer and HMAC-SHA256 lock as every JWT, but their **own audience** (`Jwt:AdminAudience`, default
  `{Jwt:Audience}.Admin`; startup fails if the two are equal), `token_use=admin_access`, `sub=admin:{id}`, claim `admin_profile_id`, role claim = role
  code, `sid` = session. They are validated by a second JWT scheme, **`AdminBearer`**.
- The default scheme (used by every existing endpoint, including bare `[Authorize]`) rejects admin tokens on the audience; `AdminBearer` rejects
  mobile and staff tokens on the audience. All admin policies name `AdminBearer` and require `token_use=admin_access`.
- On **every** admin request `AdminBearer` checks the database: the session (refresh-token family) is live, and the profile is enabled, `Active`, and
  still holds the role in the token. Logout, a newer sign-in, deactivation or a changed role therefore end an access token at once.

### Authorization
Seven policies in `Program.cs`, constants in `DomainConstants.Policies`: `RequireAdminAccount` (any of the six roles) and one per fixed role.
A policy admits exactly its role: **Super Admin does not implicitly pass the other roles' policies** (least privilege; see open question below).
Role and acting admin always come from the token, never from a request.

### Storage
Only one new table, `admin_refresh_tokens` (same shape as `mobile_refresh_tokens`, FK `admin_profile_id` -> `admin_profiles.id` Restrict). It is
required because `mobile_refresh_tokens` and `refresh_tokens` have foreign keys to other identity tables. No grant table: activation is a single
request that carries the OTP reference, the OTP and the new password, so it needs no state between steps.
`OtpPurpose` gains `AdminActivation = 5` and `AdminLogin = 6` (stored as int, no check constraint, no schema change).

### Workflow and endpoints
| Step | Endpoint | Access |
|---|---|---|
| Bootstrap first Super Admin | command `bootstrap-super-admin --nsg-number <n> --confirm` (below) | operator |
| Provision account | `POST api/v1/admin/accounts` | Super Admin |
| List / get / roles | `GET api/v1/admin/accounts`, `GET .../{id}`, `GET api/v1/admin/roles` | Super Admin |
| Deactivate / reactivate | `POST api/v1/admin/accounts/{id}/deactivate`, `.../reactivate` | Super Admin |
| Send activation OTP | `POST api/v1/admin/activation/otp` | anonymous, `otp` rate limit |
| Verify OTP, set password, activate | `POST api/v1/admin/activation/complete` | anonymous, `otp` rate limit |
| Sign in step 1 (password, sends OTP) | `POST api/v1/admin/auth/login` | anonymous, `auth` rate limit |
| Sign in step 2 (password + OTP, tokens) | `POST api/v1/admin/auth/login/verify` | anonymous, `auth` rate limit |
| Refresh (rotation, no OTP) | `POST api/v1/admin/auth/token/refresh` | anonymous (refresh token) |
| Logout (revokes the session) | `POST api/v1/admin/auth/logout` | anonymous (refresh token), idempotent |
| Who am I | `GET api/v1/admin/auth/me` | any admin role |

- **PIS verification.** Provisioning and activation require NSG number, mobile number and date of birth to equal one **active** `users_pis` record.
  Mismatch, unknown person, wrong status and already-activated all give the same 404 answer (nothing to enumerate).
- **OTP destination** is always `+91` + the PIS mobile number, never a request value; the OTP record's destination is re-checked on verification.
  Reuses `IOtpService` (hashing, expiry, attempts, cooldown, demo mode) and the per-hour send cap (`MobileAuthSettings.OtpMaxSendsPerHour`).
- **Provisioning** creates `PendingActivation`, enabled, `password_hash` NULL, `created_by` = the acting Super Admin. A Super Admin never sets a password.
- **Password** policy `AdminPasswordPolicy`: 12-128 characters with upper, lower, digit, symbol, not containing the NSG number. Stored with the existing
  PBKDF2-SHA256 (100K iterations) service; activation is a conditional UPDATE so exactly one activation wins.
- **Sign-in** is two calls and checks the password in both, so the OTP (sent only after a correct password) cannot be used alone. A wrong password or
  wrong OTP counts toward lockout (`MobileAuthSettings.MaxFailedAttempts`, `LockoutMinutes`; lock answers 423). Unknown account, pending account and wrong
  password return the same 401 and cost the same hashing time. A correct password on a disabled account answers 403 and sends no OTP.
- **Sessions.** One live session per admin (a new sign-in ends the previous one). Refresh rotates the token and detects reuse (whole family revoked),
  re-checks the account and sends no OTP. Logout revokes the family; the next sign-in needs an OTP again.
- **Deactivation** disables the account, clears lockout and revokes all sessions. It cannot target the caller and cannot remove the last active Super
  Admin (a check, not a locked transaction: two simultaneous deactivations of the final two Super Admins could both pass). Reactivation re-enables the
  account as it was; one that never activated still has to activate.
- **Audit** (`IAuditService`): sign-in success/failure, OTP requested/verified/failed, token refreshed/reuse, logout, and `AdminAction` events for
  create / activate / deactivate / reactivate / bootstrap, with the actor in `additional_data`. No passwords, OTPs or tokens. `audit_logs.user_id` is
  an `int` and admin ids are `bigint`, so the admin id is carried in `entity_id` / `additional_data`.

### Bootstrap procedure (first Super Admin)
1. The person must already exist in PIS (active, with a mobile number and date of birth).
2. An operator with access to the target environment's configuration runs, once:
   `dotnet NSG.Mobile.API.dll bootstrap-super-admin --nsg-number <NSG number> --confirm`
   It builds the host with the normal configuration, creates one `PendingActivation` SUPER_ADMIN row without a password, writes an audit record, prints the
   new id and exits **without starting the web host**. It never runs at startup, is not reachable over HTTP, and refuses once any Super Admin exists.
3. The person activates through `activation/otp` and `activation/complete` and chooses their own password; all further accounts are created through the API.
No default credential exists in source, configuration or the database.

## Consequences
- Existing mobile authentication, tokens, policies and tables are unchanged (`ITokenService`, `MobileAuthService` and their fakes are untouched).
- `MobileAuthSettings` (lockout, OTP cap, refresh lifetime) is shared by the mobile and admin flows rather than duplicated.

## Open questions / not done
- Should Super Admin also satisfy the other roles' policies? Today it does not; decide when the first role-specific admin module is built.
- Admin password reset / change and a forgotten-password flow are not implemented (the OTP purposes `PasswordReset` exists; no endpoint uses it for admins).
- CORS is still the open `MobileApp` policy for every origin (existing TODO); the Admin Portal origin must be added when known.
- Account list ordering is by id; no sort options.
- The `(IsActive, last Super Admin)` guard race described above.
