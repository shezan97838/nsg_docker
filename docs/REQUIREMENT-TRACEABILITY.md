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
| Category-based submission with attachments | GrievanceService, GrievanceCategoryService, GrievanceDocumentRules, IFileStorageService (R2FileStorageService, ADR-018) | GrievanceCategory, Grievance | GET /api/v1/grievance-categories, POST /api/v1/grievances (multipart, the only creation call) | RequireMobileProfile; ticket GRV-YYYY-000001; user from token; pdf/jpg only; extension + content type + magic bytes; max 3 files of 5 MB; 16 MB request cap; private bucket; all-or-nothing storage with orphan clean-up | Implemented (fake-tested; not smoke-tested) |
| Category master maintenance | GrievanceCategoryService | GrievanceCategory | /api/v1/admin/grievance-categories (GET, GET by id, POST, PUT, DELETE = soft delete) | RequireGrievanceAdmin; real columns (name, department, is_active, is_deleted, created/updated by and at); deleted categories leave the dropdown and keep their grievances | Implemented (fake-tested; not smoke-tested) |
| Document download | GrievanceDocumentAccess | — | GET /api/v1/grievances/{id}/documents/{index}, GET /api/v1/admin/grievances/{id}/documents/{index} | Owner or Grievance Admin; streamed, no URL; nosniff; every download audited | Implemented (fake-tested; R2 not smoke-tested) |
| Forwarding / re-assignment | GrievanceAdminService | Grievance.assigned_to, GrievanceStatusLog | POST /api/v1/admin/grievances/{id}/forward | Target must be an active GRIEVANCE_ADMIN; recorded in the append-only history; refused on a closed grievance | Implemented (fake-tested; not smoke-tested). HQ escalation open (OQ-011, OQ-038) |
| Admin comments | GrievanceAdminService | GrievanceStatusLog | POST /api/v1/admin/grievances/{id}/comments | Visible to the employee in the history; refused on a closed grievance | Implemented (fake-tested; not smoke-tested) |
| Status tracking workflow (take action, resolve) and complete history | GrievanceAdminService, GrievanceService | Grievance, GrievanceStatusLog | GET /api/v1/grievances/{id}, PUT /api/v1/admin/grievances/{id}/status | Only 1→2→3→4; remarks required for Action Taken and for Closed (resolved); one transaction with the log row; history append-only | Implemented (fake-tested; not smoke-tested) |
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
| Admin Portal accounts (schema only) | admin_roles (6 fixed roles), admin_profiles; see ADR-016 | Schema only: no endpoints |
| Admin Portal accounts: provisioning, PIS-verified OTP activation, password + OTP sign-in, refresh rotation, logout, deactivation, role policies (SOW §3.8) | AdminAccountService, AdminActivationService, AdminAuthService, AdminBootstrapService; AdminAccountsController, AdminActivationController, AdminAuthController | AdminRole, AdminProfile, AdminRefreshToken (`admin_roles`, `admin_profiles`, `admin_refresh_tokens`) | `api/v1/admin/accounts`, `api/v1/admin/activation/*`, `api/v1/admin/auth/*`, `api/v1/admin/roles`; see ADR-017 | Separate `AdminBearer` scheme and audience; per-request session check; Super Admin only for account management; rate limited; audited | **Implemented; migration `Add_Admin_Refresh_Tokens` not yet applied to dev** |

## Module 10: DG Message

Implemented 2026-10-22; decisions in OQ-039. Covered by `DgMessageTests` (fakes only); the migration `Add_Dg_Message` is generated but **not applied**, and nothing was run against MySQL or R2.

| Requirement | Component | Entity | API | Security | Status |
|-------------|-----------|--------|-----|----------|--------|
| Create / update / soft-delete DG message with optional image | DgMessageService, DgMessageRepository, IFileStorageService (R2) | DgMessage (`dg_message`) | POST, PUT, DELETE /api/v1/admin/dg-messages | RequireMediaCellAdmin; admin from token; JPEG/PNG, 5 MB, magic bytes; private bucket; ids-only audit | Implemented (fake-tested; not smoke-tested) |
| Show active DG messages to employees | DgMessageService | DgMessage | GET /api/v1/dg-messages, GET /api/v1/dg-messages/{id}/image | RequireMobileProfile; inactive and deleted never returned; image streamed, no URL | Implemented (fake-tested; not smoke-tested) |
