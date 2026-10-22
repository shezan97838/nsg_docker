# Configuration and Secrets

> How the NSG Mobile API gets its configuration, which settings are secrets, and how to rotate them.
> **This document contains no real values. Never paste a real secret into this file, a ticket, a chat or a log.**

## 1. Rules

1. `appsettings.json` and `appsettings.Development.json` are committed and hold **placeholders and non-secret values only**.
   `ConfigurationSafetyTests` fails the build if a real password, key or secret appears in them.
2. Real values come from **User Secrets** (local development only) or **environment variables / a secret store** (every other environment).
3. Secrets are never passed on a command line (visible in process lists) and never written to logs, test output or documentation.
4. `.gitignore` excludes `appsettings.Production.json`, `appsettings.Staging.json`, `appsettings.Local.json`, `appsettings.*.local.json`,
   `secrets.json`, `UserSecrets/`, `.env*` (except `*.env.example`), and key/certificate files (`*.pfx`, `*.p12`, `*.pem`, `*.key`).

## 2. Precedence (last one wins)

`appsettings.json` → `appsettings.{Environment}.json` → **User Secrets (Development only)** → **environment variables** → command line.

In an environment variable name, `__` stands for `:` (for example `ConnectionStrings__MySql` is `ConnectionStrings:MySql`).

## 3. Required settings

| Environment variable | Secret | Required | Notes |
|---|---|---|---|
| `ASPNETCORE_ENVIRONMENT` | no | yes | `Production` on servers. User Secrets are loaded only for `Development`. |
| `ConnectionStrings__MySql` | **yes** | yes | MySQL connection string for the application account (section 6). Use `SslMode=Required` unless the database is on the same host. |
| `Jwt__Secret` | **yes** | yes | HMAC-SHA256 signing key, at least 32 random bytes. Generate with `openssl rand -base64 48`. |
| `Jwt__Issuer`, `Jwt__Audience` | no | yes | Per environment; they must match what the mobile apps expect. |
| `Encryption__Key` | **yes** | yes | AES-256 field-encryption key: base64 of **exactly 32 bytes**. Generate with `openssl rand -base64 32`. |
| `BlindIndex__Key` | **yes** | yes | HMAC key for the PAN / salary-account uniqueness hashes: base64, **at least 32 bytes** and **different from `Encryption__Key`**. Generate with `openssl rand -base64 32`. |
| `Redis__ConnectionString` | yes | optional | Empty means in-memory cache. |
| `Sms__ApiKey`, `Hrms__ApiKey`, `Payroll__ApiKey` | **yes** | when those integrations go live | Currently stubs. |
| `Fcm__CredentialPath` | path only | when push goes live | Path to the Firebase service-account file, which itself is a secret and stays outside the repository. |
| `FileStorage__BasePath` | no | yes on Linux | The default in `appsettings.json` is a Windows path; set an absolute Linux path. Still required outside Development when R2 is used. |
| `R2Storage__ServiceUrl` | no | to use R2 | `https://<ACCOUNT_ID>.r2.cloudflarestorage.com`. A real value switches grievance document storage to Cloudflare R2; the committed placeholder means off (ADR-018, `GRIEVANCE-MODULE.md`). |
| `R2Storage__BucketName` | no | with R2 | The private bucket. |
| `R2Storage__AccessKey`, `R2Storage__SecretKey` | **yes** | with R2 | Bucket-scoped R2 API token (Object Read & Write). User-secrets or environment only. Missing values stop startup when R2 is on. |
| `R2Storage__KeyPrefix` | no | optional | Folder in front of every object, for example one per environment. |
| `RateLimiting__GrievanceWritePermitLimit` | no | optional | Grievance creates + uploads per client IP per minute; default 10. |

Tools and tests:

| Variable | Used by | Notes |
|---|---|---|
| `ConnectionStrings__MySql` | `tools/NSG.Mobile.GeoImport` | Read only from the environment. |
| `NSG_TEST_MYSQL` | `tests/NSG.Mobile.Tests` | Connection string to a **scratch** database whose name ends in `_test`; the suite drops and recreates it. The tests generate throw-away JWT, encryption and blind-index keys in memory unless `Jwt__Secret`, `Encryption__Key`, `BlindIndex__Key` are already set. |

`/.env.example` lists these names with placeholders.

## 4. Local development (User Secrets)

