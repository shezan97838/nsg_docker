# NSG Mobile API — Open Questions & SOW Ambiguities

> These items require business/client clarification before implementation can proceed.
> Do NOT resolve these silently — each must be explicitly confirmed.

## Critical Ambiguities

### OQ-001: Salary Archive Duration Conflict
- **SOW §3.3**: "Minimum 7-year archive accessible through the app"
- **SOW §6 Compliance Matrix**: "Payroll API integration; 24-month archive"
- **Impact**: Data retention policy, storage sizing, archive API design
- **Current approach**: Configurable `SalaryArchiveMonths` setting
- **Needs**: Client confirmation on intended retention period

### OQ-002: OAuth 2.0 vs JWT Authentication
- **SOW §3.8**: "JWT/OAuth 2.0 based authentication"
- **Issue**: No OAuth authorization server specified in SOW
- **Current approach**: JWT-based auth with refresh tokens (no full OAuth AS)
- **Needs**: Clarify if a full OAuth 2.0 authorization server is required or if JWT with refresh tokens suffices

### OQ-003: MFA Login Sequence
- **SOW §3.8**: "Password + OTP + Biometric"
- **SOW §3.7 Step 3**: Password set → Biometric setup → Device binding (onboarding)
- **Issue**: Exact login-time MFA sequence not specified
- **Needs**: Confirm: Is it Password → OTP → Biometric on every login? Or Password + one of (OTP, Biometric)?

### OQ-004: Biometric Server Verification Mechanism
- **SOW §3.7**: "Biometric setup (fingerprint or Face ID) is initiated on the device"
- **Issue**: Does the server receive biometric data, or just a device-bound proof?
- **Current approach**: API receives device-bound cryptographic proof, NOT raw biometric data
- **Needs**: Confirm biometric stays on-device (platform secure enclave)

### OQ-005: Device Binding Mechanism
- **SOW §3.7 Step 3**: "Device binding is completed (IMEI + device token)"
- **Issue**: Raw IMEI access is restricted on Android 10+ and iOS
- **Current approach**: Platform-safe device identifier hash + optional cryptographic key binding
- **Needs**: Confirm acceptable device identification method

## External Integration Questions

### OQ-006: NIC SMS Gateway API Contract
- URL, authentication method, request/response schema, template IDs
- Currently using placeholder implementation

### OQ-007: HRMS API Contract
- URL, authentication method, available endpoints, data schema
- Used for: PIS data fetch, identity verification during onboarding

### OQ-008: Payroll API Contract
- URL, authentication method, salary/Form-16 data format
- Used for: Salary slip retrieval, Form-16 fetch

### OQ-009: FCM/APNs Configuration
- Firebase project ID, service account credentials
- APNs certificate/key for iOS push notifications

## Business Logic Questions

### OQ-010: Role & Permission Matrix
- Exact roles beyond Employee, ITAdmin, NodalOfficer
- Permission mapping per module per role
- Currently using minimal extensible role set

### OQ-011: Grievance Routing Rules
- *Update 2026-10-22 (audit):* automatic routing by category was removed. A new grievance starts unassigned and a Grievance Admin assigns it with `POST /api/v1/admin/grievances/{id}/forward` (validated against active `GRIEVANCE_ADMIN` accounts). Who should receive new grievances by default, HQ NSG escalation and per-transition SLAs are still open (see OQ-038).
- How are Nodal Officers assigned to categories?
- Routing rules for HQ NSG escalation
- SLA definitions for each status transition

### OQ-012: PIS Editable Fields
- Which PIS fields can users request changes for?
- Which require approval? By whom?
- SOW §3.1 mentions "photo or contact info" as examples

### OQ-013: New Device Registration Policy
- Does new device registration require IT Cell approval?
- Or is re-authentication (password + OTP) sufficient?
- Currently configurable

### OQ-014: Data Residency
- NSG infrastructure or NIC Cloud?
- Affects deployment configuration and compliance

### OQ-015: Form-16 Archive
- SOW §3.4 specifies 7-year archive (clear)
- Confirm: Is this 7 financial years or 7 calendar years?

## PIS Personnel Master (`users_pis`) — raised 2026-10-13

