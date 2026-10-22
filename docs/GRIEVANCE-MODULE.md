# Grievance Management System (GMS) — SOW §3.8

Module guide: setup, API reference, Flutter contract, tests and open points. Written 2026-10-22; **audited and corrected the same day** (see section 11 for what changed).

## 0. Status — read this first

| Area | State |
|---|---|
| Code (Domain, Application, Infrastructure, API) | Complete. Builds with 0 warnings and 0 errors. |
| Rules and workflows | Covered by automated tests that use **fakes only** (`GrvFakeRepository`, `GrvFakeStorage`): transitions, remarks, comments, forwarding, isolation between users (404), magic bytes, size/count limits, all-or-nothing storage, orphan clean-up, audit rows, response codes and headers. |
| **Repository SQL on real MySQL** | **Not smoke-tested.** Queries were translated to SQL without a connection; none was executed against a database. |
| **Migration `20261022103033_Add_Grievance_Management`** | Generated; **not applied anywhere and its SQL not yet reviewed against a database.** It replaces the earlier, never-applied `20261022052101_Add_Grievance_Management` (regenerated on request, see section 11). |
| **Real R2** | **Not smoke-tested.** `R2FileStorageService` has only been exercised through its pure parts. |
| **Upload limits in Kestrel** | The 400 for an oversized file is tested. The path for a request body over the 16 MB cap (HTTP 413 turned into a 400 by a filter) is tested only with synthetic exceptions. |
| **Authorization with real tokens** | Anonymous, wrong-audience and forged tokens are tested through the real pipeline (401). Genuine sessions need a database; a wrong-role admin (403) is untested. |

Smoke-test checklist: section 9. Run later against a local Docker MySQL and a development bucket, never against a shared database or the production bucket.

## 1. What it does

An employee raises a grievance in a category with a description and optional PDF/JPEG attachments, and follows its history. A Grievance Admin reviews it, comments, forwards it to another Grievance Admin, takes action and resolves it.

| Actor | Token | Policy | Sees |
|---|---|---|---|
| Employee (any mobile role) | Mobile access token | `RequireMobileProfile` | Only their own grievances. Someone else's is a 404 (never 403). |
| Grievance Admin | Admin Portal token, role `GRIEVANCE_ADMIN` | `RequireGrievanceAdmin` | All grievances, with the employee's NSG number, name and unit. |

The user is always taken from the token (`profile_id` / `admin_profile_id`); no request body carries a user id.

### Status workflow
| Value | Name | Set by |
|---|---|---|
| 1 | Submitted | the system, on creation |
| 2 | Under Review | admin ("take action": start review) |
| 3 | Action Taken | admin (remarks **required**) |
| 4 | Closed = **resolved** | admin (remarks **required**: the resolution) |

Only 1→2, 2→3, 3→4 are allowed. A simultaneous change by another admin is a 409 naming the current status.

### History (append-only)
Every action appends one `grievance_status_log` row in the same transaction as the change; rows are never updated or deleted. Each row records the kind of action, the status (from/to), the remarks or comment, the actor (type and id, from the token) and the IST time.

| `action` | Name | Written by | Status |
|---|---|---|---|
| 1 | Status change | creation (employee), `PUT .../status` (admin) | changes |
| 2 | Comment | `POST .../comments` | unchanged |
| 3 | Forwarded | `POST .../forward` (also sets `assigned_to`) | unchanged |

Comments and forwards are refused (409) on a closed grievance. The employee sees the whole history (action, remarks, actor type, date) but **not** admin ids or who it was forwarded to; admins see both.

### Documents (attachments)
- Attached to the **single** creation call (`POST /grievances`, multipart). There is no separate upload endpoint and documents cannot be added later.
- PDF and JPEG only (`.pdf`, `.jpg`, `.jpeg`). Extension, declared content type and the file's first bytes (`%PDF`, `FF D8 FF`) must all agree. Fixed limits for every category: **at most 3 files, at most 5 MB each** (`GrievanceLimits`). Everything is checked **before** anything is sent to storage; one bad file refuses the whole submission.
- All files are stored first, then the grievance row (with the storage keys) is written; if that fails, every stored object is deleted again.
- The database keeps, per document, `{ key, name, contentType, sizeBytes, uploadedAtIst }` in `grievance.document_urls` (JSON). `key` is an object key in the private bucket, never a URL, and is never returned to clients. `name` is the sanitised original name and is the download name.
- Downloads stream through the API (owner: own grievance only; admin: any). Headers: `Content-Disposition: attachment`, `X-Content-Type-Options: nosniff`, `Cache-Control: private, no-store`, the stored validated content type. No public or pre-signed URL exists. Every download writes an audit row (ids only).

