# Database Standards (MySQL first)

> Authoritative database rules for the NSG Mobile API. **The MySQL schema is the source of truth**; nothing is designed around SQL Server conventions.
> Windows is the development environment, Ubuntu Linux the deployment environment. A green build on Windows does **not** prove Linux compatibility.
> `.claude/rules/project-rules.md` keeps a short summary and points here. Evidence for the current schema: [DATABASE-SCHEMA-AUDIT.md](DATABASE-SCHEMA-AUDIT.md).

Target: MySQL 8.0 (verified on 8.0.44), InnoDB, `utf8mb4`, Pomelo.EntityFrameworkCore.MySql 8.

## 1. Why the naming rules exist
MySQL on Linux treats database and table names as case-sensitive (`lower_case_table_names=0`). MySQL on Windows stores them in lowercase (`lower_case_table_names=1`) whatever case the DDL used. A name such as `UsersPIS` therefore works on a Windows server, but a dump made there contains `userspis`, and restoring it on Linux produces a table the code (asking for `UsersPIS`) cannot find. Lowercase `snake_case` means the same thing on both platforms. Column, index and constraint names are case-insensitive in MySQL, but they follow the same convention so one rule covers everything.

`lower_case_table_names` can only be set when a server is first initialised. It is **not** a routine fix, and no application change may depend on it.

## 2. Rules

### A. Tables
- Lowercase `snake_case`: `a-z`, digits, underscore; starts with a letter; at most 64 characters.
- Never a MySQL 8.0 reserved word (the list in `tests/NSG.Mobile.Tests/MySqlReservedWords.cs` comes from `information_schema.KEYWORDS`).
- Every table is mapped **explicitly** with `ToTable("name")` in its `IEntityTypeConfiguration<T>`. Model building fails if a table is missing a compliant explicit name (`DatabaseNamingStandard`).
- Keep the established word and its singular/plural form; do not rename a table because another name looks nicer. (`UsersPIS` → `users_pis`, `NSGTrade` → `nsg_trade`.)
- All future tables follow this rule. There is no legacy exception.
- Exception: EF's own `__EFMigrationsHistory` table and its `MigrationId`/`ProductVersion` columns (see section 6).

