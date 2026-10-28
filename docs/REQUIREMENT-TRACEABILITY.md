# NSG Mobile API — Requirement Traceability Matrix

> Maps SOW requirements → architecture components → database entities → API endpoints → security controls.

## Module 1: Personal Information System (PIS) — SOW §3.1

| SOW Requirement | Component | Entity | API | Security | Status |
|-----------------|-----------|--------|-----|----------|--------|
| Display employee personal details | ProfileController, MobileProfileService | UserPis (via NSG number) | GET /api/v1/profile | Mobile token + session check, identity from token only | **Foundation** |
| PIS data from centralized HRMS | IHrmsService | — | — | Integration abstraction | **Stub** |
| Read-only for users | MobileProfileResponseDto | UserPis | GET /api/v1/profile | DTO projection, no write endpoint | **Foundation** |
| PIS lookup: NSG unit master (admin CRUD, dropdown) | NsgUnitService, NsgUnitsController | NsgUnit (`nsg_units`) | GET/POST/PUT/DELETE /api/v1/nsg-units, GET /api/v1/nsg-units/dropdown | Bearer auth; writes `RequireITAdmin`; delete blocked (409) while referenced by `users_pis`; audited | **Done** (API only; value list not supplied — OQ-018) |
| PIS lookup: parent department rank master (admin CRUD, dropdown) | ParentDepartmentRankService, ParentDepartmentRanksController | ParentDepartmentRank (`parent_department_ranks`) | GET/POST/PUT/DELETE /api/v1/parent-department-ranks, GET /api/v1/parent-department-ranks/dropdown | Bearer auth; writes `RequireITAdmin`; delete blocked (409) while referenced by `users_pis`; audited | **Done** (API only; value list not supplied — OQ-018) |
| PIS lookup: religion master (admin CRUD, dropdown) | ReligionService, ReligionsController | Religion (`religions`) | GET/POST/PUT/DELETE /api/v1/religions, GET /api/v1/religions/dropdown | Bearer auth; writes `RequireITAdmin`; delete blocked (409) while referenced by `UsersPIS.ReligionId`; audited | **Done** (API only; value list not supplied — OQ-018) |
| PIS personnel master records (admin CRUD; deactivate instead of delete) | UserPisService, UsersPisController | UserPis (`users_pis`) | GET/POST /api/v1/users-pis, GET/PUT/DELETE /api/v1/users-pis/{id} | Bearer auth; every endpoint `RequireITAdmin`; Aadhaar/PAN/PRAN/salary account encrypted, returned masked only; PAN and salary account unique via blind index; NSG and mobile unique; audited without personnel data | **Done** (API only; mobile activation approval not included) |
| Mobile registration, OTP, MPIN, login (SOW §3.7/§3.8) | MobileAuthService | MobileUserProfile (`mobile_user_profiles`), ApproveMobileUser (`approve_mobile_users`), MobileAuthGrant (`mobile_auth_grants`), OtpRequest | POST /api/v1/mobile-auth/{login/check,login,otp/send,otp/verify,registration/submit,mpin/setup,mpin/change,mpin/reset} | See ADR-015: PBKDF2 MPIN, lockout, OTP limits, single-use hashed grants, JWT only after MPIN | **Implemented, not run against MySQL**; no real SMS gateway; no refresh token/device binding |
| Mobile sessions (refresh tokens, logout, revocation) | MobileAuthService | MobileRefreshToken (`mobile_refresh_tokens`) | POST /api/v1/mobile-auth/{token/refresh,logout} | Hash-only storage, rotation, reuse detection, per-request session check; see ADR-015 addendum 2 | **Implemented; MySQL integration tests pass; not run on Ubuntu** |
| Mobile approval by Registration Admin | MobileApprovalService | ApproveMobileUser, UserPis | GET /api/v1/mobile-approval-requests, PUT /api/v1/mobile-approval-requests/{id}/decision | `RequireRegistrationAdmin` (REGISTRATION_ADMIN); one transaction updates request and `users_pis` status; first decision wins | **Implemented, not run against MySQL** |
| PIS trade lookup: NSG trade (independent lookup; admin CRUD, deactivate instead of delete) | NsgTradeService, NsgTradesController | NsgTrade (`nsg_trade`: id, nsg_trade_name, created_at_ist, is_active) | GET/POST /api/v1/nsg-trades, GET/PUT/DELETE /api/v1/nsg-trades/{id} | Bearer auth; writes `RequireITAdmin`; unique name; 409 on duplicate or when deactivating a trade used by personnel; audited | **Done** (applied to development; value list not supplied — OQ-026) |
| PIS trade lookup: parent department trade (independent lookup; admin CRUD, deactivate instead of delete) | PdTradeService, PdTradesController | PdTrade (`pd_trade`: id, pd_trade_name, created_at_ist, is_active) | GET/POST /api/v1/pd-trades, GET/PUT/DELETE /api/v1/pd-trades/{id} | Bearer auth; writes `RequireITAdmin`; unique name; 409 on duplicate or when deactivating a trade used by personnel; audited | **Done** (applied to development; value list not supplied — OQ-026) |
| PIS lookup: education qualification (independent lookup; admin CRUD, deactivate instead of delete; follows the no-blank-response rule) | EducationQualificationService, EducationQualificationsController | EducationQualification (`education_qualifications`: id, course_name, created_at_ist, is_active; migration `Rework_EducationQualifications_To_CourseName` renamed `name` → `course_name` and dropped the unused `updated_at_utc`) | GET/POST /api/v1/education-qualifications, GET/PUT/DELETE /api/v1/education-qualifications/{id} | Bearer auth; writes `RequireITAdmin`; unique course name; 409 on duplicate or when deactivating a qualification used by personnel; empty list, create, update and deactivate answer with a message; audited | **Done** (applied to development and test; not production; value list not supplied) |
| PIS personnel: NSG trade and parent department trade on the record (optional, independent) | UserPisService | UserPis (`NsgTradeId` → column `nsg_trade_id`, `PdTradeId` → column `pd_trade_id`) | POST/PUT /api/v1/users-pis (`nsgTradeId`, `pdTradeId`); GET responses carry trade ids and names | Trade must exist when supplied; no rank/trade matching; restrict FKs | **Done** (OQ-026) |
| Change requests with approval | — | — | POST /api/v1/personal-information/change-requests | Audit trail | **Phase 2** |
| Card-based UI | Mobile app concern | — | — | — | N/A (Mobile) |