### Routing
There is no automatic routing. A new grievance starts **unassigned** (`assigned_to` null) and an admin assigns it with `POST /admin/grievances/{id}/forward`, which validates that the target is an existing, enabled, activated `GRIEVANCE_ADMIN` (OQ-011).

### Audit
`IAuditService` rows, ids only (no names, notes, comments or file names): `GrievanceCreated`, `GrievanceStatusChanged`, `GrievanceDocumentUploaded`, `GrievanceCommentAdded`, `GrievanceForwarded`, `GrievanceCategoryChanged` (created / updated / deleted), `GrievanceViewed`, `DocumentDownloaded`.

## 2. Data
One migration creates three tables (lowercase snake_case, IST `*_ist` timestamps, `Restrict` foreign keys):

| Table | Purpose |
|---|---|
| `grievance_category` | `grievance_name` (unique, also among inactive and deleted), `department_id` (nullable, unused), `is_active`, `is_deleted`, `created_at_ist`, `created_by`, `updated_at_ist`, `updated_by` (`created_by`/`updated_by` = `admin_profiles.id`, no foreign key). **Soft delete only:** rows are never removed. No metadata, display order or SLA. |
| `grievance` | `grievance_id` is the ticket `GRV-YYYY-000001` (unique); `current_status` 1-4; `document_urls` JSON; `assigned_to`, `action_taken_*`, `closed_*`; soft delete by `is_active`. Check constraints tie status 3/4 to their action/closing fields. |
| `grievance_status_log` | Append-only history: `log_action` (1 status change, 2 comment, 3 forward), `forwarded_to`, from/to status, `remarks`, `changed_by` with `changed_by_type` (1 employee = `mobile_user_profiles.id`, 2 admin = `admin_profiles.id`), `changed_at_ist`. |

