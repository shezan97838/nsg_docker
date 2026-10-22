# ADR-018: Cloudflare R2 behind IFileStorageService

**Status**: Accepted (code complete; not yet smoke-tested against a real bucket)
**Date**: 2026-10-22
**SOW Reference**: §3.8 (Grievance supporting documents). Builds on ADR-011 (external integration abstraction) and ADR-012 (file security).

## Context
Grievance documents (PDF and JPEG evidence) must be stored somewhere that survives deploys and works with more than one host. The local-disk
implementation (`LocalFileStorageService`) is single-server and, on Render, ephemeral. The client chose Cloudflare R2 (S3-compatible).

## Decision
- **Reuse the existing abstraction.** `IFileStorageService` (`StoreAsync` / `GetAsync` / `DeleteAsync`) gains a second implementation,
  `R2FileStorageService`, in Infrastructure. Application and API code never see the S3 SDK, so the provider can be swapped again.
- **Provider selection is configuration, not code.** R2 is used when `R2Storage:ServiceUrl` holds a real value. The committed `REPLACE_WITH_...`
  placeholder means "not configured", so Development and the test suite keep the local store. Once R2 is selected, a missing bucket, access key
  or secret key stops the application at startup with a message naming the setting (never the value).
- **Private bucket, no URLs.** The bucket has no public access. The API never builds, stores or returns a public or pre-signed URL; documents are
  streamed through the API after the caller's own authorization check (owner of the grievance, or the Grievance Admin policy).
- **Object keys** are server generated: `{category}/{yyyy}/{MM}/{guid}{ext}` (for grievances `grievances/2026/10/<guid>.pdf`). The client's file
  name is never part of a key; it is kept, sanitised, in the grievance's `document_urls` metadata and used as the download name.
  `R2Storage:KeyPrefix` (optional, for example one folder per environment) is added inside the storage service and is not part of the stored key,
  so keys stay portable between buckets. The service reads and deletes only keys of exactly the generated shape, which rules out traversal and
  foreign keys.
- **Credentials** come from user-secrets or the environment (`R2Storage__AccessKey`, `R2Storage__SecretKey`), never from a committed file. The
  token is scoped to the one bucket with Object Read & Write only.
- **R2 client settings:** path-style addressing, region `auto`, unsigned payload on `PutObject`, and `RequestChecksumCalculation` /
  `ResponseChecksumValidation` set to `WHEN_REQUIRED` (R2 rejects the SDK's newer default checksum trailers). SDK: `AWSSDK.S3` 3.7.511.8, pinned.
- **No silent fallback to local disk in production.** A small flag (`IStorageAvailability`, registered in Infrastructure) is true in Development
  or when R2 is the active provider. Outside Development without R2, grievance uploads and downloads answer 503 `STORAGE_UNAVAILABLE` and never
  touch the local disk.
- **No orphans.** A file is validated completely before storage is called. If the object is stored but recording it in the database fails (or the
  request is cancelled), the object is deleted again; a failed delete is logged for manual clean-up.
- **Failure handling.** An unreachable or refusing store surfaces as `FileStorageUnavailableException`, mapped to 503 with a generic message that
  contains no endpoint, bucket or credential detail.

## Consequences
- Files are not stored on the application host; deploys and scaling do not lose them.
- Authorization is enforced in the application for every read; there is no link that can be shared or guessed.
- Cloudflare becomes a processor of grievance documents. See OQ-035 (data residency outside India).
- A download streams through the API, so large files cost API bandwidth; the 10 MB ceiling keeps this bounded.
- `FileStorage__BasePath` is still required by `Program.cs` outside Development even when R2 is active (that check is unconditional). Changing it
  is a separate decision (OQ-037).
- Not verified yet: the real SQL of the repository, real MySQL behaviour, and real R2 behaviour (checksums, unsigned payload, stream disposal under
  load). These need the manual smoke tests in `docs/GRIEVANCE-MODULE.md` section 9.