## Module 2: Leave Management — SOW §3.2

| SOW Requirement | Component | Entity | API | Security | Status |
|-----------------|-----------|--------|-----|----------|--------|
| View current leave balance | IHrmsService | — | GET /api/v1/leave/balance | Bearer auth | **Phase 2** |
| View leave transaction history | — | — | GET /api/v1/leave/history | Pagination, auth | **Phase 2** |
| Monthly automatic update | — | — | — | HRMS sync job | **Phase 2** |
| Admin bulk data upload | — | — | POST /api/v1/leave/bulk-upload | ITAdmin policy | **Phase 2** |

## Module 3: Salary Integration — SOW §3.3

| SOW Requirement | Component | Entity | API | Security | Status |
|-----------------|-----------|--------|-----|----------|--------|
| Integrate with Payroll API | IPayrollService | — | — | Typed HttpClient | **Stub** |
| Display monthly salary breakdown | — | — | GET /api/v1/salary/slips | Bearer auth, ownership | **Phase 2** |
| Salary slip XML/Image format | IFileStorageService | — | GET /api/v1/salary/slips/{id} | File security | **Phase 2** |
| Archive access (7yr vs 24mo TBD) | — | — | — | Configurable retention | **OQ-001** |

## Module 4: Form-16 — SOW §3.4

