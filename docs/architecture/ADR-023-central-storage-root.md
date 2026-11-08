# ADR-023: One storage root for every stored document

**Status:** Policy final (2026-11-06); the R2 parts are superseded by ADR-027 (2026-11-08): `LocalFileStorageService` is now the only provider and `Storage:RootPath` is mandatory in every environment. The layout, safety rules and one-root policy below remain current; passages about R2, provider selection or a Render exception are historical. Ubuntu local storage is configured and supported but **not yet runtime-verified**.
**Supersedes in part:** ADR-018 (R2 for grievance documents); extends ADR-021 (salary slips) and ADR-022 (Form 16), whose separate roots no longer exist. Superseded in part by ADR-027 (R2 and provider selection removed).

## Context
Documents lived in three unrelated places: `Form16Storage:RootPath`, `SalarySlipStorage:RootPath`, and `IFileStorageService` (private Cloudflare R2 when `R2Storage:ServiceUrl` was real, otherwise `FileStorage:BasePath`) for grievance documents, DG message images and banner images. An earlier version of this ADR kept those roots as a transitional fallback when `Storage:RootPath` was empty. That fallback allowed a host to start with documents going somewhere unintended, so it was removed.

## Decision

### Storage policy
| Environment | Object store (Grievance, DG messages, Banners) | Form 16, salary slips | Root |
|---|---|---|---|
| Local Development (Windows) | local file system (R2 settings ignored) | local | `D:\NSGStorage` |
| Ubuntu with local storage | local file system | local | `/var/lib/nsg-mobile/uploads` (not yet run on Ubuntu) |
| Current Render deployment | Cloudflare R2, unchanged | local, only if `Storage:RootPath` is set | not required for R2 |

### Provider selection (one rule, `ObjectStoreProvider.UsesR2`)
R2 is used **only** outside Development **and** when `R2Storage:ServiceUrl` holds a real value. In every other case the object store is the local file system. Form 16 and salary slips are always local. A local-storage configuration error is never hidden by a fallback to another provider or another root.

### One root, no fallback
* **`Storage:RootPath` is mandatory whenever local file storage is selected.** Missing, empty, whitespace, relative, non-existent, unwritable or unsafe values stop the application at startup (`StorageStartupValidator`) with a message naming `Storage:RootPath`.
* The former `Form16Storage:RootPath`, `SalarySlipStorage:RootPath` and `FileStorage:BasePath` were removed (code, options classes, `appsettings.json`, the `Program.cs` startup check). Setting them, for example the `FileStorage__BasePath` the Dockerfile still passes on Render, changes nothing. Only the limits survive: `Form16Storage:MaxPdfSizeBytes`, `SalarySlips:*`, `FileStorage:MaxFileSizeBytes` and `AllowedExtensions`.
* With R2 selected, `Storage:RootPath` is not required and startup succeeds without it; Form 16 and salary slips then fail with a clear `Storage:RootPath is not configured` error when used, never writing elsewhere. If it **is** set it is validated like any other.
* The root folder must already exist; it is never created. Module folders and `_incoming` are created inside it. A probe file is written and removed to prove writability.

### Layout (unchanged)
```
{Storage:RootPath}/
├── Form16/        {assessmentYear}/{encodedNsgNumber}/part-a.pdf | part-b.pdf
├── SalarySlips/   {yyyy}/{MM}/{guid}.pdf
├── Grievance/     {yyyy}/{MM}/{guid}.{pdf|jpg|png}
├── DgMessages/    {yyyy}/{MM}/{guid}.{jpg|png}
├── Banners/       {yyyy}/{MM}/{guid}.{jpg|png}
└── _incoming/     staged uploads (same volume, so the final step is a rename)
```
Database values keep their shape (`salary-slips/…`, `grievances/…`, `dg-messages/…`, `banners/…`, Form 16 relative paths); `IStorageRoot` maps them to disk, dropping the namespace segment where there is one. No schema change, no absolute path in MySQL.

### Safety (all in `StorageRoot` / `StorageFiles`)
Root absolute, outside `wwwroot` and the deployment directory, not a drive or filesystem root; stored values are plain forward-slash relative paths that must resolve inside **their own module folder**; existing symbolic links and junctions below the root are refused; uploads are staged under a random name with `CreateNew`, flushed and renamed without overwrite, so a failure never leaves a partial file under a final name. Form 16 keeps stage → commit (previous file kept as backup) → complete.

## Migration (local Development, done)
`tools/NSG.Mobile.StorageMigration` (`plan`, `copy`, `verify`) copied the 23 documents referenced by the local Development database into `D:\NSGStorage` (SELECT-only database access, refuses non-local databases; R2 HEAD/GET/LIST only; never moves or overwrites; manifests outside the storage root). It is not re-run by this policy change.

## Consequences / open items
* Ubuntu is untested. The code uses OS-aware path comparison and Unix file modes, but nothing has run on Linux.
* Setting `Storage__RootPath` on Render, UAT or production, copying real R2 data, and removing R2 (`R2FileStorageService`, `R2Options`, the AWS S3 package) remain separate, approved steps. Rollback of such a cutover needs a reverse copy of anything uploaded after it.
* Form 16 folder names keep the case of the NSG number, so two numbers that differ only by case collide on Windows but not on Linux.
* `appsettings.Development.json` still contains Development R2 credentials (tracked in git); move them to user-secrets and rotate (not part of this change).
* Unwritable-root detection is by an actual write probe; it is covered by tests for an absent root and a file where a folder must be, but not by a permission-denied test on Windows.