### OQ-016: Mandatory/Optional (M/O) markers for PIS fields
- The workbook read (`PIS Date.xlsx`, 37 columns) has no M/O annotations except "(Optional)" on the property-return date. Only `NsgNumber` and `ServiceNumber` are therefore NOT NULL; everything else is nullable.
- `PIS Data.xlsx` (Desktop\Documentation\ExistingProject\NSG) could not be read in this session — confirm whether it carries the M/O list and which workbook is authoritative.

### OQ-017: NSG rank value format
- Workbook ranks look like `AC-I(GD)`, `R-II(NA)` (rank + trade/branch); `rank_hierarchy` holds 22 ranks without a trade. `UsersPIS.NsgRankId` references `rank_hierarchy`; the trade/branch suffix has no column yet.

### OQ-018: Lookup master lists
- `nsg_units`, `education_qualifications`, `religions`, `parent_department_ranks` are created empty. Approved value lists, and whether units have a parent/child hierarchy (`ParentUnitId`), are not yet supplied.
- Category is `PersonnelCategory` (GEN, SC, ST, OBC). RESOLVED (2026-10-13): EWS (5) is NOT required and was removed; migration `Remove_Ews_Personnel_Category` restores `CK_UsersPIS_Category` to 1-4 (it fails if any row holds 5). Blood group is `BloodGroupType` (standard ABO-Rh, with `[Display]` labels).

### OQ-019: Leave columns
- "AVAILED" columns hold text such as `2 TIMES`; stored verbatim. "DUE" columns carry no unit. Confirm units before any numeric conversion.

### OQ-020: Mobile number normalisation and identifier format
- `UsersPIS.MobileNumber` is stored exactly as supplied (unique) while `Users.MobileNumber` is normalised to +91XXXXXXXXXX. Matching PIS to app users needs an agreed normalisation. Approved NSG/Service number formats (length, letters) are not given; both are varchar(30), unrestricted.

### OQ-021: Encryption design
- `IEncryptionService` is AES-256-CBC with no integrity tag. Confirm whether AES-GCM (or encrypt-then-MAC) is required for Aadhaar/PAN/PRAN/account fields, key rotation, and who may decrypt. `Encryption:Key` is still a placeholder in `appsettings.json`.

### OQ-022: Lookup table names versus stakeholder terminology
- The NSG unit and parent department rank APIs (`/api/v1/nsg-units`, `/api/v1/parent-department-ranks`) are mapped to the existing tables `nsg_units` and `parent_department_ranks`. Stakeholder names `RANKNSG`, `ParentDepartment` and `NSGUnit` do not exist in the schema and were **not** mapped or renamed; `rank_hierarchy` (snake_case) also differs from the PascalCase used elsewhere. Confirm whether these are legacy/external names that need documenting, and whether "ParentDepartment" means a rank within the parent department (as the entity name and test data suggest) or a department master. RESOLVED (2026-10-13): `RANKNSG`, `ParentDepartment` and `NSGUnit` are stakeholder terms, not table names; they map to `rank_hierarchy`, `parent_department_ranks` and `nsg_units`. No table is renamed.
- Lookup `Name` uniqueness is enforced case-insensitively (MySQL default collation) after trimming; confirm this matches the approved value lists.
- Deleting a lookup that `users_pis` references returns 409; deactivation (`IsActive=false`) is the supported alternative. Confirm no cascade or reassignment workflow is required.
- RESOLVED (2026-10-13): `DELETE /api/v1/ranks/{id}` never hard-deletes. It deactivates the rank (idempotent, audited as `RankDeactivated`) and returns 409 while any `users_pis` row holds the rank. Open: `PUT /api/v1/ranks/{id}` with `isActive=false` is still allowed for a rank in use; confirm whether that should also be blocked.

### OQ-023: UsersPIS final model — specification reconciliation
- The "49 agreed properties" list from the earlier discussion was not available in this session. The existing entity (48 stored scalars) was treated as the agreed list and checked against the workbook headers in `PIS Data.xlsx`; the 49th, `MobileAppActivationStatus` (non-null, default `Pending = 0`, int column, `CK` 0–2), was added. The property name `MobileAppActivationStatus` is APPROVED (2026-10-13).
- `UsersPIS.Id` changed from `int` to `bigint` and `WeightKg` from `decimal(5,1)` to `decimal(5,2)` (migration `UsersPis_BigintKey_Decimal2_ActivationStatus`); `HeightCm` was later changed again to whole centimetres (see OQ-024). Both are widening changes; no other table references `UsersPIS.Id`.
- `NumberOfChildren` stays `int?` (column `int`, check >= 0): a `tinyint unsigned` mapping was not applied because no upper bound is approved and the C# type is part of the agreed property list.
- No index was added on `MobileAppActivationStatus`; add one when the activation workflow defines its query pattern.
- Mobile activation status is an onboarding decision only and is unrelated to `Users.Status` / login or session state.