| SOW Requirement | Component | Entity | API | Security | Status |
|-----------------|-----------|--------|-----|----------|--------|
| Fetch Form-16 from payroll | IPayrollService | — | GET /api/v1/form16 | Bearer auth | **Phase 2** |
| PDF viewer/download | IFileStorageService | — | GET /api/v1/form16/{id} | Ownership, audit | **Phase 2** |
| 7-year archive | — | — | — | Configurable retention | **Phase 2** |

## Module 5: Engagement & Communications — SOW §3.5

| SOW Requirement | Component | Entity | API | Security | Status |
|-----------------|-----------|--------|-----|----------|--------|
| Push notifications (FCM/APNs) | IPushNotificationService | Device.PushToken | — | FCM integration | **Stub** |
| Admin-controlled broadcasts | — | — | POST /api/v1/notifications/broadcast | Admin policy | **Phase 2** |
| Targeted by Office/Group/Rank | — | — | — | Topic-based targeting | **Phase 2** |
| Newsletters/Circulars | — | — | GET /api/v1/communications/circulars | Auth, download audit | **Phase 2** |
| Events Calendar | — | — | GET /api/v1/events | Auth | **Phase 2** |
| Quick Polls/Surveys | — | — | — | — | **Phase 2** |
| Feedback/Suggestions | — | — | — | Optional anonymity | **Phase 2** |

## Module 6: Digital Library — SOW §3.6

| SOW Requirement | Component | Entity | API | Security | Status |
|-----------------|-----------|--------|-----|----------|--------|
| Categorized document repository | IFileStorageService | — | GET /api/v1/library | Rank/role access control | **Phase 2** |
| Full-text search | — | — | GET /api/v1/library/search | Auth | **Phase 2** |
| Offline access/download | — | — | GET /api/v1/library/{id}/download | Auth, audit | **Phase 2** |
| Admin upload | — | — | POST /api/v1/library | Admin policy | **Phase 2** |
| Version history | — | — | — | — | **Phase 2** |

## Module 7: Secure Onboarding — SOW §3.7

| SOW Requirement | Component | Entity | API | Security | Status |
|-----------------|-----------|--------|-----|----------|--------|
| Submit Name, Rank, ForceNo, Mobile | MobileAuthService | User | POST /api/v1/mobile-auth/registration/submit | Validation, rate limit | ✅ **Done** |
| Mobile OTP via NIC SMS | OtpService, INicSmsService | OtpRequest | — | Hash storage, attempt limit | ✅ **Done** |
| OTP verified | MobileAuthService | OtpRequest | POST /api/v1/mobile-auth/otp/verify | Purpose isolation, expiry | ✅ **Done** |
| IT Cell approval | OnboardingService | User | POST /api/v1/onboarding/approve/{id} | ITAdmin policy, audit | ✅ **Done** |
| Identity verification vs HRMS | IHrmsService | — | — | Integration abstraction | **Stub** |
| User sets password | MobileAuthService | User.PasswordHash | POST /api/v1/mobile-auth/mpin/setup | PBKDF2, never plaintext | ✅ **Done** |
| Biometric setup on device | Mobile app concern | — | — | Device-bound, no raw biometric | ✅ **Arch** |
| Device binding (IMEI+token) | DeviceRepository | Device | Login flow | Hash identifier, single device | ✅ **Done** |

## Module 8: Security / Authentication — SOW §3.8

| SOW Requirement | Component | Entity | API | Security | Status |
|-----------------|-----------|--------|-----|----------|--------|
| MFA: Password + OTP + Biometric | MobileAuthService | User, OtpRequest | POST /api/v1/mobile-auth/login | Layered auth | ✅ **Done** |
| JWT/OAuth 2.0 | JwtTokenService | — | — | HMAC-SHA256, 15-min expiry | ✅ **Done** |
| Configurable session timeout | JwtOptions | RefreshToken | — | AccessTokenExpirationMinutes | ✅ **Done** |
| Single device binding | DeviceRepository | Device | Login flow | Revoke old, bind new | ✅ **Done** |
| IT Cell approval before activation | OnboardingService | User.Status | POST /api/v1/onboarding/approve | State machine | ✅ **Done** |
| Secure logout | MobileAuthService | RefreshToken | POST /api/v1/mobile-auth/logout | Token revocation | ✅ **Done** |
| HTTPS with TLS 1.2+ | Program.cs | — | — | RequireHttps, HSTS | ✅ **Done** |
| AES-256 encryption at rest | AesEncryptionService | — | — | Key from secure config | ✅ **Done** |
| No plaintext sensitive data on device | Mobile app concern | — | — | — | N/A (Mobile) |
| Comprehensive audit logging | AuditService | AuditLog | — | All security events | ✅ **Done** |

