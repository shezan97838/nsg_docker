# Leave Management module

Implemented 2026-10-26. Decisions: `architecture/ADR-019-leave-management.md`; open questions: OQ-040 to OQ-042 in `OPEN-QUESTIONS.md`.
**Status: implemented and fake-tested. Migration `20261026164053_Add_Leave_Management` is APPLIED to the local development database `nsg_mobile_dev` (127.0.0.1:3306) only; no other database has it. Migration application is confirmed by MySQL; functional behaviour on MySQL (repository SQL, rollback, concurrency, upload lock) is NOT yet verified** (see section 8).

This is **not** a leave application, approval, attendance or balance system. Leave Admins maintain one number per person per calendar year:
`leave_taken`, the authoritative **total** (not an increment) of leave days taken in that year. Mobile users can only read their own current-year number.

## 1. Who can do what

| Caller | Token | Policy | Can |
|---|---|---|---|
| Leave Admin | Admin Portal (`AdminBearer`), role `LEAVE_ADMIN` | `RequireLeaveAdmin` | create/update records, bulk upload, read records, history, batches, error reports |
| Any other admin role, **including Super Admin** | Admin Portal | not admitted | 403 on every Leave Admin route |
| Mobile user | Mobile (`RequireMobileProfile`) | `RequireMobileProfile` | `GET /api/v1/leave/my-record` only |

`LEAVE_ADMIN` is a seventh fixed role (id 7, seeded by the migration). A Super Admin provisions Leave Admin accounts through the existing `POST /api/v1/admin/accounts` with `roleCode: "LEAVE_ADMIN"`. The role was also added to the `RequireAdminAccount` policy (any active admin), so a Leave Admin can use `auth/me` and `logout` like every other role.

The acting admin always comes from the token (`ICurrentUserService.AdminProfileId`), the person for the mobile endpoint from the token's mobile profile. **The year is never accepted from a client.**

## 2. Rules that hold everywhere

- **Current year** = the calendar year of `IndianStandardTime.Now` (IST), computed on the server. 2026-12-31 20:00 UTC is already 2027 here.
- **Final total, not an increment:** changing 30 to 35 stores 35.
- **NSG number** must belong to an **active** row of `users_pis` (matched ignoring case; the stored spelling is what is saved). Inactive or unknown people are refused.
- **Leave taken:** a whole number, 0 to 366 (OQ-040).
- **Previous years** are never changed or deleted; a new year simply gets its own row.
- **Writing the stored value again changes nothing:** no new `updated_*`, no history row.
- **Audit fields:** on creation `created_by = updated_by` and `created_at_ist = updated_at_ist`; on updates only `updated_by`, `updated_at_ist` and `row_version` change.
- **History:** every actual change of a count writes one `personnel_leave_history` row (old, new, actor, time, `manual`/`bulk`, batch id) in the same transaction as the change.
- All timestamps are IST (`*_ist` columns, `+05:30` in responses). Responses use the global `{ success, message, data }` envelope.

## 3. Endpoints

All admin routes: `Authorization: Bearer <admin access token>`, role `LEAVE_ADMIN`.

| Method and route | Purpose | Success | Errors |
|---|---|---|---|
| `POST /api/v1/admin/leave-records` body `{ "nsgNumber": "...", "leaveTaken": 12 }` | Set the current-year total; **creates (201) or replaces (200)** | 201/200 + record | 400 invalid, 404 person not found, 409 lost race |
| `PUT /api/v1/admin/leave-records/{nsgNumber}` body `{ "leaveTaken": 15 }` | Replace the total of an **existing** current-year record | 200 + record | 404 person or record not found |
| `GET /api/v1/admin/leave-records/{nsgNumber}` | Current-year record | 200 + record | 404 with a message |
| `GET /api/v1/admin/leave-records/{nsgNumber}/history?pageNumber&pageSize` | Every change, newest first | paged list, or a message when none | 404 person |
| `POST /api/v1/admin/leave-uploads` multipart field `file` (.xlsx) | Bulk upload | 200 + batch summary | 400 validation, 409 another upload running, 500 processing failed / outcome unknown |
| `GET /api/v1/admin/leave-uploads?status=` | Batches, newest first | paged list, or a message | |
| `GET /api/v1/admin/leave-uploads/{batchId}` | One batch (status, counters) | 200 | 404 |
| `GET /api/v1/admin/leave-uploads/{batchId}/errors` | Row-level report | paged list, or a message | 404 |
| `GET /api/v1/leave/my-record` (**mobile token**) | Your current-year leave | 200 | 404 when nothing entered yet, 401 |

Example (create):

