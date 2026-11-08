# Local configuration (Windows and local Docker)

Status (2026-11-09): implemented and verified on the developer machine (both environments reached `/health/ready` = 200). Scope: **developer machine only**. Render and Ubuntu production are described at the end; their configuration is not changed by this document. General configuration reference: `CONFIGURATION.md`.

## 1. The two local environments

| | Windows (`dotnet run`) | Local Docker |
|---|---|---|
| Started with | `scripts\run-local.ps1` | `docker compose --env-file .env.local.secrets -f docker-compose.local.yml up --build` |
| `ASPNETCORE_ENVIRONMENT` | `Development` | `Development` (set by compose; plain HTTP works) |
| URL | `http://localhost:5062` | `http://localhost:8080` |
| MySQL | `127.0.0.1:3306`, database `nsg_mobile_dev` | `host.docker.internal:3306` (MySQL on the Windows host), the **same** database `nsg_mobile_dev` |
| `Database:MigrateOnStartup` | `false` (`appsettings.Development.json`) | `false` (compose) |
| `Encryption:Key`, `BlindIndex:Key` | from `.env.local.secrets` | the **same** values from `.env.local.secrets` |
| JWT secret / Issuer / Audience / AdminAudience | Windows values (section 4) | Docker values (section 4) |
| Document storage root (`Storage:RootPath`) | `D:\NSGStorage` (`appsettings.Development.json`) | `/var/data/files` on the Docker named volume `nsg_local_files` |
| `Security:DisableAuthorization` | `true` (from `appsettings.Development.json`, local development only) | `false` (compose, explicit) |
| `Otp:Mode` | `Demo` | `Demo` (compose, explicit) |

Keep `Otp:Mode=Demo` locally until the real SMS gateway is integrated. Demo OTP sends nothing and uses a fixed code.

## 2. How configuration is resolved

ASP.NET Core adds sources in this order; **later sources win**:

1. `appsettings.json` (committed; `REPLACE_*` placeholders, `Database:MigrateOnStartup=true` for Render)
2. `appsettings.{Environment}.json` (`appsettings.Development.json`: dev Issuer/Audience, `Storage:RootPath`, `Database:MigrateOnStartup=false`, `Security:DisableAuthorization=true`, `Otp:Mode=Demo`)
3. User Secrets (Development environment only; `UserSecretsId` in `NSG.Mobile.API.csproj`)
4. Environment variables (`__` stands for `:`)
5. Command-line arguments

On Windows, `scripts\run-local.ps1` puts values from `.env.local.secrets` into the process environment, so **they override User Secrets** for the same key. `.env.local.secrets` is therefore the single source of truth for shared local secrets. The User Secrets remain in place as a fallback (for example when the API is started from Visual Studio, which does not run the launcher) and must then hold the same values.

The app refuses to start with a missing or `REPLACE_*` JWT, encryption or blind-index value (`StartupSecretsValidator`). In Docker, `appsettings.Development.json` is **not** in the image (`.dockerignore`), so no value from it applies there; every Docker setting comes from the compose file and `.env.local.secrets`.

## 3. `.env.local.secrets`

- Git-ignored (`.gitignore`) and excluded from the Docker build context (`.dockerignore`). **Never commit it.** The committed template is `.env.local.secrets.example` (placeholders only).
- Created once from the template; the initial values for the keys that already existed were copied from User Secrets without being printed. **Existing `Encryption:Key` and `BlindIndex:Key` must never be regenerated**: encrypted columns and the PAN/salary-account blind indexes would become unreadable.
- Format: `NAME=value`, one per line, no quotes, no spaces around `=`. A password containing `$`, quotes, `#` or spaces would break Docker Compose interpolation; avoid them.
- Keys in the file:

| Key | Used by |
|---|---|
| `LOCAL_DB_USER`, `LOCAL_DB_PASSWORD`, `LOCAL_DB_NAME` | both (the launcher builds the connection string for `127.0.0.1`; compose builds it for `host.docker.internal`) |
| `Encryption__Key`, `BlindIndex__Key` | both, identical |
| `WINDOWS_Jwt__Secret` | Windows only (becomes `Jwt__Secret`) |
| `DOCKER_Jwt__Secret`, `DOCKER_Jwt__Issuer`, `DOCKER_Jwt__Audience`, `DOCKER_Jwt__AdminAudience` | Docker only (become `Jwt__*`) |

The launcher prints variable **names** only, never values. Do not paste the file, or output that contains its values, into tickets, chat or logs.

## 4. JWT configuration is separate per environment

A token issued by one environment must not be accepted by the other.

| | Windows | Docker |
|---|---|---|
| Secret | `WINDOWS_Jwt__Secret` | `DOCKER_Jwt__Secret` (different value) |
| Issuer | `NSG.Mobile.API.Dev` (`appsettings.Development.json`) | `NSG.Mobile.API.Docker` |
| Audience | `NSG.Mobile.App.Dev` (`appsettings.Development.json`) | `NSG.Mobile.App.Docker` |
| AdminAudience | default `<Audience>.Admin` | `NSG.Mobile.Admin.Docker` |

`Jwt:AdminAudience` must differ from `Jwt:Audience`; the app refuses to start otherwise. Users and refresh tokens live in the shared `nsg_mobile_dev` database, so after switching environments sign in again (an access token from the other environment is rejected by design).

## 5. Shared Encryption and BlindIndex keys