The API project has a `UserSecretsId`. Secrets are stored per user outside the repository
(Windows: `%APPDATA%\Microsoft\UserSecrets\<id>\secrets.json`; Linux/macOS: `~/.microsoft/usersecrets/<id>/secrets.json`).

```bash
dotnet user-secrets set "ConnectionStrings:MySql" "<connection string>" --project src/NSG.Mobile.API
dotnet user-secrets set "Jwt:Secret"              "<random base64>"      --project src/NSG.Mobile.API
dotnet user-secrets set "Encryption:Key"          "<base64 of 32 bytes>" --project src/NSG.Mobile.API
dotnet user-secrets set "BlindIndex:Key"          "<base64, >= 32 bytes>" --project src/NSG.Mobile.API
dotnet user-secrets list --project src/NSG.Mobile.API     # shows values: do this only in a private terminal
dotnet user-secrets remove "Jwt:Secret" --project src/NSG.Mobile.API
```

Prefer generating the values with `openssl` and piping them into the store rather than typing them. A new developer needs the
database host/account from the team lead through a secure channel, not through the repository.

## 5. Servers (Linux / NIC cloud)

* Supply the variables from the platform secret store or from a root-owned file loaded by the service manager, for example a
  systemd unit with `EnvironmentFile=/etc/nsg-mobile-api/env`, file mode `0600`, owner root. The file format is `NAME=value`
  (see `/.env.example`).
* Do not bake secrets into images, scripts, CI logs or `appsettings.Production.json` that is checked in anywhere.
* Restart the service after any change; configuration is read at startup.

## 6. Database account: do not use `root`

**Recommendation (not applied; needs your explicit approval before any database user is created or changed).**
Use two accounts with different lifetimes, both restricted to the application schema and to the application host:

| Account | Used by | Privileges on the NSG schema only |
|---|---|---|
| runtime (for example `nsg_app`) | the running API | `SELECT, INSERT, UPDATE, DELETE` |
| migrator (for example `nsg_migrator`) | deployment step that applies EF migrations | `SELECT, INSERT, UPDATE, DELETE, CREATE, ALTER, DROP, INDEX, REFERENCES` (add `CREATE VIEW`/`TRIGGER` only if later needed); enabled only during deployment |

Template for a DBA to review (placeholders only; run nothing without review):

```sql
CREATE USER 'nsg_app'@'<app-host>' IDENTIFIED BY '<generated password>' REQUIRE SSL;
GRANT SELECT, INSERT, UPDATE, DELETE ON `<schema>`.* TO 'nsg_app'@'<app-host>';
-- no GRANT OPTION, no global privileges, no access to mysql.* or other schemas
```

The API does not apply migrations and does not seed data at startup, so the runtime account never needs DDL rights;
reference data (ranks, states, cities, lookups) lives only in the database.

## 7. Rotation procedures

### 7.1 Database password (no data impact)

1. Generate a new password in the secret store.
2. MySQL 8: `ALTER USER '<user>'@'<host>' IDENTIFIED BY '<new>' RETAIN CURRENT PASSWORD;` so both passwords work during the roll-out.
3. Update `ConnectionStrings__MySql` and restart the service(s) one at a time; confirm `/health/ready` is `200` on each.
4. `ALTER USER '<user>'@'<host>' DISCARD OLD PASSWORD;`.
5. Verify the old password is rejected, then remove it from every place it was stored.

**Existing exposure:** the development database password (root) previously lived in plain text in files in the working copy. Treat
it as exposed: rotate it using the steps above and move the application to the dedicated account in section 6.

### 7.2 JWT signing key

Impact: every issued access token becomes invalid when the new key is deployed (access tokens live 15 minutes in production). Refresh
tokens are stored hashed in the database and are not signed with this key, so clients recover by refreshing. The code supports a
single key only, so there is no overlap period: deploy off-peak. Plan for a short burst of 401s and refresh calls.

### 7.3 Encryption key (`Encryption__Key`) — affects stored data

Impact: AES-CBC ciphertext in `UsersPIS.AadhaarNumberEncrypted`, `PanNumberEncrypted`, `PranNumberEncrypted` and
`SalaryAccountNumberEncrypted` can only be decrypted with the key that wrote it. Changing the key without re-encrypting makes
those values unreadable (the API then shows them as missing). The ciphertext format carries **no key identifier**, so old and new
keys cannot be told apart.

Safe procedure (maintenance window, database backup first, writes to `users_pis` paused):

