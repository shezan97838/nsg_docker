# ADR-027: Local filesystem is the only document storage

**Status:** Accepted (2026-11-08). Code complete; Linux/Ubuntu runtime not yet verified.
**Supersedes:** ADR-018 (Cloudflare R2 behind `IFileStorageService`). Supersedes in part ADR-023, which kept R2 as the object store outside Development.

## Context
ADR-018 put grievance documents, DG message images and banner images in a private Cloudflare R2 bucket, and ADR-023 kept that arrangement on the Render deployment while every other module used the local disk under `Storage:RootPath`. Two storage providers meant two sets of configuration, provider-selection rules, startup checks and tests. R2 also has no India-only region (OQ-035). The client has confirmed that no data in the existing R2 bucket needs to be preserved and that Render is a development/testing environment only.

## Decision
* **`LocalFileStorageService` is the only `IFileStorageService`.** It is registered directly; there is no provider selection.
* **No Cloudflare R2 or AWS S3 runtime dependency.** `R2FileStorageService`, `R2Options`, `ObjectStoreProvider`, the `IAmazonS3` registration and the `AWSSDK.S3` package were removed. The `R2Storage` configuration section no longer exists; a stray `R2Storage__*` variable is ignored.
* **`Storage:RootPath` is mandatory in every environment** (Development, Staging, Production). `StorageStartupValidator` stops the host when it is missing, blank, relative, absent or unwritable. There is no fallback to another location.
* **Keys and database are unchanged.** Stored keys (`grievances/yyyy/MM/{guid}.ext`, `dg-messages/…`, `banners/…`) were provider-neutral; `StorageRoot` maps them to `Grievance/`, `DgMessages/` and `Banners/` under the root. No schema change or migration.
* **`IStorageAvailability` is kept** (always available in production) so a service answers 503 instead of failing if a host reports storage as unavailable.
* **`tools/NSG.Mobile.StorageMigration` was removed.** It existed only to copy R2 objects and old local files into the common root; no R2 data is migrated.

## Environments
| Environment | Storage | Persistence |
|---|---|---|
| Local Development (Windows) | local file system, `D:\NSGStorage` | persistent |
| Render (free, development/testing) | local file system in the container, `Storage__RootPath=/var/data/files` (set in the `Dockerfile`) | **ephemeral**: files are lost on every deploy or restart unless a persistent disk is mounted at `/var/data` |
| Ubuntu production | local file system, for example `/var/lib/nsg-mobile/uploads` | persistent; the folder must exist and be writable by the service account; back it up (not yet run on Ubuntu) |

## Consequences
* Documents uploaded to a Render instance can disappear; that is accepted for testing only.
* Local storage is single-server. Running more than one API instance against the same documents needs a shared volume; this is not addressed here.
* Documents now live in India on the NSG-controlled server in production, which removes the data-residency concern of OQ-035.
* Backup, retention and disk-space monitoring of the storage root become an operational duty of the production host.
* Rollback to R2 would require restoring the removed code from version control.