## Module 9: Grievance Management — SOW §3.8

Implemented 2026-10-22; see `docs/GRIEVANCE-MODULE.md`. **Status legend for this module:** "Implemented" means code complete and covered by automated tests that use fakes only. **None of it has been smoke-tested against a real MySQL database or a real R2 bucket**, and the migration `Add_Grievance_Management` (regenerated 2026-10-22) has not been applied anywhere.

| SOW Requirement | Component | Entity | API | Security | Status |
|-----------------|-----------|--------|-----|----------|--------|
| Category-based submission with attachments | GrievanceService, GrievanceCategoryService, GrievanceDocumentRules, IFileStorageService (R2FileStorageService, ADR-018) | GrievanceCategory, Grievance | GET /api/v1/grievance-categories, POST /api/v1/grievances (multipart, the only creation call) | RequireMobileProfile; ticket NSG-YYYY-000001; user from token; pdf/jpg only; extension + content type + magic bytes; max 3 files of 5 MB; 16 MB request cap; private bucket; all-or-nothing storage with orphan clean-up | Implemented (fake-tested; not smoke-tested) |
| Category master maintenance | GrievanceCategoryService | GrievanceCategory | /api/v1/admin/grievance-categories (GET, GET by id, POST, PUT, DELETE = soft delete) | RequireGrievanceAdmin; real columns (name, department, is_active, is_deleted, created/updated by and at); deleted categories leave the dropdown and keep their grievances | Implemented (fake-tested; not smoke-tested) |
| Document download | GrievanceDocumentAccess | — | GET /api/v1/grievances/{id}/documents/{index}, GET /api/v1/admin/grievances/{id}/documents/{index} | Owner or Grievance Admin; streamed, no URL; nosniff; every download audited | Implemented (fake-tested; R2 not smoke-tested) |
| Forwarding / re-assignment | GrievanceAdminService | Grievance.assigned_to, GrievanceStatusLog | POST /api/v1/admin/grievances/{id}/forward | Target must be an active GRIEVANCE_ADMIN; recorded in the append-only history; refused on a closed grievance | Implemented (fake-tested; not smoke-tested). HQ escalation open (OQ-011, OQ-038) |
| Admin comments | GrievanceAdminService | GrievanceStatusLog | POST /api/v1/admin/grievances/{id}/comments | Visible to the employee in the history; refused on a closed grievance | Implemented (fake-tested; not smoke-tested) |
| Status tracking workflow (take action, resolve) and complete history | GrievanceAdminService, GrievanceService | Grievance, GrievanceStatusLog | GET /api/v1/grievances/{id}, PUT /api/v1/admin/grievances/{id}/status | Only 1→2→3→4; remarks required for Action Taken; closing only through the resolve call (below); one transaction with the log row; history append-only | Implemented (fake-tested; not smoke-tested) |
| Resolution with comment and supporting file (added 2026-10-26) | GrievanceAdminService.ResolveAsync, GrievanceService.OpenMyResolutionDocumentAsync, GrievanceDocumentAccess | Grievance.resolution_document varchar(500) (migrations Add_Grievance_Resolution_Document, applied to the local dev DB only, then Change_Grievance_Resolution_Document_To_Varchar, not applied) | POST /api/v1/admin/grievances/{id}/resolve (multipart), GET /api/v1/admin/grievances/{id}/resolution-document, GET /api/v1/grievances/{id}/resolution-document | RequireGrievanceAdmin to resolve; comment and one PDF/JPEG/PNG (max 5 MB, signature checked) both required; stored in private storage, removed again if recording fails; owner downloads through the API only (404 for others), every download audited | Implemented (host- and fake-tested; MySQL and R2 not smoke-tested) |
| Reviewing officer list and detail | GrievanceAdminService | — | GET /api/v1/admin/grievances, GET /api/v1/admin/grievances/{id} | RequireGrievanceAdmin; employee name, NSG number, unit shown; each view audited | Implemented (fake-tested; not smoke-tested) |
| Reviewing officer dashboard | GrievanceDashboardService | — | GET /api/v1/admin/dashboard/summary | RequireGrievanceAdmin | Implemented (fake-tested; queries not run on MySQL) |
| Push notification on status change | — | — | — | — | **Not implemented** (FCM is a placeholder; OQ-036) |
| Audit of grievance actions | IAuditService | AuditLog | — | Ids only: GrievanceCreated, GrievanceStatusChanged, GrievanceDocumentUploaded, GrievanceCommentAdded, GrievanceForwarded, GrievanceCategoryChanged, GrievanceViewed, DocumentDownloaded | Implemented |