1. Take and verify a backup.
2. Run a one-off re-encryption job that holds the old and new keys, decrypts each value with the old key and re-encrypts it with
   the new key, in batches inside transactions.
3. Switch `Encryption__Key` to the new key, restart, read back a sample of records.
4. Destroy the old key after the backup retention period.

Current state: `users_pis` is empty in the development database; production volumes are not known. Before production data exists,
decide whether to add a key-version prefix to the ciphertext (an ADR) so future rotations can be gradual.

### 7.4 Blind-index key (`BlindIndex__Key`) — affects uniqueness protection

Impact: `UsersPIS.PanNumberHash` and `SalaryAccountNumberHash` are HMACs of the plaintext and cannot be recomputed from themselves.
If the key changes without re-indexing, new submissions are hashed with the new key and will **not match** the old hashes, so
duplicates of existing PANs and salary accounts are silently accepted and uniqueness is lost for existing rows.

Procedure (same maintenance window as 7.3 if both rotate): pause writes, decrypt each PAN and account number with the
current encryption key, recompute both hashes with the new blind-index key, update the rows in batches, then deploy the new key.
Verify with a duplicate-submission test.

### 7.5 If a key is compromised (not routine rotation)

Rotating is not enough on its own: a leaked encryption key means previously stored ciphertext (and any backups) can be read. Follow the
security incident process, rotate as above, and assess notification duties for the personal data involved.

## 8. Guard rails and known gaps

* `ConfigurationSafetyTests` (tests project) fail if a committed config file contains a real password, a non-placeholder secret, a
  private-key block, or a connection string that uses `root`. Run them before the first commit and in CI.
* Scan the full history with a secret scanner (for example gitleaks) before the first push of any new repository.
* **Phase 2 gaps, not yet fixed:** the application does not validate keys at startup and does not reject `REPLACE_*` placeholders.
  An invalid base64 key fails on first use (HTTP 500), and a missing `Jwt__Secret` leaves the 60-character placeholder in force as the
  signing key, which is public. Production deployments must therefore always set `Jwt__Secret`; fail-fast startup validation is the
  recommended next change.

## 9. Temporary authorization bypass (local development only)

While the token/login flow is being built, `Security:DisableAuthorization` can switch authorization off so endpoints work without a token.

* It is `true` only in `appsettings.Development.json`. `appsettings.json` and every other environment leave it off.
* `Program.cs` throws at startup if the setting is `true` in any environment other than `Development`; it can never run in Staging or Production.
* The `[Authorize]` attributes and policies are unchanged. Setting the flag to `false` (or removing it from the Development file) restores full enforcement, which is how the whole test suite runs.
* With the bypass on, a valid token is still honoured. Endpoints that need a signed-in user (`/profile`, `/devices/current`, `/mobile-auth/logout`) still need a real token, because there is no identity otherwise. Records created without a token have no `CreatedBy`/`UpdatedBy`, and audit entries have no user id.
* A warning is logged at startup whenever the bypass is active.
* **To re-enable authorization:** delete the `Security` block from `appsettings.Development.json`, then remove `DevelopmentAuthorizationBypassHandler` and its registration block in `Program.cs` once the login flow is complete.

### Temporary reviewer `AsmarTest` (TEMP-DEV-REVIEWER)

With no Registration Admin login yet, approving or rejecting a mobile approval request (`PUT /api/v1/mobile-approval-requests/{id}/decision`) with no token user is recorded as reviewer `AsmarTest`.

* Active only when the bypass above is active, so only in Development; `ReviewerIdentityResolver` also checks `IHostEnvironment.IsDevelopment()`. Staging and Production never accept it (the request is refused with 401, as before).
* A real token user id always wins. The name is never read from the request body.
* `approved_by` is numeric, so it stores the stand-in `-1`; the name `AsmarTest` is written to the audit entry `additional_data` (`ITApprovalGranted` / `ITApprovalRejected`).
* **To remove:** delete `ReviewerIdentityResolver.cs` fallback (class `DevelopmentReviewerOptions` and the `TEMP-DEV-REVIEWER` branch), the `Configure<DevelopmentReviewerOptions>` line in `Program.cs`, and `tests/NSG.Mobile.Tests/DevelopmentReviewerTests.cs`. Search the repo for `TEMP-DEV-REVIEWER`.

