# Database Schema Audit

Audit date 2026-10-14. Standard applied: [DATABASE-STANDARDS.md](DATABASE-STANDARDS.md). Scope: every mapped entity, table, column, key, index, constraint, migration and SQL statement.

## 1. Environment checked
| Item | Value |
|---|---|
| MySQL server (development) | 8.0.44, Windows (`version_compile_os=Win64`) |
| `lower_case_table_names` / `lower_case_file_system` | **1 / 1** (Windows: table names are stored lowercase; Linux default is 0) |
| Storage engine, character set, collation | InnoDB, `utf8mb4`, `utf8mb4_0900_ai_ci` (server, database and every table) |
| `sql_mode` | `ONLY_FULL_GROUP_BY, STRICT_TRANS_TABLES, NO_ZERO_IN_DATE, NO_ZERO_DATE, ERROR_FOR_DIVISION_BY_ZERO, NO_ENGINE_SUBSTITUTION` |
| Reserved words | 262, read from `information_schema.KEYWORDS` |
| Provider / EF | Pomelo.EntityFrameworkCore.MySql 8.0.2, EF Core 8.0.11 |
| Raw SQL in production code | **none** (`src`, `tools`): LINQ/EF only. About 70 raw statements exist in tests only. |
| Seed data in code | none (removed earlier); reference data lives in the tables |
| Real Linux server available | **no**: Linux behaviour was checked statically (see section 6) |

Evidence that the hazard is real: on this server `information_schema.TABLES` lists `userspis`, `refreshtokens`, `nsgunits`, … in lowercase although the code and migrations say `UsersPIS`, `RefreshTokens`, `NsgUnits`.

## 2. Findings
Severity: **Critical** data-integrity/security risk · **High** likely runtime or migration failure · **Medium** inconsistent schema or portability risk · **Low** maintainability/naming.