### B. Columns
- Lowercase `snake_case`, same limits as tables, never reserved words (so the C# property `User.Rank` is stored in column `rank_name`).
- C# properties keep .NET naming. `DatabaseNamingStandard` derives the column from the property name (`NsgTradeId` → `nsg_trade_id`); an explicit `HasColumnName` always wins.
- Consistent names: primary key `id`; foreign key `<referenced_singular>_id`; booleans `is_*`; status columns `*_status` or `status`; audit columns `created_at_ist`, `updated_at_ist`, `created_by`, `updated_by`.
- Every `DateTime` property is an **Indian Standard Time (IST / UTC+05:30) instant**, stored as `datetime(6)`, and its column ends in `_ist` (enforced by `DatabaseNamingStandard`). Application code writes `IndianStandardTime.Now`.

### C. Data types
- Identifiers: `int` unless the architecture says otherwise. `users_pis.id` and `audit_logs.id` are `bigint` by decision; do not change `users_pis.id`.
- A foreign-key column has **exactly** the type (including signedness) of the key it references. Enforced by `Foreign_key_columns_have_exactly_the_type_of_the_key_they_reference...`.
- Bounded strings: `varchar(n)` with an explicit, justified `n`. Exact numbers: `decimal(p,s)` (weight `decimal(5,2)`, leave balances `decimal(7,2)`). Date-only values: `date`. Booleans: `tinyint(1)`. Enums: `int` with a `CHECK` where the value set is closed. Encrypted values: `varchar(256)` holding base64 ciphertext; hashes: `varchar(64)` lowercase hex.
- Index keys must fit InnoDB's 3,072-byte limit (with `utf8mb4` a `varchar(n)` key costs 4n bytes). The longest indexed column today is `varchar(500)` = 2,000 bytes.
- Never use SQL Server types or syntax (`nvarchar`, `uniqueidentifier`, `IDENTITY`, `GETDATE()`).
- Do not change a column type for style. Confirm existing data fits the new type first.

### D. Linux compatibility
- All identifiers, in model, migrations, raw SQL and scripts, are lowercase. The EF create script must contain no identifier with an uppercase letter (tested).
- Collation: the schema currently relies on the server default `utf8mb4_0900_ai_ci` (case- and accent-insensitive; GUID columns are `ascii_general_ci`). Before the first Linux deployment verify the target server reports the same default (see DATABASE-SCHEMA-AUDIT.md, finding M3).
- Always verify behaviour on the supported MySQL version, not by reasoning about another engine.

### E. Constraints and indexes
- Names: primary key `pk_<table>` (cosmetic: MySQL calls it `PRIMARY`), foreign key `fk_<table>_<referenced_table>_<column>`, index `ix_<table>_<columns>` (unique indexes too), check `ck_<table>_<rule>`. All generated or validated by `DatabaseNamingStandard`; names stay within 64 characters. An explicit `HasConstraintName(...)` on a foreign key is kept by the standard (still checked for format and length); use it only when the generated name would exceed 64 characters (the leave errors -> batches key).
- Foreign keys use `Restrict` delete behaviour (no cascades); every foreign key column is covered by an index.
- Uniqueness is enforced **in the database** as well as in validation. MySQL allows several `NULL`s in a unique index; remember that the default collation makes unique comparisons case- and accent-insensitive.
- No duplicate or redundant indexes (an index whose columns are a prefix of, or a superset of uniques of, another).
- A `CHECK` constraint may only reference columns of its own table, using the final snake_case names (tested).

### F. Migrations
- Use the EF Core Pomelo provider and review the generated SQL (`dotnet ef migrations script <from> <to>`) before applying.
- **Never edit or rename a migration that was applied to a shared or deployed database.** Fix forward with a corrective migration. A migration that exists only on one developer's machine and in the disposable test database may be removed with `dotnet ef migrations remove`.
- Never apply migrations at application start. Production uses a migration bundle or script, run with a **migrator** account; the runtime account has `SELECT, INSERT, UPDATE, DELETE` only (docs/CONFIGURATION.md section 6).
- Delete the scaffolded `DropPrimaryKey`/`AddPrimaryKey` pairs that appear when only a table name changes: MySQL primary keys are always `PRIMARY`, and Pomelo wraps those operations in stored procedures that need `CREATE ROUTINE`.
- EF's differ ignores changes that differ only by letter case (it missed `CK_States_Code_Format` → `ck_states_code_format`). After a renaming migration, compare the real schema, not just the generated script.
- Model configuration, `AppDbContextModelSnapshot`, `__EFMigrationsHistory` and the real schema must agree: run `dotnet ef migrations has-pending-model-changes` and compare `information_schema` after applying.
- A renaming migration is tested on a **copy of real data** (scratch database whose name ends in `_test`): row counts and checksums before and after, foreign-key integrity, the rollback (`Down`), and a scan for identifiers with uppercase letters.
- Before changing a deployed database take a backup (`util.dumpSchemas`) and apply only after explicit approval.

### G. SQL in application code
- Application code uses LINQ/EF only. Raw SQL (`FromSql`, `ExecuteSql`, scripts, dynamic identifiers) is limited to tests and one-off tooling, uses parameters for values, and uses the exact lowercase names. Identifiers are never built from user input.
- Code must not depend on an index or constraint name appearing in a MySQL error message unless that name is a constant covered by a test (`UserPisRepository` reads `ix_users_pis_*` names from duplicate-key errors; renaming those indexes means updating it).
- Strings that merely look like database names but are data (for example the HMAC labels `UsersPIS.Pan` in `PisBlindIndexes`) are **not** identifiers and must never follow a rename.

## 3. How the rules are enforced
| Control | Where |
|---|---|
| Explicit snake_case tables, derived columns/keys/indexes/foreign keys, 64-character limit | `src/NSG.Mobile.Infrastructure/Persistence/DatabaseNamingStandard.cs` (runs when the model is built) |
| Complete expected schema (every table, column, MySQL type, nullability) | `tests/NSG.Mobile.Tests/ExpectedDatabaseSchema.cs`, asserted by `DatabaseNamingRuleTests` |
| No reserved words, FK/PK type equality, Restrict, check-constraint columns, IST `_ist` columns, lowercase create script | `DatabaseNamingRuleTests` |
| Migration chain applies on real MySQL | the whole integration suite (`ApiFactory.ResetDatabaseAsync` runs every migration) |

## 4. Changing the schema: checklist
1. Add or change the entity and its `IEntityTypeConfiguration<T>` (explicit `ToTable`, lengths, types).
2. Update `ExpectedDatabaseSchema.cs` in the same change.
3. `dotnet ef migrations add <Name>`; read the SQL; strip primary-key operations if only names changed.
4. Run the full suite against the test database; for renames also run the copy-of-data check above.
5. Ask for approval before applying to development or production.

## 5. Public API versus database
JSON property names and routes are public contracts. They do **not** follow database names and are not renamed because a column was renamed.

## 6. Known exceptions and open decisions
- `__EFMigrationsHistory` (mixed case, EF-managed). On Linux it is created with that exact case and EF always asks for it that way, so a database built by running the migrations on Linux works. It breaks only if a Windows dump is restored on Linux. Deployment rule: **build Linux databases by running the migrations, never by restoring a Windows schema dump.** Optionally configure `MigrationsHistoryTable("__ef_migrations_history")` before the first Linux database exists (existing databases would then need a one-time manual `RENAME TABLE`).
- Collation pinning, enum check constraints and one redundant index are tracked in DATABASE-SCHEMA-AUDIT.md.