### OQ-024: PIS field validation — unapproved formats and uniqueness of encrypted identifiers
- **Height** is now a whole number of centimetres (`byte?`, MySQL `TINYINT UNSIGNED`, 0–255; 0 rejected). Migration `UsersPis_WholeCmHeight_TenDigitMobile` rounds any existing fractional values explicitly. No plausible-height range (e.g. 100–230 cm) is approved, so none is enforced beyond the type.
- **Mobile numbers** are exactly ten digits starting 6–9; the database check no longer accepts a `+91` prefix. Applying the migration fails if existing rows contain `+91` or other non-conforming values — review them first (rows are not rewritten silently).
- **Next-of-kin mobile** uses the same rule as the individual mobile (ten digits starting 6–9) in the validator; there is no database check (free-text column today). RESOLVED: the workbook sample value `1234567893` is confirmed dummy data, so NOK numbers are Indian mobiles, not landlines.
- **PRAN** (12 digits) and **salary account number** (digits only, 9–18) were PROVISIONAL; the owner decided to KEEP them as they are (2026-10-13). PRAN stays non-unique. PRAN matches the NPS PRAN length and the workbook sample; the account range is a common bank range and is deliberately not a fixed length. Constants: `DomainConstants.PisFormats`. Confirm or replace.
- **Aadhaar** is validated as exactly 12 digits only. RESOLVED: confirmed — no Verhoeff checksum and no first-digit rule.
- **PAN and salary account number uniqueness: APPROVED and ENFORCED (2026-10-13).** The encrypted columns use a random IV, so uniqueness is enforced through blind-index columns `PanNumberHash` and `SalaryAccountNumberHash` (HMAC-SHA256, lower-case hex, 64 chars, unique indexes, NULLs may repeat, format check `CK_UsersPIS_Hashes_Format`). Hashes are computed by `PisBlindIndexes` from the normalised value (trimmed, upper-case PAN; trimmed account number) with domain separation per field, using `BlindIndex:Key` (base64, >= 32 bytes, DIFFERENT from `Encryption:Key`, secure config only). Operational notes: changing the key invalidates every hash and needs a full re-index; rows that already hold encrypted PAN/account values before this migration have NULL hashes and need a backfill (none are expected, as no PIS write API exists yet); a MySQL 1062 on `IX_UsersPIS_PanNumberHash` / `IX_UsersPIS_SalaryAccountNumberHash` must map to 409 Conflict without echoing the value. PRAN and Aadhaar have no uniqueness requirement. Migration: `UsersPis_PanAndSalaryAccount_BlindIndex` (additive).
- Validators and `PisBlindIndexes` exist (`UserPisValidator`, `PisSensitiveIdentifiersValidator`) but nothing calls them yet: there is no PIS create/update API. When one is added, normalise (`PisNormalizer`), validate, then encrypt; duplicate NSG/mobile numbers (MySQL error 1062) should map to 409 Conflict as in the lookup modules.

