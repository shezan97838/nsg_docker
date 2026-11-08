# Runbook: applying `Rebuild_Users_Pis_Final_Schema`

Migration `20261103075304_Rebuild_Users_Pis_Final_Schema` (ADR-020) **drops and recreates `users_pis`**. Every existing personnel row is deleted. No other table changes.

## 1. Before you start
1. **Take a backup** of the whole database (`mysqldump --single-transaction --routines --triggers <db> > pre_pis_rebuild.sql`). A backup is the only way back: the migration cannot be reverted and MySQL DDL cannot be rolled back.
2. Confirm the target database name. Never run this against a database you have not named explicitly.
3. Confirm the MySQL version is 8.0.16 or newer (`SELECT VERSION();`).
4. Know the impact: after the migration `users_pis` is empty. Until personnel are entered/imported, mobile registration and login, the profile endpoint, admin portal sign-in (admins are matched to a personnel row by NSG number), and any look-up by NSG number will find no person. Plan to re-populate immediately, or create the first admin's personnel row before expecting admin sign-in.
5. Optional, read-only: see what will be discarded.
   ```sql
   SELECT COUNT(*) AS rows_to_be_discarded FROM users_pis;
   ```
   (Mobile numbers do not need to be verified or repaired first: the rows are discarded, not converted.)

## 2. Apply
Stop the application (or at least make sure nothing writes to `users_pis`), then:
```powershell
# from the repository root; the connection string comes from the environment, never from a file
$env:ConnectionStrings__MySql = "Server=<host>;Port=3306;Database=<database>;Uid=<user>;Pwd=<password>;AllowUserVariables=True;SslMode=Required;"
$env:Database__MigrateOnStartup = "false"
dotnet ef database update 20261103075304_Rebuild_Users_Pis_Final_Schema `
  --project src/NSG.Mobile.Infrastructure --startup-project src/NSG.Mobile.API
```
Or generate the SQL, review it, and run it with your own tooling:
```powershell
dotnet ef migrations script <last-applied-migration> 20261103075304_Rebuild_Users_Pis_Final_Schema `
  --project src/NSG.Mobile.Infrastructure --startup-project src/NSG.Mobile.API -o rebuild_users_pis.sql
```
Migrations applied before this one (up to `20261028184226_Add_Banner_Show_Only_Image`) are applied in order automatically if they are still pending.
The account needs `CREATE`, `DROP`, `ALTER`, `INDEX`, `REFERENCES` and `CREATE TEMPORARY TABLES` on the database.

## 3. What the preflight can tell you
It runs before anything is dropped and stops with `Check constraint 'PIS rebuild blocked: <reason>' is violated.`

| Message | Meaning | Fix |
|---|---|---|
| `MySQL 8.0.16 or newer is required` | CHECK constraints would not be enforced | upgrade MySQL |
| `a foreign key points at users_pis` | another table references `users_pis` | find it (`information_schema.referential_constraints`), decide with the owner; do not drop it blindly |
| `a view uses users_pis` / `a stored routine uses users_pis` | it would break when the table is dropped | update or drop the view/routine first |
| `a trigger exists on users_pis` | the trigger would be dropped silently | review it, drop it deliberately |

A failed preflight changes nothing. Fix the cause and run the migration again.

## 4. If it fails after the preflight
Examples: the server stops, the account lacks `DROP`/`CREATE`, the disk is full. MySQL DDL is not transactional, so the database may be left without `users_pis`, or without its indexes.
1. Do **not** re-run blindly.
2. Check `__EFMigrationsHistory`: if `20261103075304_Rebuild_Users_Pis_Final_Schema` is **not** listed, the migration did not complete.
3. Look at the table: `SHOW CREATE TABLE users_pis;`. If it is missing or incomplete, either restore the backup from step 1 and fix the cause, or (since the data is disposable) run the migration again; it starts with `DROP TABLE IF EXISTS users_pis`, so it is safe to repeat once the cause is fixed.
4. After success, verify (section 5).

## 5. Verify after applying
```sql
SELECT COUNT(*) FROM information_schema.columns WHERE table_schema = DATABASE() AND table_name = 'users_pis';                 -- 49
SELECT COUNT(*) FROM information_schema.table_constraints WHERE table_schema = DATABASE() AND table_name = 'users_pis' AND constraint_type = 'FOREIGN KEY';   -- 0
SELECT COUNT(*) FROM information_schema.referential_constraints WHERE constraint_schema = DATABASE() AND referenced_table_name = 'users_pis';                 -- 0
SELECT column_name FROM information_schema.columns WHERE table_schema = DATABASE() AND table_name = 'users_pis' AND (column_name LIKE '%\_id' OR column_name LIKE '%\_hash');  -- no rows
SELECT `MigrationId` FROM __EFMigrationsHistory ORDER BY 1 DESC LIMIT 1;                                                           -- the new migration
```