```json
POST /api/v1/admin/leave-records   { "nsgNumber": "NSG1001", "leaveTaken": 30 }

201 { "success": true, "message": "Leave record created successfully.",
      "data": { "id": 1, "nsgNumber": "NSG1001", "leaveYear": 2026, "leaveTaken": 30,
                "createdAt": "2026-10-26T20:14:03.1+05:30", "createdBy": 9001,
                "updatedAt": "2026-10-26T20:14:03.1+05:30", "updatedBy": 9001 } }
```

Example (mobile):

```json
GET /api/v1/leave/my-record
200 { "success": true, "message": "Leave record fetched successfully",
      "data": { "nsgNumber": "NSG1001", "leaveYear": 2026, "leaveTaken": 35, "lastUpdatedAt": "2026-10-26T20:20:00+05:30" } }

404 { "success": false, "message": "No leave record has been entered for you for 2026 yet.", "data": null }
```

Upload success and failure:

```json
200 { "success": true, "message": "Leave upload completed: 400 created, 100 updated, 0 unchanged.",
      "data": { "id": 57, "fileName": "leave.xlsx", "leaveYear": 2026, "uploadStatus": "Completed", "totalRows": 500,
                "createdCount": 400, "updatedCount": 100, "unchangedCount": 0, "failedCount": 0,
                "createdAt": "...+05:30", "createdBy": 9001, "completedAt": "...+05:30" } }

400 { "success": false, "message": "Upload rejected: 2 of 120 rows have errors. First problem, row 7: NSGNumber was not found among active personnel. No leave record was changed. Row-level report: batch 58 (GET /api/v1/admin/leave-uploads/58/errors).", "data": null }
```

The envelope has `data: null` on every failure, so the batch id travels in the message; `GET /api/v1/admin/leave-uploads` (newest first) also lists it.

## 4. Excel upload

**Template:** the first worksheet of an `.xlsx` workbook. Row 1: headers `NSGNumber` and `LeaveTaken` (any order, case-insensitive, no other columns). One person per row below. Fully blank rows are ignored. Type the NSG number as **text** so leading zeros survive. Only `.xlsx` is accepted (not `.xls`, `.csv`).

**Limits (configurable, section `Leave` in configuration, defaults in code):** `MaxFileSizeBytes` 2 MB, `MaxRows` 5000, `StaleBatchMinutes` 15. The request ceiling is a fixed 16 MB. The reader needs no new NuGet package (a streaming reader over the OOXML ZIP/XML parts); it refuses non-ZIP content, caps every unpacked part at 25 MB, forbids DTDs and entity expansion, limits cell text, and never extracts to disk. The file name is stored only as a display value (no directories, no control characters, 255 characters).

**Workflow** (`LeaveBulkUploadService`):

1. Authenticate/authorize; take the admin from the token; derive the IST year.
2. **Write the batch row first** (status `Processing`, holding the single upload lock). If another upload holds it: 409, nothing else happens.
3. Check size/extension/content, parse, then validate **every** row: NSG number format, leave value (required, whole, 0-366), duplicates inside the file (**every** occurrence is an error, nothing is chosen silently), existence among active personnel. Errors carry the Excel row number (header = row 1).
4. Any problem: nothing is applied. The batch becomes `ValidationFailed` with its error rows (at most 1000 stored, plus a line saying how many more) in a **separate, durable write**, then 400.
5. All valid: **one database transaction** creates missing current-year records, replaces changed totals (unchanged totals are counted and not written), writes the history rows and, **in the same transaction**, marks the batch `Completed` with its counters. All or nothing.
6. Failure inside the transaction: it rolls back (no record, no history row, the batch is still `Processing` in the database); the service then records `ProcessingFailed` in a separate write and answers 409 (a record was created/changed by someone else meanwhile) or 500.

**Honesty rules:**
- `Completed` is written inside the leave transaction, so it can only exist if the data was committed. After an unexpected exception the repository reads the batch back: `Completed` means it did commit, `Processing` means it did not. If that read also fails the outcome is **unknown**: the batch stays `Processing`, the answer is 500 "result could not be confirmed ... check batch N before retrying", and rollback is **not** claimed.
- If the failure record itself cannot be saved, the answer says so ("The failure record for batch N could not be saved") instead of pointing at a report that does not exist.
- A batch left `Processing` by a crashed host stops blocking after `StaleBatchMinutes`; the next upload closes it as `ProcessingFailed` with an explanatory error row.

**Concurrency:** one upload at a time, enforced by the database (`processing_lock`, a unique nullable column that is 1 only while a batch is `Processing`). Manual writes and bulk writes use a `row_version` concurrency token plus the unique `(nsg_number, leave_year)` key: a lost race is retried (manual: 3 times, then 409) and a bulk apply that meets a concurrent change rolls back as a whole (409). Re-uploading the same file is safe: the second run reports everything `unchanged`.