### OQ-025: Personnel (PIS) CRUD API — decisions taken and open points
- **Authorization:** every `/api/v1/users-pis` endpoint (including reads) requires `RequireITAdmin` (ITAdmin or SystemAdmin). The data covers all employees; an employee seeing their own record belongs to the Profile module. Confirm whether NodalOfficer needs read access.
- **Sensitive fields:** Aadhaar, PAN, PRAN and salary account number are accepted in plaintext, encrypted on write, and returned only masked (last four characters, e.g. `XXXXXXXX9532`) on the detail endpoint; never on the list. No endpoint returns them in full. Confirm whether any role may view the full value (and, if so, add an audited reveal action).
- **Update semantics:** `PUT` replaces all non-sensitive fields (omitted/null clears them); the four sensitive identifiers are replaced only when supplied (null = unchanged). There is currently no way to clear a stored sensitive identifier. The NSG number, activation status, `CreatedAtUtc`/`CreatedBy` cannot be changed by the API.
- **Delete:** `DELETE` deactivates (`IsActive = false`, idempotent, audited `UserPisDeactivated`); personnel rows are never hard-deleted. `PUT ... isActive=true` reactivates. Nothing references `users_pis` yet, so there is no 409 on deactivate.
- **References:** rank, parent department rank, unit, side-stepped-from unit, education, religion and state ids must exist (400 otherwise). Inactive lookups are still accepted; confirm whether new assignments should require active lookups.
- **Strict identifiers:** NSG number and service number must arrive already trimmed (400 otherwise); other text is trimmed and blank text becomes null. `mobileNumber` must be a valid 10-digit number or null; an empty string is rejected.
- **JSON shape:** enums are strings (`"ST"`, `"OPositive"`, `"Pending"`); `heightCm` is an integer (a fractional value is a 400); dates are `yyyy-MM-dd`.
- **Configuration defect fixed:** the development `Encryption:Key` was not a 256-bit key (28 bytes), so the encryption service could not be constructed. It was replaced with a generated dev key; nothing could have been encrypted with the old one. Production and staging must supply valid `Encryption:Key` (32 bytes, base64) and `BlindIndex:Key` (>= 32 bytes, base64) through secure configuration.
- **Not implemented (out of scope):** mobile activation approval/rejection, OTP, password/MPIN, linking a PIS record to a `Users` account, bulk import.

