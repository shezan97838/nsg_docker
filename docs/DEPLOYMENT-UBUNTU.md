# Deployment on Ubuntu 24.04 + MySQL 8

## 0. What is verified and what is not (read first)
**The application has never been run on Ubuntu.** The build machine is Windows 11 with no WSL or Docker. Linux compatibility is **not** verified. Everything marked "Verified" below was executed on Windows only.

| Verified (executed, 2026-10-15) | Result |
|---|---|
| Full test suite incl. MySQL integration tests, on MySQL 8.0.44 (Windows) in the disposable DB `nsg_mobileauth_test` | 566 of 566 pass |
| All migrations from an empty database, apply → roll back → re-apply (done on that DB in earlier steps; the test fixture applies the full chain on every run) | OK |
| Static check of the complete migration script: every table reference matches the table's exact case at that point of the chain; final tables all lowercase (matters because Windows MySQL ignores case, Linux MySQL does not) | 0 violations; **table names only** — see 6.2 |
| `dotnet publish -r linux-x64 --self-contained false` produces a package; it contains no `appsettings.Development.json` | OK |
| The published package started on Windows with production-style settings and **refused to start** for: placeholder/short JWT secret, missing or non-absolute `FileStorage__BasePath`, forwarded headers enabled without proxies, demo OTP in Production/Staging-without-flag/non-`_demo` database | OK |
| Cross-platform unit tests: path-inside check, trusted-proxy middleware (TestServer), project-reference casing, no auto-migration in source | pass (on Windows) |

| **Requires a real Ubuntu server (unverified)** |
|---|
| Application starts and serves under systemd as an unprivileged user; file/directory modes with `UMask=0077`; log directory writes under `ProtectSystem=strict` |
| MySQL 8 on Linux: `lower_case_table_names=0` behaviour for columns/indexes/constraint names (see 6.2), `SslMode=Required` connection, app account with DML-only rights |
| nginx TLS termination, `X-Forwarded-*` handling, HTTP→HTTPS redirect, the real client address in audit logs and rate limiting |
| Persistent file storage on the real volume and its backup |
| Behaviour after reboot and `Restart=on-failure` loops |

## 1. Review findings and what was changed (2026-10-15)
| # | Severity | Finding (evidence) | Status |
|---|---|---|---|
| F1 | **Critical** | `appsettings.json` ships `Jwt:Secret = REPLACE_WITH_SECURE_SECRET_…` (public, 56 chars) and nothing validated it; the host started with `Jwt__Secret=x`. A deployment that forgets the variable would sign tokens with a publicly known key (forge `ITAdmin`). | **Fixed**: `StartupSecretsValidator` refuses to start on a missing/placeholder/<32-byte JWT secret, missing issuer/audience, bad Encryption/BlindIndex key (also now at startup instead of first request). Tested, and confirmed on the published host. |
| F2 | **High** | `CorrelationIdMiddleware` copied any client `X-Correlation-Id` into the log and audit row; `audit_logs.correlation_id` is `varchar(100)` and `AuditService` swallows write errors, so a 400-character header silently suppressed the audit entry of that request (failed logins, approvals). Reproduced on MySQL before the fix (no audit row). | **Fixed**: only `[A-Za-z0-9._-]{1,64}` is accepted, otherwise a generated id is used. MySQL regression test passes. |
| F3 | Medium | Legacy `AuthController.GetClientIpAddress` trusted the raw `X-Forwarded-For` header (client-controlled) for audit IPs. | **Fixed**: uses the socket address only (the trusted-proxy middleware has already corrected it when configured). |
| F4 | Medium | systemd/nginx templates were incomplete (no `UMask`, no hardening, no TLS server block, no health-endpoint restriction). Default `umask 022` would leave stored files world-readable. | **Fixed in this document** (sections 3–4). Untested on Ubuntu. |
| F5 | Medium | `AllowedHosts` is `*`. | Set `AllowedHosts` in the environment file (section 3). Config only. |
| F6 | Medium (accepted) | Mobile sessions have no absolute lifetime cap (sliding rotation); no device binding; refresh limited by the shared per-IP `auth` rate limit (10/min) — many phones behind one NAT could hit it. | Documented; tune `RateLimiting__AuthPermitLimit`; ADR needed for the cap. |
| F7 | Low (accepted) | CORS `AllowAnyOrigin` (TODO in `Program.cs`). Bearer tokens only, no cookies, so not exploitable for session theft; restrict before a browser admin portal exists. | Open. |
| F8 | Low | `/health/ready` runs a MySQL query and is unauthenticated. | nginx restricts `/health/` to localhost (section 4). |