## 5. Tables (migration `20261026164053_Add_Leave_Management`)

| Table | Columns (all `snake_case`; IST `*_ist`) | Keys / constraints |
|---|---|---|
| `personnel_leave_records` | `id` bigint, `nsg_number` varchar(30), `leave_year` int, `leave_taken` int, `row_version` int, `created_at_ist`, `created_by`, `updated_at_ist`, `updated_by` | **unique (`nsg_number`, `leave_year`)**; checks: `leave_taken` 0..366, `leave_year` 2000..2999 |
| `personnel_leave_upload_batches` | `id`, `file_name` varchar(255), `leave_year`, `total_rows`, `created_count`, `updated_count`, `unchanged_count`, `failed_count`, `upload_status` int (1 Processing, 2 Completed, 3 ValidationFailed, 4 ProcessingFailed), `processing_lock` tinyint NULL, `created_at_ist`, `created_by`, `completed_at_ist` | unique `processing_lock`; checks: status 1..4, counters >= 0, lock = 1 exactly while status = 1 |
| `personnel_leave_upload_errors` | `id`, `batch_id`, `excel_row_number` int NULL, `nsg_number` varchar(30) NULL, `error_message` varchar(500), `created_at_ist` | FK `batch_id` -> batches (Restrict); index (`batch_id`, `excel_row_number`) |
| `personnel_leave_history` | `id`, `leave_record_id`, `nsg_number`, `leave_year`, `old_leave_taken` NULL, `new_leave_taken`, `change_source` int (1 manual, 2 bulk), `upload_batch_id` NULL, `changed_by`, `changed_at_ist` | FKs to records and batches (Restrict); index (`nsg_number`, `changed_at_ist`); checks: source 1..2, counts >= 0 |
| `admin_roles` | + row id 7 `LEAVE_ADMIN` | |

Deviations from the brief (all deliberate):
- `row_number` is a **reserved word in MySQL 8** (window function) and `docs/DATABASE-STANDARDS.md` forbids reserved words, so the column is `excel_row_number`.
- `personnel_leave_records` has an extra `row_version` (optimistic concurrency); the batches table has `unchanged_count`, `leave_year`, `processing_lock`, `completed_at_ist`.
- `created_by`/`updated_by` are NOT NULL and set on creation (the brief asks for both to be set); they are `admin_profiles.id` values without a foreign key, like every other admin id column in this code base.
- No foreign key from `nsg_number` to `users_pis`: the same decision as ADR-014/ADR-016. The service validates existence; deleting a PIS row never cascades to leave.
- The FK-name generator in `DatabaseNamingStandard` produced a name longer than MySQL's 64 characters for the errors -> batches key, so the standard now **keeps a constraint name set explicitly with `HasConstraintName`** (still format- and length-checked). No existing foreign key was affected.
- A check constraint tying `change_source` to `upload_batch_id` was left out on purpose: MySQL forbids CHECK constraints on foreign-key columns with referential actions, and I could not confirm it for `Restrict` without a database. The repository guarantees it.

## 6. Audit logging

`AuditAction.LeaveRecordChanged` (manual change: actor, year, old and new total, ids only) and `LeaveBulkUploadProcessed` (batch id, status, counters). `LeaveRecordViewed` is reserved and not written yet. File contents, tokens and exception text are never logged or stored.

## 7. Code map

Domain: `Entities/PersonnelLeave.cs`, `Enums/LeaveEnums.cs`, `AdminRoleCodes.LeaveAdmin`, `Policies.RequireLeaveAdmin`.
Application: `Leave/` (`LeaveLimits.cs` settings/limits/messages, `DTOs`, `Excel/LeaveExcelReader.cs`, `Services/LeaveService.cs`, `LeaveBulkUploadService.cs`, `LeaveRules.cs`), `Interfaces/Repositories/ILeaveRepository.cs`.
Infrastructure: `Repositories/LeaveRepository.cs`, `Persistence/Configurations/LeaveConfigurations.cs`, migration, `Leave` settings binding in `DependencyInjection.cs`.
API: `LeaveAdminController`, `LeaveController`, policy line in `Program.cs`.
Tests: `LeaveExcelReaderTests`, `LeaveServiceTests`, `LeaveBulkUploadTests`, `LeaveAuthorizationHostTests` (all without a database), `LeaveDatabaseTests` (MySQL, skipped without `NSG_TEST_MYSQL`), plus the updated schema pin and role lists.

## 8. What is verified and what is not

Verified by tests that ran (no database): the Excel reader (including a zip bomb, DTD/entity expansion, truncated and legacy files), every validation rule, the manual create/update/unchanged/previous-year/IST-year behaviour, the bulk workflow including the all-or-nothing, failure-recording, unknown-outcome and single-upload decisions, and the full HTTP pipeline (real JWT, policies, envelope) including that every other admin role, a mobile token and anonymous callers are refused.