| ID | Sev. | Finding | Status |
|---|---|---|---|
| H1 | High | 12 tables had mixed-case names (`AuditLogs`, `Cities`, `Devices`, `EducationQualifications`, `NsgUnits`, `OtpRequests`, `ParentDepartmentRanks`, `RefreshTokens`, `Religions`, `States`, `Users`, `UsersPIS`). On Linux (`lower_case_table_names=0`) a Windows dump restores them lowercase and the application cannot find them; hand-written scripts and BI tools would also break. | **Fixed** (migration applied to development 2026-10-14) |
| H2 | High | `UserPisRepository` decides *which* unique rule fired by searching MySQL's error text for index names (`IX_UsersPIS_NsgNumber` …). Any index rename silently turns a 409 into a 500. | **Fixed** (names updated, matched case-insensitively; covered by `UsersPisApiTests`) |
| M1 | Medium | `users.Rank` column is the MySQL 8.0 reserved word `RANK`. EF quotes it, but any hand-written SQL fails. | **Fixed**: column is `rank_name` (C# property and JSON unchanged) |
| M2 | Medium | Mixed timestamp naming: `CreatedAt`, `ExpiresAt`, `LastLoginAt` … (no UTC marker) next to `CreatedAtUtc`. All values are UTC (`DateTime.Now` is never used), so the names were misleading. | **Fixed**: every `DateTime` column ends in `_utc` |
| M3 | Medium | Collation is not pinned in the model; it depends on the server default `utf8mb4_0900_ai_ci`. A Linux server configured with another default would change unique-index and sort behaviour. | **Open**: verify the target server before first deployment; pinning needs approval |
| M4 | Medium | `__EFMigrationsHistory` (+ `MigrationId`, `ProductVersion`) is mixed case and EF-managed. Fine when Linux databases are built by running the migrations; broken by restoring a Windows schema dump. | **Open** (documented exception, deployment rule in section 5) |
| M5 | Medium | Enum columns without a `CHECK`: `users.status`, `users.role`, `devices.platform`, `devices.status`, `otp_requests.purpose`, `cities.classification`. (`states`, `users_pis` already have them.) Only the application guards these values. | **Open** (defence in depth; needs approval, tables are mostly empty) |
| M6 | Medium | Names of keys, indexes, foreign keys and checks were a mix of `IX_Table_Col` Pascal-case and lowercase. | **Fixed**: `pk_`/`ix_`/`fk_`/`ck_` snake_case, derived in one place |
| L1 | Low | 160+ PascalCase column names. | **Fixed** (rename only, data untouched) |
| L2 | Low | `rank_hierarchy` has a unique index on `(sr_no, rank_name)` that is redundant, because both columns are already unique. | **Open** (harmless; drop on approval) |
| L3 | Low | `users.row_version` is `timestamp(6)`: session time-zone dependent and ends in 2038. It is only a concurrency token. | Noted |
| L4 | Low | Unique columns (`nsg_number`, `force_number`, `mobile_number`, names) compare case- and accent-insensitively (`_ai_ci`): `N-1` and `n-1` collide. Intended for names; confirm for identifiers. | Noted (business decision) |
| L5 | Low | `created_by`, `updated_by`, `approved_by` and `audit_logs.user_id/device_id` have no foreign key (audit-style references that must survive deletion). Orphan ids are possible by design. | Noted |
| L6 | Low | Hash check constraints use `REGEXP '^[0-9a-f]{64}$'`; under a case-insensitive collation uppercase hex also matches. The application only writes lowercase. | Noted |

Checked and **no defect found**: every foreign key column has exactly the type of the key it references (`int` → `int`), all 14 foreign keys use `RESTRICT`, every foreign key column is covered by an index, `users_pis.id` is `bigint` as decided and nothing references it, the widest indexed column is `varchar(500)` (2,000 bytes, limit 3,072), multiple `NULL`s in unique indexes behave as intended, enum columns store `int`, dates use `date`, UTC instants use `datetime(6)`, ciphertext is `varchar(256)` (a 12–20 character value encrypts to under 100 base64 characters), `audit_logs.additional_data` is a native `json` column, and no SQL-Server-only syntax exists.

## 3. Changes made
Corrective migration **`20261014075520_Standardize_Database_Names_To_Snake_Case`**: renames only. It does not touch data or change any column type.
- 14 tables renamed (12 legacy + the two trade tables made singular: `nsg_trades` → `nsg_trade`, `pd_trades` → `pd_trade`), 161 columns, 44 indexes; 14 foreign keys and 11 check constraints dropped and re-created under snake_case names (MySQL refuses to rename a column used by a `CHECK`). One check, `CK_States_Code_Format`, is handled by hand because EF's differ ignores case-only differences.
- The 15 scaffolded primary-key drop/re-add pairs were removed (MySQL calls every primary key `PRIMARY`).
- Code: new `DatabaseNamingStandard` (applied in `AppDbContext`); explicit `ToTable` and constraint/index names rewritten in every configuration; `UserPisRepository` duplicate-key classifier; `User.Rank` → `rank_name`.
- Tests: 70 raw SQL statements and schema assertions updated; `DatabaseNamingRuleTests` rewritten (18 tests); `ExpectedDatabaseSchema` pins all 15 tables and 171 columns.

Table mapping: `AuditLogs`→`audit_logs`, `Cities`→`cities`, `Devices`→`devices`, `EducationQualifications`→`education_qualifications`, `NsgUnits`→`nsg_units`, `OtpRequests`→`otp_requests`, `ParentDepartmentRanks`→`parent_department_ranks`, `RefreshTokens`→`refresh_tokens`, `Religions`→`religions`, `States`→`states`, `Users`→`users`, `UsersPIS`→`users_pis`, `nsg_trades`→`nsg_trade`, `pd_trades`→`pd_trade`; `rank_hierarchy` unchanged.

## 4. Verification
| Check | Result |
|---|---|
| Build | 0 errors |
| Full suite on the test database (applies every migration on real MySQL 8.0.44) | **374 passed, 0 failed** |
| Naming/schema tests (no database needed) | 18 passed |
| Model vs snapshot (`has-pending-model-changes`) | no changes |
| Migration on a **scratch copy of the real development data** (36 states, 7,931 cities, 22 ranks, 3 audit rows) | row counts and CRC checksums identical before and after; 14 foreign keys (all `RESTRICT`); 12 check constraints; 0 orphaned cities; **no identifier with an uppercase letter left** (only EF's own history table); `Down()` restores the old names with data intact. The scratch database was dropped afterwards. |
| Residual references to old names | none in `src`, `tools`, `tests` (only C# class/DbSet names, comments, and the HMAC labels that must not change) |
| Real Linux MySQL server | **not available**; replaced by the lowercase-only create-script test and the case-sensitive scan of the migrated copy |

## 5. Status and deployment risks
- **The corrective migration was applied to the development database `nsg_mobile_dev` on 2026-10-14** (backup taken first; row counts and checksums of all populated tables identical afterwards; 171 columns, 14 foreign keys, 12 check constraints and 46 indexes match the model; 0 orphaned rows; no identifier with an uppercase letter). It has also been applied to the disposable test database. It has NOT been applied to production.
- Build Linux databases by running the migrations (bundle/script with a migrator account). Do not restore a Windows schema dump (M4).
- Migrating needs `ALTER, CREATE, DROP, INDEX, REFERENCES` on the schema; no stored-routine rights are needed any more.
- Check the Linux server defaults before the first deployment: version 8.0.x, `lower_case_table_names=0`, default collation `utf8mb4_0900_ai_ci`, strict `sql_mode`, `time_zone` UTC.
- Moving data from Windows is now safe for names (dumps contain lowercase tables); verify with the row-count/checksum method used above.
- The FileStorage path in `appsettings.json` is still a Windows path (unrelated to the schema, see docs/CONFIGURATION.md).

## 6. Complete inventory (after → before)
Generated from the EF model and the pre-change model script. Columns marked `=` kept their name. Types, nullability and defaults are unchanged by the migration except where "was" is shown. The expected schema is pinned in `tests/NSG.Mobile.Tests/ExpectedDatabaseSchema.cs`.

#### `audit_logs` (was `AuditLogs`)

| Old column | New column | MySQL type | Null | Notes |
|---|---|---|---|---|
| `Id` | `id` | bigint | no | auto_increment |
| `UserId` | `user_id` | int | yes |  |
| `Action` | `action` | varchar(100) | no |  |
| `EntityType` | `entity_type` | varchar(100) | yes |  |
| `EntityId` | `entity_id` | varchar(100) | yes |  |
| `Result` | `result` | varchar(50) | no |  |
| `IpAddress` | `ip_address` | varchar(45) | yes |  |
| `DeviceId` | `device_id` | int | yes |  |
| `CorrelationId` | `correlation_id` | varchar(100) | yes |  |
| `AdditionalData` | `additional_data` | json | yes |  |
| `CreatedAt` | `created_at_utc` | datetime(6) | no |  |

- Primary key: (`id`) (MySQL always names it PRIMARY)
- Foreign keys: none
- Indexes: `ix_audit_logs_action` (action); `ix_audit_logs_created_at` (created_at_utc); `ix_audit_logs_user_id_created_at` (user_id, created_at_utc)
- Check constraints: none

#### `cities` (was `Cities`)

| Old column | New column | MySQL type | Null | Notes |
|---|---|---|---|---|
| `Id` | `id` | int | no | auto_increment |
| `Name` | `name` | varchar(200) | no |  |
| `StateId` | `state_id` | int | no |  |
| `Code` | `code` | varchar(50) | yes |  |
| `Classification` | `classification` | int | yes |  |
| `IsActive` | `is_active` | tinyint(1) | no | default TRUE |
| `SourceSystem` | `source_system` | varchar(50) | yes |  |
| `SourceKey` | `source_key` | varchar(250) | yes |  |
| `CreatedAtUtc` | `created_at_utc` | datetime(6) | no |  |
| `UpdatedAtUtc` | `updated_at_utc` | datetime(6) | yes |  |

- Primary key: (`id`) (MySQL always names it PRIMARY)
- Foreign keys: `fk_cities_states_state_id` (`state_id`) REFERENCES `states` (`id`) (RESTRICT)
- Indexes: `ix_cities_source_system_source_key` (unique) (source_system, source_key); `ix_cities_state_id_is_active` (state_id, is_active); `ix_cities_state_id_name` (state_id, name)
- Check constraints: none

#### `devices` (was `Devices`)

| Old column | New column | MySQL type | Null | Notes |
|---|---|---|---|---|
| `Id` | `id` | int | no | auto_increment |
| `UserId` | `user_id` | int | no |  |
| `Platform` | `platform` | int | no |  |
| `DeviceName` | `device_name` | varchar(200) | yes |  |
| `DeviceIdentifierHash` | `device_identifier_hash` | varchar(500) | no |  |
| `PublicKey` | `public_key` | varchar(2000) | yes |  |
| `PushToken` | `push_token` | varchar(500) | yes |  |
| `Status` | `status` | int | no |  |
| `AppVersion` | `app_version` | varchar(50) | yes |  |
| `OsVersion` | `os_version` | varchar(50) | yes |  |
| `LastSeenAt` | `last_seen_at_utc` | datetime(6) | yes |  |
| `LastIpAddress` | `last_ip_address` | varchar(45) | yes |  |
| `RevokedAt` | `revoked_at_utc` | datetime(6) | yes |  |
| `CreatedAt` | `created_at_utc` | datetime(6) | no |  |
| `UpdatedAt` | `updated_at_utc` | datetime(6) | no |  |

- Primary key: (`id`) (MySQL always names it PRIMARY)
- Foreign keys: `fk_devices_users_user_id` (`user_id`) REFERENCES `users` (`id`) (RESTRICT)
- Indexes: `ix_devices_user_id_device_hash` (user_id, device_identifier_hash); `ix_devices_user_id_status` (user_id, status)
- Check constraints: none

#### `education_qualifications` (was `EducationQualifications`)

> Later change (2026-10-14, migration `Rework_EducationQualifications_To_CourseName`): `name` was renamed `course_name`, index `ix_education_qualifications_name` became `ix_education_qualifications_course_name`, and `updated_at_utc` was dropped (it held no data in development or test). The table below is the state at the snake_case rename.

| Old column | New column | MySQL type | Null | Notes |
|---|---|---|---|---|
| `Id` | `id` | int | no | auto_increment |
| `Name` | `name` | varchar(100) | no |  |
| `IsActive` | `is_active` | tinyint(1) | no | default TRUE |
| `CreatedAtUtc` | `created_at_utc` | datetime(6) | no |  |
| `UpdatedAtUtc` | `updated_at_utc` | datetime(6) | yes |  |

- Primary key: (`id`) (MySQL always names it PRIMARY)
- Foreign keys: none
- Indexes: `ix_education_qualifications_name` (unique) (name)
- Check constraints: none

#### `nsg_trade` (was `nsg_trades`)

| Old column | New column | MySQL type | Null | Notes |
|---|---|---|---|---|
| = | `id` | int | no | auto_increment |
| = | `nsg_trade_name` | varchar(100) | no |  |
| = | `created_at_utc` | datetime(6) | no |  |
| = | `is_active` | tinyint(1) | no | default TRUE |

- Primary key: (`id`) (MySQL always names it PRIMARY)
- Foreign keys: none
- Indexes: `ix_nsg_trade_nsg_trade_name` (unique) (nsg_trade_name)
- Check constraints: none

#### `nsg_units` (was `NsgUnits`)

| Old column | New column | MySQL type | Null | Notes |
|---|---|---|---|---|
| `Id` | `id` | int | no | auto_increment |
| `Name` | `name` | varchar(100) | no |  |
| `IsActive` | `is_active` | tinyint(1) | no | default TRUE |
| `CreatedAtUtc` | `created_at_utc` | datetime(6) | no |  |
| `UpdatedAtUtc` | `updated_at_utc` | datetime(6) | yes |  |

- Primary key: (`id`) (MySQL always names it PRIMARY)
- Foreign keys: none
- Indexes: `ix_nsg_units_name` (unique) (name)
- Check constraints: none

#### `otp_requests` (was `OtpRequests`)

| Old column | New column | MySQL type | Null | Notes |
|---|---|---|---|---|
| `Id` | `id` | int | no | auto_increment |
| `ReferenceId` | `reference_id` | char(36) | no | collate ascii_general_ci |
| `MobileNumber` | `mobile_number` | varchar(15) | no |  |
| `OtpHash` | `otp_hash` | varchar(500) | no |  |
| `Purpose` | `purpose` | int | no |  |
| `AttemptCount` | `attempt_count` | int | no |  |
| `IsVerified` | `is_verified` | tinyint(1) | no |  |
| `ExpiresAt` | `expires_at_utc` | datetime(6) | no |  |
| `VerifiedAt` | `verified_at_utc` | datetime(6) | yes |  |
| `CreatedAt` | `created_at_utc` | datetime(6) | no |  |

- Primary key: (`id`) (MySQL always names it PRIMARY)
- Foreign keys: none
- Indexes: `ix_otp_requests_mobile_purpose_created` (mobile_number, purpose, created_at_utc); `ix_otp_requests_reference_id` (unique) (reference_id)
- Check constraints: none

#### `parent_department_ranks` (was `ParentDepartmentRanks`)

| Old column | New column | MySQL type | Null | Notes |
|---|---|---|---|---|
| `Id` | `id` | int | no | auto_increment |
| `Name` | `name` | varchar(100) | no |  |
| `IsActive` | `is_active` | tinyint(1) | no | default TRUE |
| `CreatedAtUtc` | `created_at_utc` | datetime(6) | no |  |
| `UpdatedAtUtc` | `updated_at_utc` | datetime(6) | yes |  |

- Primary key: (`id`) (MySQL always names it PRIMARY)
- Foreign keys: none
- Indexes: `ix_parent_department_ranks_name` (unique) (name)
- Check constraints: none

#### `pd_trade` (was `pd_trades`)

| Old column | New column | MySQL type | Null | Notes |
|---|---|---|---|---|
| = | `id` | int | no | auto_increment |
| = | `pd_trade_name` | varchar(100) | no |  |
| = | `created_at_utc` | datetime(6) | no |  |
| = | `is_active` | tinyint(1) | no | default TRUE |

- Primary key: (`id`) (MySQL always names it PRIMARY)
- Foreign keys: none
- Indexes: `ix_pd_trade_pd_trade_name` (unique) (pd_trade_name)
- Check constraints: none

#### `rank_hierarchy`

| Old column | New column | MySQL type | Null | Notes |
|---|---|---|---|---|
| `Id` | `id` | int | no | auto_increment |
| `SrNo` | `sr_no` | int | no |  |
| `RankName` | `rank_name` | varchar(100) | no |  |
| `ShortCode` | `short_code` | varchar(20) | yes |  |
| `IsActive` | `is_active` | tinyint(1) | no | default TRUE |
| `CreatedAt` | `created_at_utc` | datetime(6) | no |  |
| `UpdatedAt` | `updated_at_utc` | datetime(6) | no |  |

- Primary key: (`id`) (MySQL always names it PRIMARY)
- Foreign keys: none
- Indexes: `ix_rank_hierarchy_is_active` (is_active); `ix_rank_hierarchy_rank_name` (unique) (rank_name); `ix_rank_hierarchy_sr_no` (unique) (sr_no); `ix_rank_hierarchy_sr_no_rank_name` (unique) (sr_no, rank_name)
- Check constraints: none

#### `refresh_tokens` (was `RefreshTokens`)

| Old column | New column | MySQL type | Null | Notes |
|---|---|---|---|---|
| `Id` | `id` | int | no | auto_increment |
| `UserId` | `user_id` | int | no |  |
| `DeviceId` | `device_id` | int | no |  |
| `TokenHash` | `token_hash` | varchar(500) | no |  |
| `FamilyId` | `family_id` | char(36) | no | collate ascii_general_ci |
| `ExpiresAt` | `expires_at_utc` | datetime(6) | no |  |
| `RevokedAt` | `revoked_at_utc` | datetime(6) | yes |  |
| `ReplacedByTokenId` | `replaced_by_token_id` | int | yes |  |
| `CreatedByIp` | `created_by_ip` | varchar(45) | yes |  |
| `RevokedByIp` | `revoked_by_ip` | varchar(45) | yes |  |
| `RevokeReason` | `revoke_reason` | varchar(200) | yes |  |
| `ReuseDetectedAt` | `reuse_detected_at_utc` | datetime(6) | yes |  |
| `CreatedAt` | `created_at_utc` | datetime(6) | no |  |

- Primary key: (`id`) (MySQL always names it PRIMARY)
- Foreign keys: `fk_refresh_tokens_devices_device_id` (`device_id`) REFERENCES `devices` (`id`) (RESTRICT); `fk_refresh_tokens_refresh_tokens_replaced_by_token_id` (`replaced_by_token_id`) REFERENCES `refresh_tokens` (`id`) (RESTRICT); `fk_refresh_tokens_users_user_id` (`user_id`) REFERENCES `users` (`id`) (RESTRICT)
- Indexes: `ix_refresh_tokens_device_id` (device_id); `ix_refresh_tokens_family_id` (family_id); `ix_refresh_tokens_replaced_by_token_id` (replaced_by_token_id); `ix_refresh_tokens_token_hash` (unique) (token_hash); `ix_refresh_tokens_user_id_revoked_at` (user_id, revoked_at_utc)
- Check constraints: none

#### `religions` (was `Religions`)

| Old column | New column | MySQL type | Null | Notes |
|---|---|---|---|---|
| `Id` | `id` | int | no | auto_increment |
| `Name` | `name` | varchar(100) | no |  |
| `IsActive` | `is_active` | tinyint(1) | no | default TRUE |
| `CreatedAtUtc` | `created_at_utc` | datetime(6) | no |  |
| `UpdatedAtUtc` | `updated_at_utc` | datetime(6) | yes |  |

- Primary key: (`id`) (MySQL always names it PRIMARY)
- Foreign keys: none
- Indexes: `ix_religions_name` (unique) (name)
- Check constraints: none

#### `states` (was `States`)

| Old column | New column | MySQL type | Null | Notes |
|---|---|---|---|---|
| `Id` | `id` | int | no | auto_increment |
| `Name` | `name` | varchar(100) | no |  |
| `Code` | `code` | varchar(10) | yes |  |
| `AdministrativeType` | `administrative_type` | int | no |  |
| `IsActive` | `is_active` | tinyint(1) | no | default TRUE |
| `CreatedAtUtc` | `created_at_utc` | datetime(6) | no |  |
| `UpdatedAtUtc` | `updated_at_utc` | datetime(6) | yes |  |

- Primary key: (`id`) (MySQL always names it PRIMARY)
- Foreign keys: none
- Indexes: `ix_states_administrative_type_is_active` (administrative_type, is_active); `ix_states_code` (unique) (code); `ix_states_name` (unique) (name)
- Check constraints: `ck_states_administrative_type`, `ck_states_code_format`

#### `users` (was `Users`)

| Old column | New column | MySQL type | Null | Notes |
|---|---|---|---|---|
| `Id` | `id` | int | no | auto_increment |
| `Name` | `name` | varchar(200) | no |  |
| `Rank` | `rank_name` | varchar(100) | no |  |
| `ForceNumber` | `force_number` | varchar(50) | no |  |
| `MobileNumber` | `mobile_number` | varchar(15) | no |  |
| `PasswordHash` | `password_hash` | varchar(500) | yes |  |
| `Status` | `status` | int | no |  |
| `Role` | `role` | int | no |  |
| `IsActive` | `is_active` | tinyint(1) | no |  |
| `ApprovedBy` | `approved_by` | int | yes |  |
| `ApprovedAt` | `approved_at_utc` | datetime(6) | yes |  |
| `LastLoginAt` | `last_login_at_utc` | datetime(6) | yes |  |
| `FailedLoginAttempts` | `failed_login_attempts` | int | no |  |
| `LockoutEndAt` | `lockout_end_at_utc` | datetime(6) | yes |  |
| `RowVersion` | `row_version` | timestamp(6) | no | default CURRENT_TIMESTAMP(6) |
| `CreatedAt` | `created_at_utc` | datetime(6) | no |  |
| `UpdatedAt` | `updated_at_utc` | datetime(6) | no |  |

- Primary key: (`id`) (MySQL always names it PRIMARY)
- Foreign keys: none
- Indexes: `ix_users_force_number` (unique) (force_number); `ix_users_mobile_number` (unique) (mobile_number); `ix_users_status` (status)
- Check constraints: none

#### `users_pis` (was `UsersPIS`)

| Old column | New column | MySQL type | Null | Notes |
|---|---|---|---|---|
| `Id` | `id` | bigint | no | auto_increment |
| `NsgNumber` | `nsg_number` | varchar(30) | no |  |
| `ServiceNumber` | `service_number` | varchar(30) | no |  |
| `NsgRankId` | `nsg_rank_id` | int | yes |  |
| `ParentDepartmentRankId` | `parent_department_rank_id` | int | yes |  |
| = | `nsg_trade_id` | int | yes |  |
| = | `pd_trade_id` | int | yes |  |
| `FullName` | `full_name` | varchar(200) | yes |  |
| `NsgUnitId` | `nsg_unit_id` | int | yes |  |
| `EducationId` | `education_id` | int | yes |  |
| `DateOfInductionInNsg` | `date_of_induction_in_nsg` | date | yes |  |
| `SideSteppedOn` | `side_stepped_on` | date | yes |  |
| `SideSteppedFromNsgUnitId` | `side_stepped_from_nsg_unit_id` | int | yes |  |
| `DateOfRepatriation` | `date_of_repatriation` | date | yes |  |
| `DateOfBirth` | `date_of_birth` | date | yes |  |
| `DateOfEnrollmentInGovtService` | `date_of_enrollment_in_govt_service` | date | yes |  |
| `DateOfSuperannuation` | `date_of_superannuation` | date | yes |  |
| `Category` | `category` | int | yes |  |
| `ReligionId` | `religion_id` | int | yes |  |
| `FatherName` | `father_name` | varchar(200) | yes |  |
| `MotherName` | `mother_name` | varchar(200) | yes |  |
| `SpouseName` | `spouse_name` | varchar(200) | yes |  |
| `NextOfKinMobileNumber` | `next_of_kin_mobile_number` | varchar(15) | yes |  |
| `NumberOfChildren` | `number_of_children` | int | yes |  |
| `MobileNumber` | `mobile_number` | varchar(15) | yes |  |
| `BloodGroup` | `blood_group` | int | yes |  |
| `HeightCm` | `height_cm` | tinyint unsigned | yes |  |
| `WeightKg` | `weight_kg` | decimal(5,2) | yes |  |
| `AadhaarNumberEncrypted` | `aadhaar_number_encrypted` | varchar(256) | yes |  |
| `PanNumberEncrypted` | `pan_number_encrypted` | varchar(256) | yes |  |
| `PranNumberEncrypted` | `pran_number_encrypted` | varchar(256) | yes |  |
| `SalaryAccountNumberEncrypted` | `salary_account_number_encrypted` | varchar(256) | yes |  |
| `PanNumberHash` | `pan_number_hash` | varchar(64) | yes |  |
| `SalaryAccountNumberHash` | `salary_account_number_hash` | varchar(64) | yes |  |
| `PermanentVillage` | `permanent_village` | varchar(150) | yes |  |
| `PermanentPostOffice` | `permanent_post_office` | varchar(150) | yes |  |
| `PermanentPoliceStation` | `permanent_police_station` | varchar(150) | yes |  |
| `PermanentDistrict` | `permanent_district` | varchar(100) | yes |  |
| `PermanentStateId` | `permanent_state_id` | int | yes |  |
| `PermanentPinCode` | `permanent_pin_code` | varchar(6) | yes |  |
| `DateOfAme` | `date_of_ame` | date | yes |  |
| `DateOfImmovablePropertyReturn` | `date_of_immovable_property_return` | date | yes |  |
| `EarnedLeaveDue` | `earned_leave_due` | decimal(7,2) | yes |  |
| `HalfPayLeaveDue` | `half_pay_leave_due` | decimal(7,2) | yes |  |
| `PaternityLeaveAvailed` | `paternity_leave_availed` | varchar(100) | yes |  |
| `MaternityLeaveAvailed` | `maternity_leave_availed` | varchar(100) | yes |  |
| `ChildCareLeaveAvailed` | `child_care_leave_availed` | varchar(100) | yes |  |
| `MobileAppActivationStatus` | `mobile_app_activation_status` | int | no | default 0 |
| `IsActive` | `is_active` | tinyint(1) | no | default TRUE |
| `CreatedAtUtc` | `created_at_utc` | datetime(6) | no |  |
| `CreatedBy` | `created_by` | int | yes |  |
| `UpdatedAtUtc` | `updated_at_utc` | datetime(6) | yes |  |
| `UpdatedBy` | `updated_by` | int | yes |  |

- Primary key: (`id`) (MySQL always names it PRIMARY)
- Foreign keys: `fk_users_pis_education_qualifications_education_id` (`education_id`) REFERENCES `education_qualifications` (`id`) (RESTRICT); `fk_users_pis_nsg_trade_nsg_trade_id` (`nsg_trade_id`) REFERENCES `nsg_trade` (`id`) (RESTRICT); `fk_users_pis_nsg_units_nsg_unit_id` (`nsg_unit_id`) REFERENCES `nsg_units` (`id`) (RESTRICT); `fk_users_pis_nsg_units_side_stepped_from_nsg_unit_id` (`side_stepped_from_nsg_unit_id`) REFERENCES `nsg_units` (`id`) (RESTRICT); `fk_users_pis_parent_department_ranks_parent_department_rank_id` (`parent_department_rank_id`) REFERENCES `parent_department_ranks` (`id`) (RESTRICT); `fk_users_pis_pd_trade_pd_trade_id` (`pd_trade_id`) REFERENCES `pd_trade` (`id`) (RESTRICT); `fk_users_pis_rank_hierarchy_nsg_rank_id` (`nsg_rank_id`) REFERENCES `rank_hierarchy` (`id`) (RESTRICT); `fk_users_pis_religions_religion_id` (`religion_id`) REFERENCES `religions` (`id`) (RESTRICT); `fk_users_pis_states_permanent_state_id` (`permanent_state_id`) REFERENCES `states` (`id`) (RESTRICT)
- Indexes: `ix_users_pis_education_id` (education_id); `ix_users_pis_is_active` (is_active); `ix_users_pis_mobile_number` (unique) (mobile_number); `ix_users_pis_nsg_number` (unique) (nsg_number); `ix_users_pis_nsg_rank_id` (nsg_rank_id); `ix_users_pis_nsg_trade_id` (nsg_trade_id); `ix_users_pis_nsg_unit_id` (nsg_unit_id); `ix_users_pis_pan_number_hash` (unique) (pan_number_hash); `ix_users_pis_parent_department_rank_id` (parent_department_rank_id); `ix_users_pis_pd_trade_id` (pd_trade_id); `ix_users_pis_permanent_state_id` (permanent_state_id); `ix_users_pis_religion_id` (religion_id); `ix_users_pis_salary_account_number_hash` (unique) (salary_account_number_hash); `ix_users_pis_service_number` (service_number); `ix_users_pis_side_stepped_from_nsg_unit_id` (side_stepped_from_nsg_unit_id)
- Check constraints: `ck_users_pis_blood_group`, `ck_users_pis_category`, `ck_users_pis_enrollment_after_birth`, `ck_users_pis_hashes_format`, `ck_users_pis_mobile_app_activation_status`, `ck_users_pis_mobile_number_format`, `ck_users_pis_numbers`, `ck_users_pis_pin_code_format`, `ck_users_pis_repatriation_after_induction`, `ck_users_pis_superannuation_after_birth`