## Mobile registration and login (ADR-015)
Optional section `MobileAuth` (defaults shown); lockout values default to `Security:MaxFailedLoginAttempts` / `Security:LockoutDurationMinutes`.

| Key | Default | Meaning |
|---|---|---|
| `MobileAuth:MaxFailedAttempts` | 5 | Wrong MPINs before lockout |
| `MobileAuth:LockoutMinutes` | 30 | Lockout length |
| `MobileAuth:GrantLifetimeMinutes` | 10 | Life of registration / MPIN-setup / reset grants |
| `MobileAuth:OtpMaxSendsPerHour` | 5 | OTPs per mobile number and flow per rolling hour |

OTP lifetime, length, attempts and resend cooldown come from `Security:Otp*`. **SMS:** the registered `INicSmsService` is still the placeholder (logs the OTP, sends nothing). To deliver real OTPs, implement `INicSmsService` against the NIC gateway, register it in `AddExternalIntegrations`, and supply `Sms__BaseUrl`, `Sms__ApiKey`, `Sms__SenderId`, `Sms__TemplateId` through secure configuration.

## OTP mode: demo OTP for presentations (temporary)
The BSNL SMS gateway is not available yet. `Otp:Mode` selects how OTPs are produced:

| `Otp__Mode` | Behaviour |
|---|---|
| `Sms` (default) | Random OTP sent through `INicSmsService`. With only the placeholder registered, every send **fails** (502 `OTP_SEND_FAILED`); nothing is faked and the OTP is not logged. If a real gateway fails, the send fails: there is never a fallback to demo. |
| `Demo` | Every OTP is `123456`. No SMS is sent; `otp/send` answers "Demo OTP mode is active. No SMS has been sent…" with `data.demoOtpMode=true`. Eligibility, hashing, 5-minute expiry, 5 attempts, 60-second resend cooldown, hourly cap, single use and purpose isolation all still apply. |

**Owner decision 2026-10-18:** `Otp:Mode=Demo` is now the default in `appsettings.json` and `OtpModeGuard` no longer restricts it by environment or database name, so Production sends no SMS and every OTP is `123456`. The "Fail closed" paragraph below describes the superseded behaviour. Revert (`Otp:Mode=Sms`, restore the guard) before the BSNL gateway goes live.

**Fail closed:** the host refuses to start with `Demo` unless the environment is `Development`, or `Staging`/`Demo` **and** `Otp__AllowDemoOutsideDevelopment=true`. `Production`, an unset or unknown environment name, and an unrecognised mode value all stop startup. `OtpService` repeats the check when it is constructed.

* **Local:** `appsettings.Development.json` already sets `Otp:Mode=Demo`. Run with `ASPNETCORE_ENVIRONMENT=Development`.
* **Approved presentation environment:** `ASPNETCORE_ENVIRONMENT=Staging`, `Otp__Mode=Demo`, `Otp__AllowDemoOutsideDevelopment=true`, on a database that holds no real personnel data. Record the approval, switch both variables off afterwards.
* **Switching to the real gateway later:** implement `INicSmsService` for BSNL, register it in `AddExternalIntegrations` in place of `NicSmsServicePlaceholder`, set `Otp__Mode=Sms` (or remove the key) and the `Sms__*` secrets. The registration workflow does not change.
* Startup logs a warning while demo mode is on; the OTP value is never logged.

## Rate limits
`RateLimiting__AuthPermitLimit` (default 10 per minute) and `RateLimiting__OtpPermitLimit` (default 5 per 5 minutes) are per client IP and apply to the `auth` and `otp` policies.

## Mobile sessions, forwarded headers and file storage (2026-10-15)
* **Mobile tokens:** login returns an access JWT (`Jwt:AccessTokenExpirationMinutes`, default 15) and a single-use refresh token (`Jwt:RefreshTokenExpirationDays`, default 7). Only the SHA-256 hash of the refresh token is stored (`mobile_refresh_tokens`). One active session per profile: a new login ends the previous one. See ADR-015.
* **`ForwardedHeaders`:** `Enabled` (default false), `KnownProxies`, `KnownNetworks` (CIDR). Required behind nginx; empty lists with `Enabled=true` stop startup.
* **`FileStorage:BasePath`:** empty in the committed file. Development uses `App_Data/filestorage`; every other environment must set an absolute path or the host refuses to start.
* **Demo outside Development** additionally requires a database whose name ends in `_demo`.
