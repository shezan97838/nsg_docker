# Deployment on Render.com (Docker, from GitHub)

Status (2026-10-15): `Dockerfile` and `.dockerignore` added. **Not yet built or run**: no Docker on the development machine, and no Render service exists yet. Open items are in `OPEN-QUESTIONS.md` (OQ-032).

## What the image does
- Multi-stage build: `sdk:8.0` restores and publishes `src/NSG.Mobile.API` (Release); `aspnet:8.0` runs it.
- Runs as the non-root `app` user. Writable paths: `/app/logs` (Serilog file sink) and `/var/data/files` (file storage).
- Listens on HTTP at `$PORT` (Render sets it, default `10000`), or `8080` when `PORT` is unset. Render terminates TLS in front of the container.
- Sets `ASPNETCORE_ENVIRONMENT=Production` and `FileStorage__BasePath=/var/data/files`. Every secret comes from environment variables; `appsettings.Development.json` and any `.env` file are kept out of the image.
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
| `Encryption__Key`, `BlindIndex__Key` | base64, different keys |
| `ForwardedHeaders__Enabled` | `true` |
| `ForwardedHeaders__KnownNetworks__0` | Render's internal proxy network, see below |
| `AllowedHosts` | the service host name, for example `nsg-mobile-api.onrender.com` (and any custom domain, `;`-separated) |
| `Redis__ConnectionString` | optional (Render Key Value), otherwise in-memory cache |
| `Otp__Mode` | leave unset (`Sms`). `Demo` refuses to start in Production by design. |

5. Optional: attach a **persistent disk** mounted at `/var/data` if uploaded files must survive deploys (the container filesystem is ephemeral; `logs/` is lost on every deploy, rely on Render's log stream).

## HTTPS and forwarded headers (required)
The container receives plain HTTP from Render's proxy. Without trusted forwarded headers, `RequireHttps` rejects requests and every client shares one rate-limit bucket (fail closed). The application trusts `X-Forwarded-For`/`X-Forwarded-Proto` only from listed proxies and never from arbitrary clients.

Render does not publish a fixed proxy address. To find it: deploy, send one request, and read `ClientIp` in the request log line; that is the proxy address. Set `ForwardedHeaders__KnownNetworks__0` to the narrowest network that contains it (for example `10.0.0.0/8` if it is a 10.x address) and redeploy. Then confirm the audit log records your real public IP and that `X-Forwarded-For: 1.2.3.4` sent by the client is not stored (smoke test step 5 in `DEPLOYMENT-UBUNTU.md`).

## Smoke test
Use `DEPLOYMENT-UBUNTU.md` section 7 against `https://<service>.onrender.com` (skip the nginx-specific checks).

## Automatic database migrations (opt-in)
`Database:MigrateOnStartup` is `true` in `appsettings.json`, so the app applies pending EF Core migrations whenever it starts (`StartupDatabaseMigrator`), locally and on Render. To turn it off for an environment set `Database__MigrateOnStartup=false` in Render → Environment (or in `appsettings.Development.json` locally). If the setting is missing the code treats it as off.
- The database user needs DDL rights (`CREATE`, `ALTER`, `INDEX`, `DROP`, `REFERENCES`) on the target schema.
- A failed migration stops the app, so the deploy fails visibly; read the Render log for the migration names and the error. Only migration names are logged.
- **Single instance only.** EF Core 8 has no migration lock on MySQL: with two or more instances starting together they can race. Scale out only after migrations are applied (or turn the setting off).
- Review each migration's SQL before pushing it (`dotnet ef migrations script`). It applies to whatever database `ConnectionStrings__MySql` points at, with no further prompt, and a migration that drops or renames data is not undone by redeploying an older image.