### OQ-026: NSG and PD trades — decisions taken and open points
- **Model:** two completely independent lookup tables, `nsg_trade` (id, nsg_trade_name, created_at_utc, is_active) and `pd_trade` (id, pd_trade_name, created_at_utc, is_active). No codes, no rank foreign keys, no link between them, no seed data. `users_pis` gains nullable `nsg_trade_id` and `pd_trade_id` (C# `NsgTradeId`, `PdTradeId`), FKs with Restrict. RESOLVED 2026-10-14: the first spelling (`NSGTrade`, `NSGTradeID`) was replaced by lowercase snake_case names under the new "Database Naming Rules" (migration `Rename_Trade_Objects_To_Snake_Case`, applied to development).
- **No UpdatedAtUtc:** only the fields listed in the requirement were created. Changes are traceable through the audit log (`NsgTradeCreated/Updated/Deactivated`, `PdTradeCreated/Updated/Deactivated`); add an `UpdatedAtUtc` column if row-level change timestamps are wanted.
- **Names:** unique per table (case-insensitive, trimmed); the same name may exist in both tables. A deactivated trade still holds its name, so it is reactivated with `PUT` instead of re-created.
- **Routes:** `/api/v1/nsg-trades` and `/api/v1/pd-trades` (list with `search`, `isActive` and paging; get; post; put; delete). Reads need authentication; writes need `RequireITAdmin`. `PUT` requires `isActive` explicitly.
- **Deactivation:** `DELETE` deactivates (idempotent, audited); nothing is hard-deleted. Following the rank precedent, a trade used by any `users_pis` record cannot be deactivated (via `DELETE` or `PUT isActive=false`): 409. Renaming a used trade is allowed. Confirm this is wanted for trades.
- **Personnel:** both ids are optional and only checked for existence (400 naming the field when unknown). Following the existing lookup rule, an inactive trade can still be assigned. There is deliberately NO rank/trade matching: any trade can accompany any rank or no rank, and changing a rank never touches a trade. Because `PUT` replaces all non-sensitive fields, omitting a trade clears it.
- **Display (open):** the API returns the rank name and the trade name as separate fields; clients combine them (for example "AC-I (GD)"). The rank data still does not hold values like `AC-I` (see OQ-017).
- **Value lists (open):** no trade values were supplied; the tables start empty (see OQ-018).
- **Migration:** `Add_NSGTrade_PDTrade_And_UsersPis_TradeFks` and `Rename_Trade_Objects_To_Snake_Case` applied to the development database on 2026-10-14 after schema checks and backups; not applied to production. An earlier, never-applied design (rank-linked trades) was removed before this one was created.

### OQ-027: Database naming standard — decisions taken and approvals pending
- **Standard adopted 2026-10-14:** `docs/DATABASE-STANDARDS.md` (lowercase snake_case for every table, column, index and constraint; explicit table mappings; IST `DateTime` columns end in `_ist`). Audit and complete inventory: `docs/DATABASE-SCHEMA-AUDIT.md`.
- **Corrective migration `Standardize_Database_Names_To_Snake_Case`:** renames the 12 legacy tables, 161 columns, 44 indexes, 14 foreign keys and 11 check constraints; makes the trade tables singular (`nsg_trade`, `pd_trade`). Verified on the test database and on a scratch copy of the real development data (identical checksums, rollback tested). **Applied to the development database `nsg_mobile_dev` on 2026-10-14** after a verified backup; not applied to production.
- **Pending decisions:** (1) production deployment of the migration; (2) pin the collation (`utf8mb4_0900_ai_ci`) in the model, finding M3; (3) rename `__EFMigrationsHistory` before the first Linux database exists (finding M4) or accept the rule "build Linux databases from migrations, never from a Windows dump"; (4) add `CHECK` constraints to the enum columns that lack them (M5); (5) drop the redundant `ix_rank_hierarchy_sr_no_rank_name` index (L2); (6) confirm that case- and accent-insensitive uniqueness of NSG/force/mobile numbers is intended (L4).
- **API response rule (implementation pending):** "no blank responses; a message for empty results and after create/update/deactivate" is recorded in `.claude/rules/project-rules.md`, but existing endpoints (empty `items: []`, 204 on delete, bare resources on create/update) do not comply yet. Migrating them changes the public response shape and the tests; needs approval and an agreed envelope (`message` + `data`).

### OQ-028: Mobile registration and approval workflow (ADR-015) — assumptions and open items
- **Assumptions taken (confirm):** (1) an inactive PIS record (`is_active = false`) is treated like Rejected ("contact the IT Admin"); (2) MPIN is 4–6 digits with repeated/sequential values refused; (3) a registration/reset grant lives 10 minutes; at most 5 OTPs per mobile and flow per hour; (4) a pending request is acknowledged only to a caller who also knows the mobile number and date of birth on it; (5) an OTP reset also clears a lockout.
- **Open:** (a) real NIC SMS gateway contract and credentials (the placeholder now fails every send instead of logging OTPs); (b) device binding for mobile profiles (refresh tokens and session revocation are done, see ADR-015 addendum 2); (c) absolute session lifetime cap; (d) per-IP rate limits behind a shared NAT; (e) `OtpService` attempt counting is not atomic.
- **Migration:** `Add_Mobile_Profile_Approval_And_Grant_Tables` (three new tables, no change to existing ones) created, **not applied**. Before applying: run it on `nsg_mobile_test`, confirm two Pending rows with the same NSG number fail on `ix_approve_mobile_users_pending_nsg_number` while Pending + Approved + Rejected rows are accepted (`MobileAuthDatabaseTests` covers this), then ask for approval for development.

### OQ-029: Demo OTP, Linux deployment and migration verification (2026-10-15)
- **Demo OTP** (`Otp:Mode=Demo`, fixed `123456`) is a temporary client-presentation aid, guarded to Development or an explicitly approved Staging/Demo environment; remove the key (default `Sms`) once BSNL is integrated. Open: BSNL gateway contract and credentials; who approves presentation environments.
- **Migration verified** on MySQL 8.0.44 (Windows) in the disposable database `nsg_mobileauth_test` only (applied, rolled back, re-applied; constraints exercised). Not applied to `nsg_mobile_dev` or production. `nsg_mobileauth_test` still exists; drop it only with approval.
- **Linux not executed anywhere.** Open before go-live: TLS/reverse-proxy and forwarded-headers decision, `FileStorage__BasePath` (committed value is a Windows path), excluding `appsettings.Development.json` from the package, case-sensitive prefix check in `LocalFileStorageService`. See `DEPLOYMENT-UBUNTU.md`.

### OQ-030: Mobile sessions and Linux readiness (2026-10-15)
- **Done:** mobile refresh tokens (`mobile_refresh_tokens`, migration `Add_Mobile_Refresh_Tokens`, verified apply/rollback/re-apply on MySQL 8.0.44 in `nsg_mobileauth_test`), rotation with reuse detection, logout, revocation on MPIN change/reset/deactivation; trusted-proxy forwarded headers; FileStorage root must be an absolute, environment-supplied path; Development settings excluded from publish; hosted demo requires a `_demo` database.
- **PIS:** untouched. The client-approved PIS format is final; the mobile workflow reads `users_pis` (NSG number, mobile number, date of birth, activation status, active flag) and writes only `mobile_app_activation_status` during an approval decision.
- **Open:** real Ubuntu run; BSNL SMS gateway; device binding; absolute session lifetime; a profile deactivation endpoint/ADR; `appsettings.json` still lists no `Sms` provider.
- **Both new migrations** (`Add_Mobile_Profile_Approval_And_Grant_Tables`, `Add_Mobile_Refresh_Tokens`) are unapplied on `nsg_mobile_dev` and production.

### OQ-031: Pre-deployment review (2026-10-15)
- **Fixed:** F1 JWT/encryption/blind-index secrets validated at startup (committed `REPLACE_*` placeholders were accepted); F2 oversized `X-Correlation-Id` silently suppressed audit rows; F3 legacy `AuthController` trusted raw `X-Forwarded-For`. Details and evidence in `DEPLOYMENT-UBUNTU.md` section 1. Test count after fixes: 566 of 566 on MySQL 8.0.44 (Windows).
- **Accepted / open:** no absolute session lifetime or device binding for mobile sessions; shared per-IP rate limits behind NAT; CORS `AllowAnyOrigin`; `AuditService` swallows write errors by design (consider alerting on audit failures); `AllowedHosts` must be set per environment.
- **Still requires a real Ubuntu server:** every item in `DEPLOYMENT-UBUNTU.md` section 0 (second table) and the 13-step smoke test in section 7.

### OQ-032: Render.com container hosting (2026-10-15)
- **Added:** `Dockerfile` (multi-stage, non-root, listens on `$PORT`), `.dockerignore`, `DEPLOYMENT-RENDER.md`. Not built or run yet (no Docker on the development machine).
- **Open:** (a) is a public PaaS (Render, servers outside NSG control) approved for this system under the SOW/security requirements, or only for demos? (b) Render's proxy address range for `ForwardedHeaders__KnownNetworks` (to be read from the first deployment's logs); (c) whether `X-Forwarded-For` from Render carries more than one hop (the app uses `ForwardLimit = 1`, so per-IP rate limits and audit IPs must be checked); (d) where the MySQL 8 database is hosted, since Render has no managed MySQL; (e) persistent disk for `FileStorage`.