**Reviewed, no gap found** — refresh rotation and replay: the presented token is claimed by a conditional `UPDATE … WHERE revoked_at_utc IS NULL AND expires_at_utc > now` inside a transaction, so concurrent use has one winner (tested with 6 parallel requests); a token already rotated and presented again revokes the whole session; tokens revoked for other reasons (logout, MPIN change, new login) are refused without raising a theft alarm. JWT session validation: every `token_use=mobile_access` token is checked against a live session of an active profile on each request (logout, MPIN change/reset, new login, deactivation take effect immediately). Separation: mobile tokens (`sub=mobile:{id}`, role `Employee`) cannot satisfy `RequireITAdmin`; staff tokens lack `profile_id`/`token_use` and fail `RequireMobileProfile`; staff-only controllers using `UserId` answer 401 to a mobile token. Demo OTP: cannot be enabled in Production (host and service both refuse), cannot start outside Development without the explicit flag, and then only against a database named `*_demo`.

## 2. Prerequisites
```bash
sudo apt update
sudo apt install -y aspnetcore-runtime-8.0 nginx          # .NET 8 is in the Ubuntu 24.04 archive; confirm the package name on the server
# MySQL 8.0: sudo apt install -y mysql-server   (or an approved managed instance)
sudo useradd --system --home-dir /opt/nsg --shell /usr/sbin/nologin nsg
sudo install -d -o root -g root -m 0755 /opt/nsg /opt/nsg/api
sudo install -d -o nsg  -g nsg  -m 0750 /opt/nsg/api/logs /var/lib/nsg /var/lib/nsg/files
sudo install -d -o root -g nsg  -m 0750 /etc/nsg
```
Database (DBA, once): create the schema `nsg_mobile` (`CHARACTER SET utf8mb4`), a **migration account** with DDL rights on it, and a separate **application account** with only `SELECT, INSERT, UPDATE, DELETE` on it (the app never runs DDL). Linux MySQL is case-sensitive for table names (`lower_case_table_names=0`, set at initialisation); do not change it.

## 3. Configuration
`/etc/nsg/api.env` (mode 0640, `root:nsg`). **Every** value below is mandatory in Production:
```
ASPNETCORE_ENVIRONMENT=Production
ASPNETCORE_URLS=http://127.0.0.1:5000
AllowedHosts=api.example.in
ConnectionStrings__MySql=Server=db-host;Port=3306;Database=nsg_mobile;Uid=nsg_app;Pwd=…;SslMode=Required;AllowUserVariables=True
Jwt__Secret=<random, at least 32 bytes, e.g. openssl rand -base64 48>
Jwt__Issuer=NSG.Mobile.API
Jwt__Audience=NSG.Mobile.App
Encryption__Key=<openssl rand -base64 32>
BlindIndex__Key=<openssl rand -base64 32, different from Encryption__Key>
FileStorage__BasePath=/var/lib/nsg/files
ForwardedHeaders__Enabled=true
ForwardedHeaders__KnownProxies__0=127.0.0.1
```
Optional: `Jwt__AccessTokenExpirationMinutes` (15), `Jwt__RefreshTokenExpirationDays` (7), `RateLimiting__AuthPermitLimit` (10/min/IP), `RateLimiting__OtpPermitLimit` (5 per 5 min/IP), `Redis__ConnectionString`, `Sms__*`. **Do not set** `Otp__Mode` (default `Sms`; `Demo` makes the host exit), `Security__DisableAuthorization`, or `ASPNETCORE_ENVIRONMENT=Development`. Losing a key makes encrypted PIS fields unreadable: store the keys in the approved secret store and back them up separately from the database.

`/etc/systemd/system/nsg-api.service`:
```ini
[Unit]
Description=NSG Mobile API
After=network-online.target mysql.service
Wants=network-online.target
StartLimitIntervalSec=300
StartLimitBurst=5

[Service]
User=nsg
Group=nsg
WorkingDirectory=/opt/nsg/api
ExecStart=/usr/bin/dotnet /opt/nsg/api/NSG.Mobile.API.dll
EnvironmentFile=/etc/nsg/api.env
Restart=on-failure
RestartSec=5
TimeoutStopSec=30
SyslogIdentifier=nsg-api
UMask=0077
NoNewPrivileges=true
PrivateTmp=true
ProtectSystem=strict
ProtectHome=true
ReadWritePaths=/var/lib/nsg /opt/nsg/api/logs

[Install]
WantedBy=multi-user.target
```
`UMask=0077` keeps stored files private to `nsg`. `WorkingDirectory` must stay the application directory (settings are read from it) and `logs/` under it must be writable (Serilog path is relative).