## Cross-Cutting Concerns

| Requirement | Implementation | Status |
|-------------|---------------|--------|
| 10,000+ users | Pagination, async, indexes, Redis | ✅ |
| RESTful API with Swagger | OpenAPI v1 docs, Bearer auth | ✅ |
| Rate limiting | ASP.NET Core rate limiter (global + per-policy) | ✅ |
| Health checks | /health/live, /health/ready (MySQL) | ✅ |
| Structured logging | Serilog with correlation ID | ✅ |
| Error handling | ProblemDetails (RFC 7807) | ✅ |
| Security headers | HSTS, X-Content-Type-Options, CSP, etc. | ✅ |
| CERT-In VAPT readiness | OWASP API alignment, audit trail | ✅ (Architecture) |
| Admin Portal accounts (schema only) | admin_roles (6 fixed roles, a 7th LEAVE_ADMIN added for Leave Management), admin_profiles; see ADR-016 | Schema only: no endpoints |
| Admin Portal accounts: provisioning, PIS-verified OTP activation, password + OTP sign-in, refresh rotation, logout, deactivation, role policies (SOW §3.8) | AdminAccountService, AdminActivationService, AdminAuthService, AdminBootstrapService; AdminAccountsController, AdminActivationController, AdminAuthController | AdminRole, AdminProfile, AdminRefreshToken (`admin_roles`, `admin_profiles`, `admin_refresh_tokens`) | `api/v1/admin/accounts`, `api/v1/admin/activation/*`, `api/v1/admin/auth/*`, `api/v1/admin/roles`; see ADR-017 | Separate `AdminBearer` scheme and audience; per-request session check; Super Admin only for account management; rate limited; audited | **Implemented; migration `Add_Admin_Refresh_Tokens` not yet applied to dev** |

## Module 10: DG Message

Implemented 2026-10-22; decisions in OQ-039. Covered by `DgMessageTests` (fakes only); the migration `Add_Dg_Message` is generated but **not applied**, and nothing was run against MySQL or R2.

| Requirement | Component | Entity | API | Security | Status |
|-------------|-----------|--------|-----|----------|--------|
| Create / update / soft-delete DG message with optional image | DgMessageService, DgMessageRepository, IFileStorageService (R2) | DgMessage (`dg_message`) | POST, PUT, DELETE /api/v1/admin/dg-messages | RequireMediaCellAdmin; admin from token; JPEG/PNG, 5 MB, magic bytes; private bucket; ids-only audit | Implemented (fake-tested; not smoke-tested) |
| Show active DG messages to employees | DgMessageService | DgMessage | GET /api/v1/dg-messages, GET /api/v1/dg-messages/{id}/image | RequireMobileProfile; inactive and deleted never returned; image streamed, no URL | Implemented (fake-tested; not smoke-tested) |

