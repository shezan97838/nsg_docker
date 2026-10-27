# ADR-019: Leave Management (admin-maintained yearly leave totals)

Status: Accepted (implemented, fake-tested; migration applied to the local development database nsg_mobile_dev only, not functionally tested on MySQL)
Date: 2026-10-26

## Context
Leave Admins must maintain, per person, the total leave taken in the current calendar year, one by one or by Excel upload; mobile users read their own number.
It is not a leave application/approval/balance system. Detail: `docs/LEAVE-MANAGEMENT.md`.

## Decisions
1. **A seventh fixed admin role `LEAVE_ADMIN`** (id 7, seeded by migration) with its own policy `RequireLeaveAdmin`. It does not widen any existing role: Super Admin is not admitted either (ADR-016 rule "a policy admits exactly the named role"). It is added to `RequireAdminAccount` (any active admin) so the role can use sign-out and `auth/me`.
2. **One row per person and year** (`personnel_leave_records`, unique `(nsg_number, leave_year)`), `leave_taken` is the final total. The year is derived from `IndianStandardTime.Now`; no client value is accepted. History is never overwritten.
3. **Append-only `personnel_leave_history`** records every actual change (old, new, actor, time, source, batch) in the same transaction, because `updated_by` only names the last editor.
4. **Optimistic concurrency** by a `row_version` token plus the unique key. Manual writes retry a lost race 3 times, then answer 409; the bulk transaction rolls back as a whole on a conflict.
5. **Bulk upload is all-or-nothing and synchronous** (no queue, the workbook is at most 2 MB / 5000 rows by default). Validation runs over the whole file first; the batch and its error report are written outside the data transaction so they survive a rollback.
6. **The batch's `Completed` status is written inside the leave transaction.** It therefore proves the commit, and after an exception the repository can find out what happened by reading the batch. If even that fails the outcome is reported as unknown; rollback is never claimed without proof.
7. **Single running upload enforced by the database:** a nullable `processing_lock` column, unique, equal to 1 exactly while the batch is `Processing` (check constraint). MySQL allows many NULLs in a unique index. A crashed upload is closed after `StaleBatchMinutes`.
8. **No new NuGet package:** a small streaming reader over the OOXML parts (`System.IO.Compression` + `XmlReader`, DTDs prohibited, every part byte-capped).
9. **No foreign key from `nsg_number` to `users_pis`** and no FK on admin id columns (same as ADR-014, ADR-016).
10. **Naming:** `excel_row_number` instead of `row_number` (MySQL 8 reserved word). `DatabaseNamingStandard` now keeps an explicitly configured foreign-key constraint name, because the generated `fk_<table>_<principal>_<column>` exceeds 64 characters between `personnel_leave_upload_errors` and `personnel_leave_upload_batches`.

## Consequences
- The migration must be reviewed and applied by the owner; nothing has run on MySQL (`LeaveDatabaseTests` exist for that first run).
- A missing current-year record is answered 404 with a message on the mobile endpoint rather than as zero leave (OQ-041).