## 4. nginx (TLS termination) — `/etc/nginx/sites-available/nsg-api`
```nginx
server {
    listen 80;
    server_name api.example.in;
    return 301 https://$host$request_uri;
}
server {
    listen 443 ssl http2;
    server_name api.example.in;
    ssl_certificate     /etc/ssl/nsg/fullchain.pem;
    ssl_certificate_key /etc/ssl/nsg/privkey.pem;
    ssl_protocols TLSv1.2 TLSv1.3;
    server_tokens off;
    client_max_body_size 12m;

    location /health/ { allow 127.0.0.1; deny all; proxy_pass http://127.0.0.1:5000; }

    location / {
        proxy_pass http://127.0.0.1:5000;
        proxy_set_header Host $host;
        proxy_set_header X-Forwarded-For $remote_addr;     # overwrite, never append client-supplied values
        proxy_set_header X-Forwarded-Proto $scheme;
        proxy_read_timeout 60s;
    }
}
```
Without `ForwardedHeaders__*` the app sees every request as plain HTTP from 127.0.0.1: `RequireHttps` refuses POSTs (fail closed, visible) and all clients share one rate-limit bucket. The application uses forwarded headers only from the listed proxy.

## 5. Deployment order
1. **Build machine:** `dotnet publish src/NSG.Mobile.API -c Release -r linux-x64 --self-contained false -o ./publish`; confirm `./publish` has no `appsettings.Development.json`; record `sha256sum` of the archive.
2. **Generate and review the SQL** (SDK + `dotnet-ef` 8.x on the build machine; run with `ASPNETCORE_ENVIRONMENT=Development` and dummy keys, no database is contacted):
   `dotnet ef migrations script --idempotent --project src/NSG.Mobile.Infrastructure --startup-project src/NSG.Mobile.API -o nsg-migrations.sql`
3. **Backup** (if the schema already holds data): `mysqldump --single-transaction --routines -h db-host -u backup_user -p nsg_mobile | gzip > nsg_mobile_$(date +%F_%H%M).sql.gz`. Verify the file restores on a scratch server.
4. **Stop the service:** `sudo systemctl stop nsg-api`.
5. **Apply the migrations** with the migration account (never from the app): `mysql -h db-host -u nsg_migrator -p nsg_mobile < nsg-migrations.sql`. Never apply to production without written approval.
6. **Check:** `SELECT MigrationId FROM __EFMigrationsHistory ORDER BY 1 DESC LIMIT 3;` must end with `20261014213919_Add_Mobile_Refresh_Tokens`; `SHOW TABLES;` must list only lowercase names (plus `__EFMigrationsHistory`).
7. **Deploy files:** `sudo mv /opt/nsg/api /opt/nsg/api.prev && sudo install -d -o root -m 0755 /opt/nsg/api && sudo cp -r publish/. /opt/nsg/api/ && sudo install -d -o nsg -g nsg -m 0750 /opt/nsg/api/logs`.
8. **Start:** `sudo systemctl daemon-reload && sudo systemctl enable --now nsg-api`, then run the smoke test (section 7).

First deployment on an empty database applies the whole chain (including the legacy rename migrations); that chain is exercised from scratch by the test fixture on Windows MySQL only.

## 6. Rollback plan and migration safety
**6.1 Safety.** Both new migrations (`20261014204124_Add_Mobile_Profile_Approval_And_Grant_Tables`, `20261014213919_Add_Mobile_Refresh_Tokens`) only create new tables, indexes and one foreign key; no existing table or data is changed, so the previous application version keeps working against the new schema. Neither has been applied to `nsg_mobile_dev` or production. PIS tables and rows are not touched by any migration in this change.

**6.2 What is not proven.** Windows MySQL ignores table-name case; Linux does not. The static check above proves table references are case-exact. Column, index and constraint names are case-insensitive in MySQL on every platform, so they cannot fail this way, but the first run on real Linux MySQL (against a scratch database, not production) is still required.

**6.3 Application rollback (preferred):** `sudo systemctl stop nsg-api && sudo mv /opt/nsg/api /opt/nsg/api.bad && sudo mv /opt/nsg/api.prev /opt/nsg/api && sudo systemctl start nsg-api`. The additive schema can stay.

**6.4 Schema rollback (only if unavoidable, before real users register):** generate the reverse script, review, and apply with the migration account:
`dotnet ef migrations script 20261014213919_Add_Mobile_Refresh_Tokens 20261014102929_Rework_EducationQualifications_To_CourseName --project … --startup-project … -o nsg-rollback.sql`
This drops `mobile_refresh_tokens`, `mobile_auth_grants`, `approve_mobile_users`, `mobile_user_profiles` **and every profile, request and session in them**. Once people have registered, restore from the step-3 backup instead of rolling back the schema, and export the approval requests first.