### DG Message: Hindi text (added 2026-10-23)

Optional `messageHindi` (nullable `message_hindi`, `text`, `utf8mb4`) on `dg_message`, accepted by POST/PUT `/api/v1/admin/dg-messages` (multipart field `messageHindi`, trimmed, blank stored as `NULL`, max 5000 characters like `message`) and returned by GET `/api/v1/dg-messages`. Migration `20261023110812_Add_Dg_Message_Hindi` (adds the column only).

- **Verified 2026-10-23:** migration applied to the local development database `nsg_mobile_dev` (127.0.0.1:3306); the existing row kept `message_hindi = NULL`; a Devanagari string round-tripped exactly (rolled-back transaction); `DgMessageTests` (unit, in-memory) and `DgMessageHindiDatabaseTests` (MySQL, database `nsg_mobile_test`) pass.
- **TEMPORARY, security risk:** POST, PUT and DELETE `/api/v1/admin/dg-messages` carry `[AllowAnonymous]` (the `RequireMediaCellAdmin` attribute is commented out in `DgMessagesController`). Anyone who can reach the API can create, change and delete DG messages. Restore the attribute and re-add the three routes to `AdminRoutes` in `GrievanceRouteSecurityTests` before real use. GET is unchanged (`RequireMobileProfile`).
- **Not verified:** the Hindi migration has not been applied to any shared or production database; image upload/replacement was covered by unit tests with fake storage only (no R2).

## Module 11: Leave Management

Implemented 2026-10-26; guide `LEAVE-MANAGEMENT.md`, decisions ADR-019, open questions OQ-040 to OQ-042. Covered by `LeaveExcelReaderTests`, `LeaveServiceTests`, `LeaveBulkUploadTests` and `LeaveAuthorizationHostTests` (fakes and an in-process host only). The migration `Add_Leave_Management` is **applied to the local development database `nsg_mobile_dev` only** (structure confirmed by MySQL, tables empty, no `LEAVE_ADMIN` account yet); `LeaveDatabaseTests` (real MySQL) exist but have **not been run**, so functional behaviour on MySQL is unverified.

| Requirement | Component | Entity | API | Security | Status |
|-------------|-----------|--------|-----|----------|--------|
| Manual create/update of the current-year leave total of a person | LeaveService, LeaveRepository | PersonnelLeaveRecord (`personnel_leave_records`), PersonnelLeaveHistory (`personnel_leave_history`) | POST /api/v1/admin/leave-records, PUT /api/v1/admin/leave-records/{nsgNumber}, GET .../{nsgNumber}, GET .../{nsgNumber}/history | RequireLeaveAdmin (LEAVE_ADMIN only); admin and year from server; NSG number checked against active `users_pis`; ids-only audit | Implemented (fake-tested; SQL not run on MySQL) |
| Excel bulk upload, all-or-nothing, durable batch and row-error report | LeaveBulkUploadService, LeaveExcelReader, LeaveRepository | PersonnelLeaveUploadBatch, PersonnelLeaveUploadError (`personnel_leave_upload_batches`, `personnel_leave_upload_errors`) | POST /api/v1/admin/leave-uploads, GET /api/v1/admin/leave-uploads, GET .../{batchId}, GET .../{batchId}/errors | RequireLeaveAdmin; .xlsx only, size/row caps, hardened parser (no DTD, capped parts); single running upload enforced by a unique column; no file content logged | Implemented (fake-tested; SQL and rollback not run on MySQL) |
| Mobile user reads own current-year leave (read only) | LeaveService | PersonnelLeaveRecord | GET /api/v1/leave/my-record | RequireMobileProfile; person from the token's profile only; no identifier accepted; no write route for mobile | Implemented (fake-tested); missing record is 404 (OQ-041) |
| New role `LEAVE_ADMIN` and policy | AdminRoleCodes, Program.cs, `admin_roles` seed row 7 | AdminRole | Provisioned by `POST /api/v1/admin/accounts` | Policy admits exactly this role; Super Admin not admitted | Implemented (migration applied to local dev DB only; role seeded, no account yet) |

