# ADR-020: PIS as a self-contained personnel table

**Status:** Accepted. Final schema decided 2026-11-03. Migration `20261103075304_Rebuild_Users_Pis_Final_Schema` is written and verified on disposable MySQL databases; it has not been applied to any shared or real database.

## Context
`users_pis` referenced nine master tables by id (rank, parent-department rank, NSG trade, PD trade, NSG unit twice, education, religion, state), stored
gender/category/blood group as integer codes, height/weight/leave as numbers, and PAN / salary account as ciphertext plus unique HMAC blind-index hashes.
Personnel data now arrives as plain values from the PIS workbook (`PIS Data Updated(1).xlsx`, Sheet1), so a person must be insertable and updatable without
resolving anything against a master table. The existing personnel rows are disposable (product-owner decision), so no data-preserving conversion is needed.

## Decision
`users_pis` / `UserPis` keeps its name and is rebuilt as a table that contains only the 42 workbook fields plus 7 system fields (49 columns):

- **Text:** every non-date personnel value is a `varchar` (rank, parent-department rank, unit, education, religion, state, gender, category, blood group, height, weight,
  number of children, leave details, PIN, side-stepped details, names, mobile numbers). Numeric-looking values stay text. Gender, category and blood group are free text.
- **Dates:** exactly seven `DATE` columns / `DateOnly?` (induction, repatriation, birth, enrolment, superannuation, immovable property return, AME).
- **Side-stepped:** one text column `side_stepped_details` (for example `10.01.2025 FROM 11 SRG`), never split or parsed.
- **Encrypted:** Aadhaar, PAN, PRAN and salary account (`varchar(512)`), reversibly encrypted with the existing `IEncryptionService`. No hash columns, no blind index, no uniqueness.
- **System:** `id` (bigint auto-increment), `mobile_app_activation_status` (int, default 0), `is_active` (default true), `created_at_ist`, `created_by`, `updated_at_ist`, `updated_by`.
- **Constraints:** `nsg_number` NOT NULL UNIQUE; `mobile_number` NOT NULL UNIQUE and exactly ten digits; `mobile_app_activation_status` in (0, 1, 2). Nothing else.
  The date-order checks that existed before (enrolment after birth, superannuation after birth, repatriation after induction) were **removed** because the source workbook
  contains legitimate records that violate them. The API validators no longer check date order either.
- **No dependencies:** no foreign keys in either direction, no lookup ids, no navigation properties, no lookup-based validation. Saving a person needs no master row.
- The master tables and their APIs are unchanged, except that the two trade masters lost their "used by personnel" guard (no personnel column references trades any more).
  The other masters keep refusing deactivation/deletion while a personnel row carries the same name as text.
- NSG trade and parent-department trade are **not** personnel fields any more (they are not in the workbook mapping) and were dropped.

## Migration strategy
- **One migration:** `20261103075304_Rebuild_Users_Pis_Final_Schema` runs a preflight, drops `users_pis` and creates it as above. It is destructive by design.
- **Superseded draft removed:** `20261103052042_Refactor_Users_Pis_To_Text_Model` (an in-place conversion with backfills from the master tables) was never applied to a real
  database and was deleted. No applied migration was edited; history up to `20261028184226_Add_Banner_Show_Only_Image` is untouched, so existing environments just apply the new one.
- **Preflight (nothing destructive runs before it passes):** MySQL 8.0.16 or newer (CHECK enforcement); no foreign key referencing `users_pis`; no view, stored routine or trigger that
  uses or sits on `users_pis`. A failed check stops with `Check constraint 'PIS rebuild blocked: <reason>' is violated.`
- **Not reversible:** `Down()` throws; restore the backup taken before the migration. See `docs/PIS-MIGRATION-RUNBOOK.md` for backup, apply and recovery steps.
- **Consequence:** every existing personnel row is lost. Nothing keyed on a PIS row works for those people (mobile registration/login, profile, admin sign-in, leave and grievance
  look-ups by NSG number) until the personnel are re-entered or imported. The other modules' own tables are not touched.

## Related changes
- The PIS and profile APIs use text properties named like the columns (`nsgRank`, `nsgUnit`, `permanentState`, ...). Requests may send a JSON number for height, weight, number of
  children and the two leave-due fields; its digits are kept as text. POST requires a unique ten-digit mobile number (starting 6-9, so the person can register in the app) and a gender;
  PUT keeps the stored mobile number and gender when they are omitted.
- The HMAC blind index (service, options, `BlindIndex:Key` setting and its startup check), the gender / category / blood-group enums and the enum mapper were removed.

## Open questions
- Whether the API should keep the 6-9 start rule for the mobile number now that the table only requires ten digits (kept: the mobile-app tables have the same rule).
- How the personnel will be re-populated after the rebuild (import is out of scope for this decision).