## 7. Ubuntu smoke-test checklist (none of this has been run)
Replace `H` with `https://api.example.in`.
1. **Startup:** `systemctl is-active nsg-api` → `active`; `journalctl -u nsg-api -n 50` shows no exception and **no** "DEMO OTP MODE" warning; `ps -o user= -C dotnet` → `nsg`.
2. **Fail-closed (on a throw-away copy of the env file):** empty `Jwt__Secret`; `Otp__Mode=Demo`; non-absolute `FileStorage__BasePath` — each must make the service exit with a clear message and no listener on :5000.
3. **Health:** `curl -s http://127.0.0.1:5000/health/live` and `/health/ready` → `Healthy`; `curl -s -o /dev/null -w '%{http_code}' $H/health/live` from outside → `403`.
4. **nginx / HTTPS:** `curl -sI http://api.example.in/` → `301` to https; `curl -sI $H/api/v1/nsg-trades` → `401` with `Strict-Transport-Security`, `X-Content-Type-Options`, no `Server` version; the 401 has a JSON body. TLS 1.1 refused (`openssl s_client -tls1_1`).
5. **Client address:** one failed `POST $H/api/v1/mobile-auth/login`, then `SELECT ip_address, correlation_id FROM audit_logs ORDER BY id DESC LIMIT 1;` → your real client IP, not `127.0.0.1`. Repeat with `-H 'X-Forwarded-For: 1.2.3.4'` and `-H "X-Correlation-Id: $(printf 'A%.0s' {1..400})"`: the stored IP is still yours and an audit row exists.
6. **Rate limiting:** 12 rapid `POST $H/api/v1/mobile-auth/login/check` from one machine → the 11th returns `429` with a body; from a second machine the limit is independent.
7. **Login (needs a registered test profile on a non-production DB):** `POST $H/api/v1/mobile-auth/login` → `accessToken`, `refreshToken`; `POST …/mpin/change` with the access token and a wrong current MPIN → `401 AUTH_INVALID_CREDENTIALS` (proves the session is live).
8. **Refresh rotation:** `POST …/token/refresh {"refreshToken":R1}` → new pair R2. Replay R1 → `401 TOKEN_REUSE_DETECTED`; R2 now also fails (`401`); the old access token fails (`401`). `SELECT revoke_reason FROM mobile_refresh_tokens` shows `Rotated`, `ReuseDetected`; no plain token in the table.
9. **Logout:** log in again, `POST …/logout {"refreshToken":R}` → `200 "You have been logged out."`; the same R and its access token → `401`; repeating logout → `200`.
10. **MPIN change revokes sessions:** log in, change MPIN, old access/refresh tokens → `401`; log in with the new MPIN.
11. **File storage** (no HTTP endpoint exists yet): `sudo -u nsg test -w /var/lib/nsg/files && echo ok`; `ls -ld /var/lib/nsg/files` → `drwxr-x--- nsg nsg`; after any stored file, `stat -c %a` → `600`; the directory is on the persistent volume and included in the backup; survives `systemctl restart nsg-api` and a reboot.
12. **Hosted demo instance only:** with `Otp__Mode=Demo`, `Otp__AllowDemoOutsideDevelopment=true`, `ASPNETCORE_ENVIRONMENT=Staging` and database `nsg_hosted_demo`, `POST …/otp/send` → message starts "Demo OTP mode is active. No SMS has been sent" and `demoOtpMode:true`; the same settings against any database not ending in `_demo` must not start.
13. **Admin boundary:** a mobile access token on `GET $H/api/v1/mobile-approval-requests` → `403` with a body.

## 8. Hosted client demo (isolated environment)
Separate host or instance, never the production one. Environment file as in section 3, plus/with these changes:
```
ASPNETCORE_ENVIRONMENT=Staging
Otp__Mode=Demo
Otp__AllowDemoOutsideDevelopment=true
ConnectionStrings__MySql=Server=…;Database=nsg_hosted_demo;…      # name MUST end in _demo
FileStorage__BasePath=/var/lib/nsg-demo/files
```
The demo OTP is `123456` for any eligible person. The database must hold **dummy personnel only**, in the client-approved PIS format; no code seeds or alters PIS rows, and the `_demo` name is the only technical guard — that the contents are dummy is a procedural control. After the presentation: remove the two `Otp__*` variables, stop the instance and drop the demo database.