## Module 12: Banner Management

Implemented 2026-10-28, finalized the same day (OQ-043). Covered by `BannerTests` (service, controller, contract, URL builder; fakes), `BannerAuthorizationHostTests` (real JWT, policies, multipart binding, file result and envelope; fake repositories and storage) and `BannerDatabaseTests` (MySQL; **only run with `NSG_TEST_MYSQL`, not yet run**). Migrations: `20261028092824_Add_Banners` (table), `20261028122148_Change_Banner_Image_To_Storage_Key` (URL → R2 key), `20261028180046_Remove_Banner_Schedule_And_Category` (drops `start_at_ist`, `end_at_ist`, `category` and their constraints/index, makes `priority` nullable, adds `ix_banners_is_active_priority`) `20261028182013_Restore_Banner_Category` (adds `category varchar(100) NOT NULL`) and `20261028184226_Add_Banner_Show_Only_Image` (adds `show_only_image tinyint(1) NOT NULL DEFAULT 0` and `ck_banners_show_only_image`: the flag may be 1 only when `image_storage_key` is set).

Final fields: `id`, `title`, `description`, `category` (required free text, up to 100 characters, restored 2026-10-28 by `20261028182013_Restore_Banner_Category`), optional image (`image_storage_key`), `image_alt_text`, `show_only_image`, `nsg_unit_id`, `priority` (nullable), `is_active`, `created_at_ist`, `updated_at_ist`. Visibility is manual (`is_active`); there is no schedule.

| Requirement | Component | API | Security / rules | Status |
|-------------|-----------|-----|------------------|--------|
| Create, update, activate/deactivate, list all, read one | BannerService, BannerRepository, SaveBannerRequestValidator, BannersController | POST, GET `api/v1/admin/banners`; GET, PUT `api/v1/admin/banners/{id}`; PATCH `api/v1/admin/banners/{id}/status` | `RequireMediaCellAdmin` (`MEDIA_CELL_ADMIN`); admin id from the token; `isActive` required; ids-only audit; `createdAt`/`updatedAt` set by the server (clients cannot send them) | Implemented; migrations applied to local `nsg_mobile_dev` |
| Image uploaded with the banner, stored privately | BannerService (`IFileStorageService`, `IStorageAvailability`, orphan clean-up) | POST/PUT multipart `image`, PUT `removeImage` | JPEG/PNG, 5 MB, extension + content type + magic bytes checked before upload; key `banners/yyyy/MM/guid.ext`; only the key is stored and it never appears in a response; `imageUrl`/`imageStorageKey` are not accepted from clients; replaced/removed object deleted; a failed write removes the new object; image + `removeImage` together is 400 | Implemented (fake-tested; real R2 not exercised by tests) |
| Employees see active banners | BannerService | GET `api/v1/banners` | `RequireMobileProfile`; active only, no date filter; order: `priority` ascending, null last, then highest id; fields `id,title,description,category,imageUrl,imageAltText,nsgUnitId,priority,isActive,createdAt,updatedAt` (`category` is the saved value, never computed; `showOnlyImage` is a JSON boolean, always present, and false whenever `imageUrl` is null) | Implemented (fake-tested) |
| Authenticated image retrieval | BannerService, `GrievanceFileResult` | GET `api/v1/banners/{id}/image` (active banners), GET `api/v1/admin/banners/{id}/image` (admin preview, any banner) | Streamed from private R2, no public or pre-signed link, `nosniff`, `no-store`; the app must send the bearer token | Implemented (fake-tested) |
| Absolute image URL | BannerImageUrlBuilder (`PublicApi:BaseUrl`, else the request's scheme, host and path base) | `imageUrl` in every response | Never hard-coded; a malformed configured value fails loudly; behind a proxy enable `ForwardedHeaders` or set `PublicApi:BaseUrl` | Implemented |