Timestamps are IST (`*_ist`), per the project-wide rule in `CLAUDE.md`; they are exposed with the fixed `+05:30` offset. (The audit brief asked for UTC names such as `CreatedAtUtc`; IST was kept on the owner's decision.)

Ticket numbers are allocated inside the create transaction against the unique index and retried at most 3 times on a collision (then 503 `TICKET_UNAVAILABLE`).

**MySQL version:** MySQL 8.0 (verified on 8.0.44). Check constraints are enforced from 8.0.16; on older MySQL or MariaDB they would be silently ignored (the services enforce the same rules).

## 3. Setup

### 3.1 Cloudflare R2 bucket
1. Create a bucket (for example `nsg-grievances-dev`). Keep it **private**: no public access, no `r2.dev` URL, no custom domain. The API is the only reader and writer.
2. Create an **R2 API token** scoped to **this bucket only**, permission **Object Read & Write**. Do not use an account-wide or admin token.
3. Note the account id: the service URL is `https://<ACCOUNT_ID>.r2.cloudflarestorage.com` (no bucket name in it).
4. No bucket CORS rules are needed (the mobile app never talks to R2). Region is `auto`.
5. Use a separate bucket (or at least a separate `KeyPrefix`) for development, staging and production.

### 3.2 Settings
| Variable | Secret | Meaning |
|---|---|---|
| `R2Storage__ServiceUrl` | no | `https://<ACCOUNT_ID>.r2.cloudflarestorage.com`. **Setting this switches storage to R2.** The committed placeholder `REPLACE_WITH_R2_SERVICE_URL` means "off". |
| `R2Storage__BucketName` | no | Bucket name. |
| `R2Storage__AccessKey` | **yes** | Access key id of the bucket-scoped token. |
| `R2Storage__SecretKey` | **yes** | Secret of the token. |
| `R2Storage__KeyPrefix` | no | Optional folder in front of every object (for example `prod`); not part of the stored keys. |
| `RateLimiting__GrievanceWritePermitLimit` | no | Creations per client IP per minute; default 10. |
| `Database__MigrateOnStartup` | no | See 3.4. |
| `FileStorage__BasePath` | no | Still required outside Development (3.5). |

AccessKey and SecretKey are read only from user-secrets or the environment; `appsettings.json` holds empty strings. They are never logged. With R2 selected, a missing bucket/key/secret, or a non-https URL, **stops the application at startup** with a message naming the setting (never the value).

Local development (User Secrets, project `src/NSG.Mobile.API`):
```
dotnet user-secrets set "R2Storage:ServiceUrl"  "https://<ACCOUNT_ID>.r2.cloudflarestorage.com" --project src/NSG.Mobile.API
dotnet user-secrets set "R2Storage:BucketName"  "nsg-grievances-dev"  --project src/NSG.Mobile.API
dotnet user-secrets set "R2Storage:AccessKey"   "<access key id>"      --project src/NSG.Mobile.API
dotnet user-secrets set "R2Storage:SecretKey"   "<secret>"             --project src/NSG.Mobile.API
```
Servers: set the same names as environment variables (`R2Storage__AccessKey`, ...). `.env.example` lists them.

### 3.3 Which storage is used where
| Environment | R2 configured | Grievance upload/download |
|---|---|---|
| Development | no | Works on local disk (`FileStorage:BasePath`, `App_Data/filestorage`). |
| Development | yes | Uses R2. |
| Anything else (Staging, Production) | yes | Uses R2. |
| Anything else | **no** | **503 `STORAGE_UNAVAILABLE`.** Local disk is never used. |

### 3.4 Migration and `MigrateOnStartup` — warning
`Database:MigrateOnStartup` is `true` in the committed `appsettings.json`. **The first start of the application against any database applies the pending migration to it, without a prompt.** Therefore:
- Set `Database__MigrateOnStartup=false` in your local environment until the SQL has been reviewed and tried on a throw-away database (for example a local Docker MySQL 8.0).
- Review the SQL: `dotnet ef migrations script 20261018093707_Add_Admin_Refresh_Tokens 20261022103033_Add_Grievance_Management --project src/NSG.Mobile.Infrastructure --startup-project src/NSG.Mobile.API` (set `ConnectionStrings__MySql` to a local dummy first, and `Database__MigrateOnStartup=false`).
- `Down` drops `grievance_status_log`, then `grievance`, then `grievance_category`.
- **If the earlier migration `20261022052101_Add_Grievance_Management` was ever applied to any database,** that database must be reset or hand-corrected first; this one replaces it and does not migrate its columns.
- Do not apply to a shared or production database without approval (OQ-034).

### 3.5 `FileStorage__BasePath` is still required
`Program.cs` refuses to start outside Development unless `FileStorage:BasePath` is an absolute path, **even when R2 is the active provider** (R2 does not use it). Set it (for example `/var/lib/nsg/files`) in Staging/Production. This is existing behaviour that was not changed.

### 3.6 Optional category seed
The migration seeds nothing. `tools/sql/seed-grievance-categories.sql` inserts Personal, Medical, Administration, Civil Dispute and Other (idempotent by name). Run it by hand on a development database after the migration.

### 3.7 Rate limiting
`POST /api/v1/grievances` (the only creation call, with its attachments) uses the named policy `grievance-write` (default 10 per minute **per client IP**; the only key available at that point in the pipeline, see section 10). Everything also falls under the global 100 per minute per IP. The IP is only the real client address when `ForwardedHeaders__Enabled=true` with the proxy listed (`DEPLOYMENT-RENDER.md`); otherwise every client behind the proxy shares one bucket. A rejected request is a bare HTTP 429 with no body (existing limiter behaviour).


## 4. API reference

Base path `/api/v1`. Timestamps are IST with a fixed `+05:30` offset. `$BASE`, `$MOBILE` (employee token) and `$ADMIN` (admin token) are placeholders; tokens come from `POST /mobile-auth/login` and `POST /admin/auth/login` + `/admin/auth/login/verify`.

**Every JSON response uses the global envelope** `{ "success": true|false, "message": "...", "data": ... }`. It is applied centrally by `ApiResponseEnvelopeFilter` (controller results) and `GlobalExceptionHandlingMiddleware` (exceptions and bare 401/403/404/429), never by the controllers. `data` is null on every error and when there is nothing to return. A paged list is `data: { items, pageNumber, pageSize, totalCount, totalPages, hasPreviousPage, hasNextPage }`. File downloads are the only exception (raw bytes). See `.claude/rules/project-rules.md`.

### 4.1 Employee (`RequireMobileProfile`)

| Call | Purpose |
|---|---|
| `GET /grievance-categories` | Dropdown: active, non-deleted categories by name. `data: [ { "id": 2, "name": "Medical" } ]`. None: message "No grievance categories found.", `data: null`. |
| `POST /grievances` | **The only creation call.** `multipart/form-data`, rate limited. Fields: `categoryId`, `subject`, `note` (the description, 10-2000 characters), optional repeated `files` (max 3, PDF/JPEG, max 5 MB each). `201`, `Location: /api/v1/grievances/{id}`. |
| `GET /grievances?pageNumber=&pageSize=&status=` | My grievances, newest first (`status` optional 1-4). None: message "No grievances found.". |
| `GET /grievances/{id}` | One of mine with documents and the **complete history**. 404 when it is not mine. |
| `GET /grievances/{id}/documents/{index}` | Download a document of mine (zero-based index). |

```
curl -X POST $BASE/api/v1/grievances -H "Authorization: Bearer $MOBILE" \
  -F categoryId=2 -F "subject=Medical reimbursement pending" -F "note=My claim from August has not been processed yet." \
  -F "files=@report.pdf;type=application/pdf" -F "files=@scan.jpg;type=image/jpeg"
```
```json
{ "success": true, "message": "Grievance submitted successfully. Your ticket number is GRV-2026-000001.",
  "data": { "id": 101, "grievanceId": "GRV-2026-000001", "categoryId": 2, "categoryName": "Medical",
            "subject": "Medical reimbursement pending", "status": 1, "statusName": "Submitted", "documentCount": 2,
            "createdAt": "2026-10-22T11:30:00+05:30",
            "note": "My claim from August has not been processed yet.",
            "documents": [ { "index": 0, "name": "report.pdf", "contentType": "application/pdf", "sizeBytes": 183422, "uploadedAt": "2026-10-22T11:30:00+05:30" },
                           { "index": 1, "name": "scan.jpg", "contentType": "image/jpeg", "sizeBytes": 90311, "uploadedAt": "2026-10-22T11:30:00+05:30" } ],
            "timeline": [ { "action": 1, "actionName": "Status change", "actorType": "Employee", "fromStatus": null, "fromStatusName": null,
                            "toStatus": 1, "toStatusName": "Submitted", "remarks": null, "changedAt": "2026-10-22T11:30:00+05:30" } ],
            "actionRemarks": null, "actionTakenAt": null, "closedRemarks": null, "closedAt": null } }
```
`id` is the numeric key used in URLs; `grievanceId` is the ticket number to show to people. In the detail, `timeline` also holds the admin comments (`actionName` "Comment"), forwards ("Forwarded", date and remarks) and status changes; `actionRemarks`/`actionTakenAt` and `closedRemarks`/`closedAt` are the resolution information.

### 4.2 Grievance Admin (`RequireGrievanceAdmin`)

| Call | Purpose |
|---|---|
| `GET /admin/grievances?pageNumber=&pageSize=&status=&categoryId=&fromDate=&toDate=&search=&assignedTo=` | All grievances, newest first. `fromDate`/`toDate` inclusive IST days; `search` a ticket-number fragment. Rows carry `createdBy`, `assignedTo` and `employee` (NSG number, name, unit). |
| `GET /admin/grievances/{id}` | Detail (audited `GrievanceViewed`): adds the history with `changedBy` and `forwardedTo`. |
| `GET /admin/grievances/{id}/documents/{index}` | Download any document (audited). |
| `PUT /admin/grievances/{id}/status` | Take action / resolve: `{ "toStatus": 2\|3\|4, "remarks": "..." }`. Remarks required for 3 and 4 (4 = resolved). |
| `POST /admin/grievances/{id}/comments` | `{ "comment": "..." }`. Appended to the history, visible to the employee; status unchanged. 409 when closed. |
| `POST /admin/grievances/{id}/forward` | `{ "toAdminId": 7, "remarks": "optional" }`. Assigns/reassigns to an active Grievance Admin; status unchanged. 409 when closed or already assigned to that admin. |
| `GET /admin/dashboard/summary?fromDate=&toDate=` | Counts by status, category and month, average resolution days. |
| `GET /admin/grievance-categories?pageNumber=&pageSize=&isActive=&search=` | Categories, active and inactive, **never deleted ones**. |
| `GET /admin/grievance-categories/{id}` | One category (404 when deleted). |
| `POST /admin/grievance-categories` | `{ "name": "Medical", "departmentId": null, "isActive": true }`; `isActive` defaults to true. `201`. A name that exists (even inactive or deleted) is 409. |
| `PUT /admin/grievance-categories/{id}` | Replaces `name`, `departmentId`, `isActive` (`isActive` required). |
| `DELETE /admin/grievance-categories/{id}` | **Soft delete** (`is_deleted`, also deactivates). `200` with message; repeating answers "already deleted". Existing grievances keep their category. |

Category response: `id`, `name`, `departmentId`, `isActive`, `isDeleted`, `createdAt`, `createdBy`, `updatedAt`, `updatedBy`.

## 5. Errors
Errors use the envelope with `success: false`, a client-safe `message` and `data: null`, and the matching HTTP status. The RFC 7807 `title`/`traceId` fields and the machine error codes are no longer in the body (the correlation id is the `X-Correlation-Id` header).

| HTTP | When |
|---|---|
| 400 | Bad input, unavailable category, short description, wrong file type/bytes/content type, over a size or count limit (the message states the limit), invalid status move, missing remarks, invalid forward target, request body over the 16 MB cap |
| 401 | No/invalid token, or a token of the wrong kind (see section 10) |
| 403 | An authenticated admin without the Grievance Admin role |
| 404 | Unknown grievance/category/document, **or a grievance that is not yours** |
| 409 | Status changed meanwhile; comment/forward on a closed grievance; already assigned to that admin; category name exists |
| 429 | Rate limit |
| 503 | R2 unreachable or no private storage in this environment; or no ticket number could be allocated after 3 attempts |

## 6. Flutter contract notes
- **Ticket vs id:** show `grievanceId` to people; use `id` (number) in URLs.
- **Status** is an integer 1-4: 1 Submitted, 2 Under Review, 3 Action Taken, 4 Closed (resolved). Pending = 1-3.
- **Create is multipart only** (a JSON body is no longer accepted): fields `categoryId`, `subject`, `note`, and repeated `files`. Each file needs a correct part `Content-Type` (`application/pdf` or `image/jpeg`) and a real PDF/JPEG body. Max 3 files of 5 MB. Attachments cannot be added later.
- **History:** `timeline[]` entries have `action` (1 status change, 2 comment, 3 forwarded), `actionName`, `actorType` (Employee/Admin), `remarks` (the comment) and `changedAt`.
- **Document index** is zero-based, from `documents[].index`. Files are only retrievable through the authenticated download call.
- **Empty lists:** the envelope has a message such as "No grievances found." and `data: null` (not an empty array). Check `data` before reading `items`.
- **Errors:** branch on the HTTP status, show `message`. Retry later on 503 and 429; on 409 reload and try again.
- **No push notifications yet** (OQ-036).

## 7. Tests
`tests/NSG.Mobile.Tests` — `GrievanceStatusTests`, `GrievanceEmployeeTests`, `GrievanceUploadTests` (attachments in the creation call), `GrievanceCategoryTests`, `GrievanceAdminTests` (including comments and forwarding), `GrievanceControllerTests`, `GrievanceStorageTests`, `GrievanceRouteSecurityTests` (fakes in `GrievanceFakes.cs`; the route-security tests run the real pipeline with a dummy connection). `ApiResponseEnvelopeTests` covers the envelope. The schema pin `ExpectedDatabaseSchema.cs` includes the three tables.

Not covered by any automated test: real SQL and MySQL semantics (unique-index races, JSON columns, group-by queries), real R2, Kestrel's request-size behaviour, genuine session tokens, a wrong-role admin (403).

## 8. Files
Domain: `Entities/Grievance.cs`, `Enums/GrievanceStatus.cs` (status, log action, actor type). Application: `Grievances/**`, `Interfaces/Repositories/IGrievanceRepository.cs`, `Interfaces/Infrastructure/IStorageAvailability.cs`. Infrastructure: `Persistence/Configurations/GrievanceConfigurations.cs`, `Repositories/GrievanceRepository.cs`, `Services/R2FileStorageService.cs`, `Migrations/*_Add_Grievance_Management*`. API: `Controllers/GrievancesController.cs`, `Controllers/GrievanceAdminController.cs`, `Filters/ApiResponse*`.

## 9. Manual smoke-test checklist (later, local Docker MySQL 8.0 + a development bucket)
Not done yet. Do not use a shared database or the production bucket.
1. Review the migration SQL; apply to the Docker database; run `tests/NSG.Mobile.Tests` with `NSG_TEST_MYSQL` pointing at a `*_test` database; apply the seed script twice (second run inserts nothing).
2. Create: two simultaneous creates get different ticket numbers; ticket numbers continue after a restart; the year prefix follows IST.
3. Create with R2: PDF and JPEG succeed and appear under `grievances/yyyy/MM/<guid>.<ext>`; wrong bytes, wrong content type, `.png`, over-limit size and a fourth file are 400; a 20 MB body is a 400 (not a 500 or a reset connection); with `KeyPrefix` set, objects sit under it.
4. Orphans: force the database write to fail after the objects are stored and confirm every object is deleted.
5. Download: headers as in section 1; the sanitised name is saved; `Range` is ignored; a deleted object is a 404 with the "no longer available" message.
6. Status/comment/forward: two admins acting on the same grievance at once (one 200, one 409 for status); comments and forwards appear in the employee's history in order; closed grievances refuse both.
7. Category soft delete: a deleted category vanishes from both dropdown and admin list, old grievances still show its name, its name cannot be reused.
8. Dashboard and employee join queries return correct numbers on realistic data (monthly grouping near month boundaries in IST, grievances whose personnel record is missing).
9. Security with real tokens: employee token on admin routes and admin token on employee routes (expect 401); an admin with another role (expect 403); another employee's grievance and document (expect 404); a revoked session.
10. Rate limit: the 11th create within a minute from one IP is 429.
11. Environments: Production without `R2Storage__ServiceUrl` answers 503 for a submission with files and for downloads, and still starts (with `FileStorage__BasePath` set).

## 10. Known limitations and open points
See `OPEN-QUESTIONS.md` OQ-011 and OQ-035 to OQ-038. In short:
- **Rate limit key is the client IP.** The limiter runs before authentication; offices behind one NAT share a bucket.
- **Wrong token kind is 401, not 403** (separate schemes and audiences, ADR-017).
- No virus/malware scanning of uploads; PDFs are served as attachments with `nosniff`.
- No push notifications; no way to remove, replace or add a document after creation; no re-opening of a closed grievance; no automatic routing.
- Admins can see employee name, NSG number and unit; the audit log records each view.
- Attachments are stored as JSON on the grievance row (`document_urls`), not in a table of their own.

## 11. Audit 2026-10-22 — what changed
- **One creation call:** `POST /grievances` is multipart and takes the attachments; `POST /grievances/{id}/documents` was removed (no clients existed: the module was never deployed).
- **Categories:** metadata, display order, SLA, nodal officer and hint removed; `is_deleted`, `created_by`, `updated_by` added; `DELETE` (soft) added; `PUT .../activate|deactivate` removed (use `isActive` on `PUT`). The employee dropdown shows `id` and `name` only; limits are fixed (3 files, 5 MB, 10-character minimum description).
- **Admin:** `POST .../comments` and `POST .../forward` added; resolving (status 4) now requires remarks; the dashboard lost the SLA count.
- **History:** `grievance_status_log` gained `log_action` and `forwarded_to`; the employee detail returns the complete history.
- **Security:** `[Authorize(RequireMobileProfile)]` on `GrievancesController` had been commented out and `[AllowAnonymous]` set on the employee download; both were restored.
- **Migration:** the never-applied migration was regenerated as `20261022103033_Add_Grievance_Management`.
