# Deployment on Render.com (Docker, from GitHub)

Status (2026-10-15): `Dockerfile` and `.dockerignore` added. **Not yet built or run**: no Docker on the development machine, and no Render service exists yet. Open items are in `OPEN-QUESTIONS.md` (OQ-032).

## What the image does
- Multi-stage build: `sdk:8.0` restores and publishes `src/NSG.Mobile.API` (Release); `aspnet:8.0` runs it.
- Runs as the non-root `app` user. Writable paths: `/app/logs` (Serilog file sink) and `/var/data/files` (the document storage root).
- Listens on HTTP at `$PORT` (Render sets it, default `10000`), or `8080` when `PORT` is unset. Render terminates TLS in front of the container.
- Sets `ASPNETCORE_ENVIRONMENT=Production` and `Storage__RootPath=/var/data/files` (the common document storage root, ADR-027). Every secret comes from environment variables; `appsettings.Development.json` and any `.env` file are kept out of the image.
- Never runs migrations (unchanged rule). Apply reviewed migrations to the target database separately, with approval.

## Steps
1. Push the repository to GitHub (the folder is not a git repository yet: `git init`, commit, add the remote, push). Check that no secret is committed.
2. Render dashboard → **New → Web Service** → connect the GitHub repository. Language/Runtime: **Docker**. Dockerfile path `./Dockerfile`, build context `.` (defaults).
3. **Health Check Path:** `/health/live`.
4. **Environment variables** (Render → Environment). Generate keys as in `CONFIGURATION.md`. Never put them in the repository.

| Variable | Value |
|---|---|
| `ConnectionStrings__MySql` | MySQL 8 connection string (`SslMode=Required;AllowUserVariables=True`). Render has no managed MySQL: use an external MySQL 8 server reachable from Render. |
| `Jwt__Secret`, `Jwt__Issuer`, `Jwt__Audience` | as in `.env.example` |
| `Encryption__Key` | base64, exactly 32 bytes |
| `ForwardedHeaders__Enabled` | `true` |
| `ForwardedHeaders__KnownNetworks__0` | Render's internal proxy network, see below |
| `AllowedHosts` | the service host name, for example `nsg-mobile-api.onrender.com` (and any custom domain, `;`-separated) |
| `Redis__ConnectionString` | optional (Render Key Value), otherwise in-memory cache |
| `Otp__Mode` | leave unset (`Sms`). `Demo` refuses to start in Production by design. |

5. Render Free keeps uploaded documents on the container filesystem, which is **ephemeral**: they are lost on every deploy or restart. That is acceptable for development/testing only. To keep files across deploys, attach a **persistent disk** mounted at `/var/data`. `logs/` is also lost on every deploy; rely on Render's log stream.

## HTTPS and forwarded headers (required)
The container receives plain HTTP from Render's proxy. Without trusted forwarded headers, `RequireHttps` rejects requests and every client shares one rate-limit bucket (fail closed). The application trusts `X-Forwarded-For`/`X-Forwarded-Proto` only from listed proxies and never from arbitrary clients.

Render does not publish a fixed proxy address. To find it: deploy, send one request, and read `ClientIp` in the request log line; that is the proxy address. Set `ForwardedHeaders__KnownNetworks__0` to the narrowest network that contains it (for example `10.0.0.0/8` if it is a 10.x address) and redeploy. Then confirm the audit log records your real public IP and that `X-Forwarded-For: 1.2.3.4` sent by the client is not stored (smoke test step 5 in `DEPLOYMENT-UBUNTU.md`).

## Smoke test
Use `DEPLOYMENT-UBUNTU.md` section 7 against `https://<service>.onrender.com` (skip the nginx-specific checks).

## Automatic database migrations (opt-in)
**Scope: Render development deployment only.** `Database:MigrateOnStartup` is `true` in the committed `appsettings.json`, so the app applies pending EF Core migrations whenever it starts (`StartupDatabaseMigrator`), locally and on Render. The owner has confirmed the Render MySQL database is disposable. **Do not use this for any environment that holds real data (NIC production):** set `Database__MigrateOnStartup=false` there and apply reviewed scripts with a migrator account (`DEPLOYMENT-UBUNTU.md`, `DATABASE-STANDARDS.md` section F). To turn it off elsewhere set `Database__MigrateOnStartup=false` (Render → Environment, or `appsettings.Development.json` locally). If the setting is missing the code treats it as off.
- **Destructive migration in the chain:** `20261103075304_Rebuild_Users_Pis_Final_Schema` drops and recreates `users_pis`, discarding every personnel row. This is intentional and approved for the disposable Render database. It cannot be reverted (`Down` throws); it has a preflight that stops it before any drop, but MySQL DDL cannot be rolled back, so a later failure needs a database restore or recreation. After the migration personnel must be re-entered or imported (`PIS-MIGRATION-RUNBOOK.md`).
- The database user needs DDL rights (`CREATE`, `ALTER`, `INDEX`, `DROP`, `REFERENCES`) on the target schema.
- A failed migration stops the app, so the deploy fails visibly; read the Render log for the migration names and the error. Only migration names are logged.
- **Single instance only.** EF Core 8 has no migration lock on MySQL: with two or more instances starting together they can race. Scale out only after migrations are applied (or turn the setting off).
- Review each migration's SQL before pushing it (`dotnet ef migrations script`). It applies to whatever database `ConnectionStrings__MySql` points at, with no further prompt, and a migration that drops or renames data is not undone by redeploying an older image.