### OQ-033: Swagger enabled in Production (2026-10-15)
- Swagger UI/JSON (`/swagger`) is now on in every environment at the project owner's request; `Swagger__Enabled=false` turns it off. It lists the whole API surface to anyone with the URL, so switch it off (or restrict it) for the real production deployment. Open: who approves leaving it on for a hosted demo.

### OQ-034: Automatic migrations at startup (2026-10-18)
- The earlier rule was "migrations never run at startup". At the project owner's request startup migration is now enabled by `Database:MigrateOnStartup=true` in `appsettings.json` (`StartupDatabaseMigrator`); `Database__MigrateOnStartup=false` turns it off per environment. The key is absent from the code default (missing = off). `LinuxReadinessTests` pins the wiring.
- **Open:** approval to use it against the NSG production database (the rule asked for approval before any apply); concurrent-instance locking if the service is ever scaled beyond one instance; a rollback procedure (migrations are forward-only here).

### OQ-035: Grievance documents in Cloudflare R2 — data residency outside India (2026-10-22)
- **Decision taken in code:** grievance documents (PDF/JPEG evidence that can contain medical, family, service and legal details of NSG personnel) are stored in a private Cloudflare R2 bucket behind `IFileStorageService` (ADR-018).
- **Open, needs NSG approval before real data is stored:** (a) R2 has no India-only region; location hints and jurisdictions (EU, FedRAMP) do not guarantee storage inside India. Is storing NSG personnel grievance documents with a foreign-headquartered provider, possibly outside India, permitted under the SOW and the applicable data-localisation and security policy? (b) If not, which store replaces it (NIC or government cloud, on-premise S3-compatible such as MinIO)? Only a new `IFileStorageService` implementation and configuration should change. (c) Encryption: R2 encrypts at rest with provider-managed keys; documents are **not** additionally encrypted by the application (the AES field encryption covers PIS fields only). Is provider-managed encryption enough, or is application-level or customer-managed-key encryption required? (d) Retention and deletion: there is no way to delete or replace a document, and no retention period is defined.
- **Until answered:** use the development bucket with test data only; do not enable R2 for production.