**Not verified (see 8.1 for what the applied migration did confirm):** the SQL of `LeaveRepository`, the behaviour of the unique/CHECK constraints on bad data, the real transaction rollback, `row_version` and unique-key races, the unique `processing_lock`, `IN (...)` chunking for 5000 rows, and the real admin session look-up for the new role. The repository fake used in the service tests stages writes and only commits on success, so those tests prove the service's decisions, not the database's atomicity. `LeaveDatabaseTests` covers exactly these points but has never been run; it needs a **dedicated** database whose name ends in `_test` (it is dropped and recreated):

```
set NSG_TEST_MYSQL=Server=127.0.0.1;Port=3306;Database=nsg_leave_test;Uid=...;Pwd=...
dotnet test tests/NSG.Mobile.Tests --filter "FullyQualifiedName~LeaveDatabaseTests"
```

Before first use (the migration itself is already applied to `nsg_mobile_dev`, see 8.1): the reviewed SQL came from `dotnet ef migrations script 20261026135357_Change_Grievance_Resolution_Document_To_Varchar 20261026164053_Add_Leave_Management` (4 `CREATE TABLE`, 1 `INSERT` into `admin_roles`, no change to existing data), apply it to a throw-away database first, then run the database tests and a manual smoke test (create, update, upload a valid and an invalid workbook, read as a mobile user).

### 8.1 Migration applied to the local development database (2026-10-26)

**Applied, not functionally tested.** `20261026164053_Add_Leave_Management` was applied with `dotnet ef database update` (startup auto-migration off) to the local development database **`nsg_mobile_dev` on `127.0.0.1:3306`** only; the target was checked from the resolved connection string (credentials in user-secrets) and from the live session (`DATABASE()` and port). The SQL was reviewed beforehand: four `CREATE TABLE`, seven `CREATE INDEX`, one `INSERT` into `admin_roles`, and the `__EFMigrationsHistory` row; no `DROP`, `ALTER`, `UPDATE` or `DELETE` of existing objects. Nothing was applied to any shared, test or production database, and `LeaveDatabaseTests` were not run.

Confirmed by MySQL after the update (`information_schema` and direct queries):
- `__EFMigrationsHistory` ends with `20261026164053_Add_Leave_Management`.
- The four tables `personnel_leave_records`, `personnel_leave_history`, `personnel_leave_upload_batches`, `personnel_leave_upload_errors` exist and are **empty**.
- All indexes of section 5 exist, including the unique `ix_personnel_leave_records_nsg_number_leave_year` and the unique `ix_personnel_leave_upload_batches_processing_lock`.
- The three foreign keys exist, all `RESTRICT`: `fk_personnel_leave_history_leave_record_id`, `fk_personnel_leave_history_upload_batch_id`, `fk_personnel_leave_upload_errors_batch_id`.
- Seven CHECK constraints exist: `ck_personnel_leave_records_leave_taken`, `..._leave_year`, `ck_personnel_leave_upload_batches_counts`, `..._processing_lock`, `..._upload_status`, `ck_personnel_leave_history_change_source`, `..._counts`.
- **MySQL accepted the unique `processing_lock` index and the `ck_personnel_leave_upload_batches_processing_lock` CHECK constraint** (the table creation succeeded with them).
- `admin_roles` row 7 is `LEAVE_ADMIN` / "Leave Admin", active; rows 1-6 unchanged. The errors table has the column `excel_row_number`.

**Still NOT verified (requires real-MySQL tests, none has run):**
- the SQL of `LeaveRepository` (queries, `IN (...)` chunking, tracked updates);
- the bulk-upload transaction rollback and the post-failure verification of the batch status;
- concurrency and race handling (`row_version`, the unique `(nsg_number, leave_year)` key, simultaneous manual saves and uploads);
- the upload-lock behaviour (second upload refused, release after a failed batch, stale-batch closing);
- that the CHECK constraints actually reject bad rows (only their existence was checked), and `ck_personnel_leave_history_change_source` and `ck_personnel_leave_history_counts` in particular have **not been specifically tested**;
- the real admin session look-up for the new role.

`LeaveDatabaseTests` (skipped without `NSG_TEST_MYSQL`) cover these points and have **never been run**. They drop and recreate their database, so they must use a dedicated `*_test` database, never `nsg_mobile_dev`.

**No `LEAVE_ADMIN` account has been provisioned yet.** The role exists, but no `admin_profiles` row uses it; a Super Admin must create one (`POST /api/v1/admin/accounts`, `roleCode: LEAVE_ADMIN`) before the Leave Admin endpoints can be used.
