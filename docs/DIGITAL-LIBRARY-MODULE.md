# Digital Library module

Official circulars, notices, PDFs and images. Media Cell admins upload them; signed-in mobile users list and open them.
Built 2026-11-06. Migration `20261106090213_Add_Digital_Library` (applied to the local `nsg_mobile_dev` only).

## Authorization

| Side | Route prefix | Policy |
|---|---|---|
| Admin | `/api/v1/admin/digital-library` | `RequireMediaCellAdmin` (role `MEDIA_CELL_ADMIN`, Admin Portal token) - the same policy DG Messages and Banners are meant to use. No new role or policy. |
| Mobile | `/api/v1/digital-library` | `RequireMobileProfile` (mobile access token) |

Note: the DG Messages admin endpoints currently carry a TEMPORARY `[AllowAnonymous]` (see `DgMessagesController`). The Digital Library does **not** copy that; its admin controller is enforced.

## Files

* Accepted: **PDF, JPG, JPEG, PNG** only. The extension picks the expected type, and the file's first bytes must match (`%PDF-`, `FF D8 FF`, PNG 8-byte signature). The client's Content-Type and file name are never trusted; everything else (DOC, XLS, ZIP, MP4, MP3, executables, a `.pdf` that is really a PNG) is a 400.
* **Maximum size: 10 MB** (`DigitalLibrary:MaxFileSizeBytes`, default 10485760; the same ceiling as Form 16 PDFs). The request ceiling is 11 MB and cannot be raised by the setting. Empty files are refused.
* Location: the `DigitalLibrary` folder of the single common storage root `Storage:RootPath` (`D:\NSGStorage\DigitalLibrary\yyyy\MM\{guid}.{ext}` in development). There is no module-specific root setting. `digital_library.file_path` holds only the relative `yyyy/MM/{guid}.{ext}`.
* Writes are staged in `_incoming` and renamed into place (never overwriting). If the database save fails the new file is removed; on a replacement the old file stays until the new row is saved and is then deleted.
* **Like Form 16, this module uses the local file system under `Storage:RootPath`** (`DigitalLibrary/`), as every module does since ADR-027. `Storage:RootPath` is mandatory in every environment, so the endpoints can always store and serve files; on Render Free the files are ephemeral.

## Database

Table `digital_library`: `id` bigint PK, `description` varchar(1000), `file_path` varchar(500), `file_type` varchar(10) (`PDF|JPG|JPEG|PNG`, CHECK), `is_important` tinyint(1) default 0, `is_active` tinyint(1) default 1, `created_at_ist` datetime(6), `created_by` bigint, `updated_at_ist` datetime(6) null, `updated_by` bigint null.
`created_by` / `updated_by` are `admin_profiles.id` from the token (no foreign key, same as Form 16).

Indexes: `ix_digital_library_is_active_created_at_ist (is_active, created_at_ist)` and `ix_digital_library_is_active_is_important_created_at_ist (is_active, is_important, created_at_ist)`. InnoDB appends the primary key to each, which serves the `created_at DESC, id DESC` order.

**Timestamps are IST, not UTC.** The task text asked for `created_at_utc`; the project rule (CLAUDE.md "Timestamp Convention", `DatabaseNamingRuleTests`) requires `*_ist` columns, so the columns are `created_at_ist` / `updated_at_ist` and the API returns `createdAt` / `updatedAt` as `DateTimeOffset` with `+05:30`. The mobile field is therefore `createdAt`, not `createdAtUtc`.

## API

All responses use the global envelope `{ success, message, data }`. Empty listings answer 200 with a message ("No digital library records found.").

### Admin

| Method | Route | Notes |
|---|---|---|
| GET | `/api/v1/admin/digital-library` | `search`, `isActive`, `isImportant`, `fileType`, `pageNumber` (1), `pageSize` (20, max 100). Active **and** inactive, newest first. |
| GET | `/api/v1/admin/digital-library/{id}` | 404 if missing; inactive records are returned. |
| POST | `/api/v1/admin/digital-library` | multipart: `description` (required, <= 1000), `isImportant` (optional, false), `file` (required). 201. Created active. |
| PUT | `/api/v1/admin/digital-library/{id}` | multipart: `description`, `isImportant` (both required), `file` (optional replacement). Never reactivates. |
| DELETE | `/api/v1/admin/digital-library/{id}` | **Soft delete**: `is_active = 0`, `updated_*` set. Row and file are kept. Repeating answers 200 "already inactive". There is no physical delete and no reactivation endpoint. |

### Mobile

| Method | Route | Notes |
|---|---|---|
| GET | `/api/v1/digital-library` | `pageNumber`, `pageSize` (max 100), optional `isImportant`. Active only, `created_at DESC, id DESC`. Items: `id, description, isImportant, fileType, fileUrl, createdAt`. |
| GET | `/api/v1/digital-library/{id}/file` | Streams the file with the right Content-Type (`application/pdf`, `image/jpeg`, `image/png`). 404 for missing, inactive or unavailable. `Cache-Control: private, no-store`. |

`fileUrl` is the relative route `/api/v1/digital-library/{id}/file`; call it with the same bearer token.

## Tests

`tests/NSG.Mobile.Tests/DigitalLibraryTests.cs` (controllers + real services + real storage in a temp folder + in-memory repository). Not covered without MySQL (`NSG_TEST_MYSQL`): the SQL of `DigitalLibraryRepository` and the JWT role check on the live pipeline.
