# ADR-016: Admin Portal accounts (`admin_roles`, `admin_profiles`)

Status: Accepted (schema). Provisioning, activation and sign-in are built in ADR-017.
Date: 2026-10-18

## Context
The Admin Portal needs accounts that are separate from mobile users (`mobile_user_profiles`) and from the legacy staff table (`users`).
Personnel facts (NSG number, registered mobile number, date of birth) stay in `users_pis`, the authoritative source.

## Decision
- `admin_roles`: six fixed rows seeded by the migration `Add_Admin_Roles_And_Profiles` (ids 1-6): `SUPER_ADMIN`, `REGISTRATION_ADMIN`,
  `PIS_ADMIN`, `GRIEVANCE_ADMIN`, `MEDIA_CELL_ADMIN`, `SALARY_MANAGEMENT_ADMIN`. `role_code` is unique. No API creates, renames or
  deletes roles; no permission table exists (authorization is by role code through fixed policies). Constants: `AdminRoleCodes`.
- `admin_profiles`: one row per NSG number (unique), `role_id` -> `admin_roles.id` (Restrict), `password_hash` null until the person
  sets a password, `activation_status` (0 PendingActivation, 1 Active), `is_active`, lockout and last-login fields, audit fields.
  A check constraint guarantees an Active account has a password hash.
- **No mobile number or date of birth is stored.** The OTP destination and the DOB check are always read from `users_pis` at the time
  of use, so there is nothing to keep consistent when PIS changes.
- **No foreign key to `users_pis`**, as for `mobile_user_profiles` (ADR-014): a key on `nsg_number` would need an alternate key on the
  client-approved PIS table. Provisioning and activation must verify that the NSG number exists in PIS.
- **Timestamps are IST (`*_ist`)**, not UTC: `DatabaseNamingStandard` and `DatabaseNamingRuleTests` require it for every `DateTime`.
  Columns: `created_at_ist`, `updated_at_ist`, `lockout_end_ist`, `last_login_at_ist`.
- `created_by` / `updated_by` are `bigint` ids of admin profiles (no FK, like the other audit columns); null for the bootstrap account.

## Initial Super Admin bootstrap (documented procedure, no code yet)
1. (Superseded by ADR-017: the operator runs the `bootstrap-super-admin` command instead of hand-writing SQL.) Originally: an authorized database operator, with the Super Admin NSG number confirmed against `users_pis`, inserted one `admin_profiles` row: `role_id = 1`, `activation_status = 0`, `password_hash = NULL`, `is_active = 1`, `created_by = NULL`; never in a migration and never at startup.
2. The person activates the account through the normal flow (OTP to the PIS-registered mobile, then they choose their own password).
3. From then on, all admin accounts are created by an authenticated Super Admin through the application.
There is no default password and no credential in source or configuration.

## Not in this change / open
- (Done in ADR-017) Provisioning, OTP activation, login (password + OTP), refresh and logout endpoints.
- (Done in ADR-017) Token separation: own audience, own scheme, own refresh-token table.
- DB-level protection of the fixed roles (e.g. triggers) was not added; protection is "no code path writes `admin_roles`".
- Whether `UsersPIS` rows referenced by an admin account may be deleted (no FK, so the database does not stop it).