### OQ-036: No push notifications — status changes are not real time (2026-10-22)
- `IPushNotificationService` is still the FCM/APNs placeholder, so nothing notifies the employee when a Grievance Admin changes the status, and nothing notifies the nodal officer of a new grievance. The mobile app has to refresh `GET /api/v1/grievances` to see changes. The SOW (§3.5, §3.8) describes status tracking and notifications.
- **Open:** FCM/APNs credentials and contract; which events notify whom (status change to the employee, new grievance to the assigned officer, SLA breach to admins); the polling interval the app should use meanwhile; whether a notification may carry the ticket number (personal data on a lock screen).

### OQ-037: Grievance module — operational decisions to confirm (2026-10-22)
- **`FileStorage__BasePath` with R2:** `Program.cs` still requires an absolute `FileStorage:BasePath` outside Development even when R2 is the provider, though R2 never uses it. Keep, or make the check conditional on R2 not being configured?
- **Rate limiting:** the `grievance-write` policy (10 per minute) is keyed by client IP because the limiter runs before authentication and unvalidated token contents would be forgeable. Staff behind one NAT share a bucket, and behind the Render proxy all clients share one unless `ForwardedHeaders` is configured. A per-user limit would need the limiter after `UseAuthorization` (which would stop it counting unauthenticated requests) or an action filter. Also, a 429 (like a 401 or 403 from the framework) has an empty body, against the "no blank responses" rule.
- **401 versus 403:** a token of the wrong kind (a mobile token on an admin route and the reverse) is 401 by design (separate schemes and audiences, ADR-017); 403 is only for an authenticated admin of another role. Confirm this is acceptable for the clients and the VAPT.
- **Malware scanning:** uploads are checked for type and signature but not scanned for malware. Required?
- **Admin visibility of identity:** Grievance Admins see employee name, NSG number and unit (audited per view). Confirm the legal basis, who may hold the role, and the audit retention period.
- **Workflow gaps:** one forward-only path (no re-open, no rejection, no withdrawal by the employee), no escalation, no SLA per transition, documents cannot be added after creation or removed. Which of these does the client need?
- **Seed values:** the optional category seed (`tools/sql/seed-grievance-categories.sql`) inserts only the five category names; the upload limits are fixed in code (OQ-038).
- **Not yet verified:** repository SQL on MySQL, the migration, real R2, and Kestrel's behaviour for a request over the 16 MB cap (see `GRIEVANCE-MODULE.md` section 9).

