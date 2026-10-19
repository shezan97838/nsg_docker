# ADR-014: Mobile user profile and mobile approval request entities
> **Timestamp note (2026-10-18):** this document predates the IST decision. Where it says UTC or `*_utc`, the current rule is IST and `*_ist` columns (see `CLAUDE.md` "Timestamp Convention" and `docs/DATABASE-STANDARDS.md`). The text below is kept as the historical record.

**Status:** Accepted. Entities and EF mappings exist; the migration was created with ADR-015 (`Add_Mobile_Profile_Approval_And_Grant_Tables`, **not applied to any database**). The workflow is implemented per ADR-015.

## Context
Mobile login/registration starts with NSG number, personal mobile number and date of birth. Two tables support it.

### `mobile_user_profiles` (`MobileUserProfile`)
The account of an approved person.
- `nsg_number` varchar(30) and `mobile_number` varchar(15): each **unique**. `date_of_birth` (`date`) is **not** unique.
- `role` (int) holds an existing `UserRole`; default `Employee`.
- `mpin_hash` and `password_hash` (both nullable varchar(500)): hashes only.
- `is_active`, `failed_login_attempts`, `locked_until_utc`, `last_login_at_utc`, and audit columns `created_at_utc`, `created_by`, `updated_at_utc`, `updated_by`.

### `approve_mobile_users` (`ApproveMobileUser`)
The history of activation requests; approved and rejected rows are kept.
- `nsg_number`, `mobile_number`, `date_of_birth`: none unique.
- `approval_status` (int, default 0) reuses `MobileAppActivationStatus` (Pending 0, Approved 1, Rejected 2).
- `requested_at_utc`, `approved_by`, `approved_at_utc`, `remarks`, and the same audit columns.
- `pending_nsg_number`: generated column, see "One pending request" below.
- Indexes: `(nsg_number, approval_status)` for history and pending lookups, `(approval_status)` for the reviewer queue, and the unique pending index.

There is no foreign key to `users_pis`; PIS is searched by NSG number when no profile exists.

## Decisions

### 1. Time and timezone
All `DateTime` values are **UTC instants**, stored as `datetime(6)` in `*_at_utc` / `*_utc` columns and written with `DateTime.UtcNow`. The application operates in India, so the API/UI boundary converts to and from `Asia/Kolkata` (IST, UTC+05:30) when it displays or accepts a time. Nothing may depend on the server's local timezone or on MySQL's session `time_zone`. `date_of_birth` is a `DateOnly` (`date`): a calendar date with no timezone and no conversion. (IANA id `Asia/Kolkata` on Linux; Windows id `India Standard Time`. The conversion helper is part of the future API work.)

### 2. Mobile number
Exactly ten digits, first digit 6–9, no country code: `^[6-9][0-9]{9}$`. The pattern is the existing `DomainConstants.PisFormats.MobileNumberPattern`, used by the PIS validators and by the `ck_*_mobile_number_format` check constraints of both new tables, so one rule serves PIS, profiles and requests. Input that arrives with `+91` must be normalised to ten digits by the future application validator before it is stored or compared (the existing authentication validators accept an optional `+91`).

### 3. Credentials
Both `mpin_hash` and `password_hash` are kept, nullable until set up. Only secure hashes are stored; a check constraint rejects an empty string in either column. The **MPIN is the primary login method.** Device biometrics (fingerprint/face) are verified on the device by the platform and reach the server only as a device-bound assertion, as ADR-007 already decides. Biometric data is never stored and a biometric result is never treated as a server-side password. Open point for the workflow step: a 4–6 digit MPIN has a tiny key space, so its hash must be slow and salted like the existing PBKDF2 hashing, with the attempt limit and lockout (`failed_login_attempts`, `locked_until_utc`) as the real protection.

### 4. Role
`MobileUserProfile.Role` defaults to `UserRole.Employee`. Mobile registration always assigns `Employee` on the server and never accepts a role from the client (the future registration DTO must not contain one). The column accepts only the four existing `UserRole` values (check constraint), so web access or other roles can be added later without a schema change, but a role stored here grants no permission now: authorization stays policy-based and is untouched.

### 5. Uniqueness
`mobile_user_profiles.nsg_number` and `mobile_number` are each unique, enforced by unique indexes (the default collation `utf8mb4_0900_ai_ci` compares case- and accent-insensitively, as for `users_pis`). Date of birth is never unique.

### 6. One pending request per NSG number
MySQL 8 has no partial unique index. The database enforces the rule with a **stored generated column** and a unique index:

```sql
pending_nsg_number varchar(30) AS (IF(approval_status = 0, nsg_number, NULL)) STORED NULL
CREATE UNIQUE INDEX ix_approve_mobile_users_pending_nsg_number ON approve_mobile_users (pending_nsg_number);
```

Only Pending rows have a non-NULL value, and MySQL lets any number of NULLs repeat in a unique index, so approved and rejected history is unlimited while a second pending request for the same NSG number fails with a duplicate-key error. Approving or rejecting a request clears its value, so a later request is allowed.

Compatibility:
- EF Core 8 / Pomelo 8: `HasComputedColumnSql("IF(`approval_status` = 0, `nsg_number`, NULL)", stored: true)`. `dotnet ef dbcontext script` renders `... AS (IF(...)) STORED NULL` and the unique index, with all names lowercase `snake_case` and accepted by `DatabaseNamingStandard`.
- MySQL 8.0 supports unique indexes on stored generated columns; `IF` over two columns of the same row is deterministic and allowed.
- EF never writes the column (the property has a private setter and is marked computed); it is read back after an insert/update.
- Collation follows `nsg_number`, so the one-pending rule is case-insensitive like the rest.
- The future repository must map the duplicate-key error on `ix_approve_mobile_users_pending_nsg_number` to a "request already pending" result, as `UserPisRepository` does for `ix_users_pis_*`. The application check ("pending request exists → do not create another") stays; the index is the guarantee under concurrency.

**Not yet verified on a real MySQL server** (no database may be touched in this step). The migration step must apply it on `nsg_mobile_test` and confirm: insert two Pending rows with the same NSG number → rejected; Pending + Approved + Rejected rows with that number → accepted; the model/snapshot/schema agree.

## Intended login order (not implemented)
1. Profile exists and active → ask for MPIN. Profile exists, inactive → contact IT support.
2. No profile → check `approve_mobile_users`; a pending request → return the pending message, do not query PIS, do not create another request.
3. No request → match NSG number, mobile number and date of birth in PIS. PIS Pending → OTP must succeed, then create exactly one request. PIS Approved → approved-registration process, no redundant request. PIS Rejected → contact IT support.
4. The workflow keeps `users_pis.mobile_app_activation_status` equal to the request status.

### 7. Review fields and audit columns
- Check `ck_approve_mobile_users_review_fields`: a Pending request has `approved_by` and `approved_at_utc` NULL; an Approved or Rejected request has both set.
- `approved_by`, `created_by` and `updated_by` stay plain `int` with no foreign key, following the existing convention (`users.approved_by`, `users_pis.created_by`). The reviewer is the authenticated Registration Admin (admin-portal account, ADR-016/017): `approved_by` holds the `admin_profiles` id read from the signed `admin_profile_id` token claim (a legacy staff `users` id is still accepted from a numeric `sub`); the audit log `user_id` carries the same id; the creator of a self-registered request has no user id, so `created_by` stays NULL for those rows.

## Migration step checklist
Create the migration; review its SQL; extend `ExpectedDatabaseSchema.cs` (both tables including `pending_nsg_number`); run the full suite on the test database; ask for approval before applying to development.