Both environments use the same database, so they must use byte-identical `Encryption__Key` and `BlindIndex__Key` (base64 of exactly 32 bytes). One file supplies both; do not set them anywhere else (User Secrets copies must match). The Docker image holds no key; compose injects it at run time.

## 6. Database and storage

- **Database:** `nsg_mobile_dev` on the MySQL server running on Windows. Docker reaches it through `host.docker.internal`; the MySQL account must be allowed to connect from the Docker network (a MySQL grant managed outside this repository). If Docker cannot connect, `/health/ready` is not 200 and the container log shows the MySQL error.
- **Migrations:** neither environment applies migrations at startup. Apply reviewed migrations separately and only with approval (`DATABASE-STANDARDS.md`).
- **Windows files:** `D:\NSGStorage` (must already exist and be writable; it is never created for you).
- **Docker files:** named volume `nsg_local_files` (Compose project `nsg-local`, volume name `nsg-local_nsg_local_files`) mounted at `/var/data/files`, owned by the non-root `app` user. Files survive `stop`, `down` and container re-creation. Only `down -v` or `docker volume rm` deletes them. The Windows and Docker file stores are **separate**: a file uploaded in one is not visible in the other (the database rows are shared, the files are not).
- Docker data-protection keys are stored inside the container and are lost when it is destroyed (the app logs a warning). Nothing local depends on them today.

## 7. Commands

All commands run from the repository root unless noted.

### Windows
```powershell
.\scripts\run-local.ps1                  # start (http profile, http://localhost:5062)
.\scripts\run-local.ps1 -Profile https   # https profile
.\scripts\run-local.ps1 -EnvFile D:\other\file   # explicit secrets file (default: <repo>\.env.local.secrets)
```
The default file is resolved from the script's own location, so the script can be started from any directory and works in Windows PowerShell 5.1 (verified) and PowerShell 7 (same code path; not run on the development machine, which has no `pwsh`).
- **Stop:** `Ctrl+C` in that window.
- **Restart:** stop, then run the script again.

### Docker
```powershell
docker compose --env-file .env.local.secrets -f docker-compose.local.yml up --build        # build and start (foreground, Ctrl+C stops)
docker compose --env-file .env.local.secrets -f docker-compose.local.yml up --build -d     # build and start in the background
docker compose --env-file .env.local.secrets -f docker-compose.local.yml stop              # stop, keep the container
docker compose --env-file .env.local.secrets -f docker-compose.local.yml start             # start the stopped container
docker compose --env-file .env.local.secrets -f docker-compose.local.yml restart           # restart (same configuration)
docker compose --env-file .env.local.secrets -f docker-compose.local.yml up --build -d --force-recreate   # apply changed environment values
docker compose --env-file .env.local.secrets -f docker-compose.local.yml down              # remove the container, KEEP uploaded files
docker compose --env-file .env.local.secrets -f docker-compose.local.yml logs -f api       # follow the log
```
`--env-file` is required: compose reads the `${...}` values from it. **Do not run `down -v`** unless you intend to delete the uploaded files in the volume. Run only one of Windows/Docker at a time against the same database to avoid surprises (they use different ports, so both can technically run).

## 8. Health verification

| Check | Windows | Docker |
|---|---|---|
| Liveness | `http://localhost:5062/health/live` | `http://localhost:8080/health/live` |
| Readiness (includes the MySQL check) | `http://localhost:5062/health/ready` | `http://localhost:8080/health/ready` |

```powershell
Invoke-WebRequest http://localhost:5062/health/ready -UseBasicParsing   # expect 200 Healthy
Invoke-WebRequest http://localhost:8080/health/ready -UseBasicParsing
```
`/health/live` only proves the process is up. **Database connectivity is confirmed only by `/health/ready` returning 200.** After startup also read the log: Windows logs `AUTHORIZATION IS DISABLED` (expected, Development settings); Docker must **not** log it. Both log `DEMO OTP MODE IS ACTIVE`.

## 9. `Database:MigrateOnStartup=false` locally

The committed `appsettings.json` has `Database:MigrateOnStartup=true` because the disposable Render development database relies on it (`DEPLOYMENT-RENDER.md`, OQ-034). Locally it is overridden to `false` in `appsettings.Development.json` (Windows) and in the compose file (Docker), so starting either environment never changes the `nsg_mobile_dev` schema. If the setting is missing the code treats it as off.

## 10. Render: unchanged

Render configuration is not touched by the local setup: its environment variables, database, encryption key and `Database:MigrateOnStartup=true` (from the committed `appsettings.json`) stay as they are. The `Dockerfile` is the same image Render builds (`ASPNETCORE_ENVIRONMENT=Production`); local Docker only overrides settings at run time through `docker-compose.local.yml`. `appsettings.Development.json` is not part of any image. See `DEPLOYMENT-RENDER.md`.

## 11. Future Ubuntu production (planned architecture; not implemented here)

- Dedicated production environment file and secrets, **unrelated** to `.env.local.secrets` and to the local keys, stored outside the application directory with restrictive permissions (readable only by the service account) and loaded by the service manager. Production secrets are never copied from, or into, the local files.
- Run **published .NET files** (`dotnet publish` output) under **Kestrel**, supervised by **systemd**, behind **Nginx** (TLS termination and reverse proxy; forwarded headers trusted only from the listed proxy).
- **No Docker and no Git source checkout** on the production server.
- `Database__MigrateOnStartup=false`; reviewed migration scripts are applied separately.
- Details and the existing Ubuntu runbook: `DEPLOYMENT-UBUNTU.md`; template `.env.example`. Anything not stated there remains an open decision, not something this document defines.