### OQ-038: Grievance module audit — decisions taken and to confirm (2026-10-22)
- **Decided by the owner:** the unapplied grievance migration was regenerated (not corrected by a second migration), and the module keeps the project-wide IST timestamp convention (`created_at_ist`, `CreatedAtIst`) instead of the UTC names in the audit brief.
- **Fixed upload limits (not configurable per category any more):** at most 3 files of 5 MB, PDF/JPEG only, description at least 10 characters. These are the previous defaults; the client has not approved them. Changing them is a code constant (`GrievanceLimits`).
- **Single creation call is multipart only.** A JSON create no longer exists; attachments can only be added at creation. Confirm that no client needs to add documents later.
- **"Description":** the creation call keeps both `subject` (required, 200 characters) and `note` (the description). Confirm whether the app wants a subject at all.
- **Default assignee:** new grievances are unassigned until an admin forwards them. Confirm whether a default Grievance Admin (or HQ) should receive new grievances automatically.
- **Comments are visible to the employee.** There are no internal-only admin notes. Confirm.
- **Attachments live in `grievance.document_urls` (JSON)** rather than a table of their own; the keys and metadata are in MySQL but are not individually queryable.
- **Dashboard:** kept (not in the audit brief's endpoint list) minus the SLA count that depended on removed category metadata. Confirm it is still wanted.
- **Machine-readable error codes** (`title` such as `TOKEN_EXPIRED`) are no longer in error bodies since the global envelope has only `success`, `message` and `data`.

### OQ-039: DG Message module — decisions taken and to confirm (2026-10-22)
- **Single message or list:** no requirement document says whether the app shows one "current" DG message. `GET /api/v1/dg-messages` therefore follows the list convention (paged, newest first, active and not deleted only); the app can show the first item. Change to "latest active only" if the client wants exactly one.
- **Role:** the admin endpoints use the existing `RequireMediaCellAdmin` policy (Media Cell Admin). Confirm that the DG message belongs to Media Cell and not to another role.
- **No admin list:** only the four requested endpoints exist. An admin cannot list inactive messages (to find an id to reactivate one); add `GET /api/v1/admin/dg-messages` if needed.
- **Image delivery:** the bucket stays private; the app reads the image with `GET /api/v1/dg-messages/{id}/image` and its normal bearer token (JPEG or PNG, at most 5 MB, checked by extension, content type and file signature). The response exposes only `hasImage`, never the key or a URL. A soft delete keeps the object in R2; a replaced image is deleted.
- **Message length:** up to 5000 characters with line breaks kept (paragraphs); stored as MySQL `text`.
- **Timestamps** use the project's IST convention (`created_at_ist`, `updated_at_ist`), not the UTC names in the brief.
- **Not yet verified:** the migration `Add_Dg_Message` (generated, not applied), the repository SQL on MySQL and real R2.

### OQ-040: Leave Management — value rules to confirm (2026-10-26)
- **Type and range of `leave_taken`:** the brief says "count", so it is a whole number stored as `int`, accepted from 0 to 366 (a calendar year has at most 366 days). No document says whether half days exist or whether the figure may exceed a year (for example accumulated or encashed leave). If half days are needed the column and the validation change to `decimal(5,1)`.
- **Which people:** a leave record needs an **active** `users_pis` row. Confirm that retired, repatriated or otherwise inactive personnel must not receive leave entries.
- **NSG number format:** no approved format exists, so only "required, at most 30 characters, no spaces or control characters" is checked before the lookup in `users_pis`. Spreadsheet users should store the number as text so leading zeros survive.
- **Corrections to past years:** the API only ever writes the current calendar year (as required). A mistake in a previous year can only be fixed directly in the database or by a future, separately authorised endpoint.
- **Unchanged totals:** writing the stored value again (manual or in a file) is accepted but changes nothing and writes no history row.
- **Excel limits:** 2 MB, 5000 rows, first worksheet only, `.xlsx` only (configuration section `Leave`). Confirm the real workbook size and whether a downloadable template endpoint is wanted (none exists).

### OQ-041: Leave Management — missing record is "not found", not zero (2026-10-26)
`GET /api/v1/leave/my-record` answers 404 with a message ("No leave record has been entered for you for 2026 yet.") when no record exists for the current year. It does not return `leaveTaken = 0` because no document says that "no record" means "no leave taken", and the endpoint never fabricates a row. Confirm the business rule; if a missing record does mean zero, the service returns 200 with `leaveTaken: 0` and no `lastUpdatedAt`. The mobile app must handle the 404 (for example by showing "not updated yet") either way, and at the start of every new year every user is in this state until HQ uploads the new totals.

### OQ-042: Leave Management — operational points (2026-10-26)
- **Role:** `LEAVE_ADMIN` is a new seventh role. Super Admin is deliberately **not** a Leave Admin; the owner must provision at least one Leave Admin account (`POST /api/v1/admin/accounts`, `roleCode: LEAVE_ADMIN`) once the migration is applied.
- **Failed batches in the envelope:** the response envelope has `data: null` on failures, so a rejected upload reports its batch id in the message and the row-level report is read from `GET /api/v1/admin/leave-uploads/{id}/errors`.
- **A batch can stay `Processing`** if the host dies or the database outcome cannot be confirmed; it blocks other uploads for `Leave:StaleBatchMinutes` (default 15), then the next upload closes it as `ProcessingFailed`. Someone must check the leave records of such a batch before re-uploading.
- **Audit retention** of `personnel_leave_history` and the batch tables is undefined (nothing is purged).
- **Not yet verified:** the migration `Add_Leave_Management` is applied to the local development database `nsg_mobile_dev` only (2026-10-26, structure confirmed by MySQL); all repository SQL, whether the CHECK constraints reject bad rows, the transaction rollback, races and the upload lock are untested on MySQL. `LeaveDatabaseTests` exist but have not been run (see `docs/LEAVE-MANAGEMENT.md`, sections 8 and 8.1). No `LEAVE_ADMIN` account exists yet.
