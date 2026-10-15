# Geography master data (States / Union Territories / Cities)


## What is loaded

| File | Content | Status |
|------|---------|--------|
| `states.csv` | 28 States + 8 Union Territories (LGD codes) | **Loaded** (36) |
| `cities-census2011.csv` | 7,931 towns: 4,041 statutory + 3,890 census towns, mapped to LGD state codes | **Loaded** (7,931) |
| `census2011-state-reconciliation.csv` | Per State/UT: StateId, source rows, imported, statutory/census split, rejects | Report |
| `census2011-prepare-report.txt` | Row counts + reconciliation against Census table A-01 | Report |
| `census2011-rejected.csv` | Rows the preparer rejected (header only = none) | Report |
| `source/census2011-town-rows.tsv` | The 8,377 raw source rows the cities were derived from (statutory 8xxxxx + "(CT)") | Provenance |
| `source/lgd-official-states.csv`, `source/state-lgd-codes.csv` | Official LGD state list read 2026-10-12; India Data Portal mirror | Provenance |

## Sources and limitations

**States/UTs** — LGD state codes. `source/lgd-official-states.csv` was read from the official LGD page
(`lgdirectory.gov.in/globalviewstateforcitizen.do`, 2026-10-12): 36 rows, all codes and State/UT types match `states.csv`
(names differ only in letter case). `source/state-lgd-codes.csv` is the India Data Portal mirror (ODC-BY) the file was first built from;
its pre-2020 rows `25` and `26` were excluded (merged into `38`).

**Cities/towns** — Census of India 2011, **"Census 2011 - Location Code Directory"** (`PC11_TV_DIR.xlsx`), Office of the Registrar General
& Census Commissioner, India. Catalog: `https://censusindia.gov.in/nada/index.php/catalog/42648`; file:
`.../catalog/42648/download/46323/PC11_TV_DIR.xlsx`. Downloaded 2026-10-12, 657,009 data rows, SHA-256
`E5670123E836148CD4A869805333B519F45CE837D1145C8B5CECE3A01DBF7DD0` (the 23 MB workbook itself is not committed; re-download and verify the hash).
Reference date: Census 2011. Counts are checked against Census table A-01 (`catalog/42526`, SHA-256 `6B649697ED5993DA834B674173DAB68E1EF9ABB97E234155A80884C2FA4584F5`).

How rows are selected (`tools/NSG.Mobile.GeoImport` → `prepare-census`, repeatable):

* **Statutory towns**: rows with town code `800001`–`804041` (4,041 distinct codes). `Code` = that national town code. `SourceKey` = `ST-<code>`.
* **Census towns**: rows tagged `(CT)`. The directory publishes no national code for them, so `Code` is left empty (not invented); `SourceKey` = `CT-<censusState>-<townCode>` (unique within the file).
* **Excluded**: villages, districts, sub-districts, un-inhabited villages and outgrowth (OG) rows. Urban agglomerations are not separate records.
* A town spanning several sub-districts appears once per sub-district plus a district-000 summary row; these are folded into one record (446 rows folded).
* **Names**: the Census label is kept as published. Only a trailing urban-status annotation such as `(M)`, `(NP)`, `(CT)`, `(Part)`, `(M Cl + OG)` is moved to the `SourceStatus` column.
  Other parentheses are part of the official disambiguating label and are kept, e.g. `Aakera (292)`, `S.A.S. Nagar (Mohali)`, `GHMC` (Greater Hyderabad). No diacritics exist in the source (all ASCII).
* **Classification** is `StatutoryTown` or `CensusTown` only.

**State mapping (2011 boundaries → today's 36)**: the Census file uses 2011 state codes. Codes 01–24, 27, 29–35 map one-to-one to the LGD code.
The following post-2011 changes are applied, using the directory's own district lists:

| Census 2011 | Becomes | Towns |
|---|---|---|
| Andhra Pradesh (28) districts 532–541 (Adilabad … Khammam) | Telangana (36), 2014 | 157 |
| Jammu & Kashmir (01) districts 003 Leh, 004 Kargil | Ladakh (37), 2019 | 4 |
| Daman & Diu (25) + Dadra & Nagar Haveli (26) | The Dadra and Nagar Haveli and Daman and Diu (38), 2020 | 14 |

This reassignment is documented reconciliation, not text present in the Census file.

**Known gaps — do not read this as a complete, current list of Indian cities**
* It is a **2011 snapshot**. Towns created, merged or renamed after the 2011 Census (new municipalities, new Census-style towns after 2011) are absent, and some listed towns have since changed status.
* Imported 7,931 vs 7,933 towns in Census table A-01 (−2): on 2011 boundaries Andhra Pradesh (incl. today's Telangana) yields 350 vs 353 (−3) and West Bengal 910 vs 909 (+1); every other state/UT matches A-01 exactly. The cause is not determinable from the file (no untagged candidates were found); these were not guessed.
* LGD (current, post-2011 urban local bodies) is the better long-term source but its Urban Local Body reports sit behind a CAPTCHA, which was not bypassed.
* Licence: the NADA catalog page does not state a reuse licence in the retrieved text. Confirm redistribution terms (Government Open Data Licence – India) before publishing the CSV outside the organisation.

Importer CSV columns (extra columns such as `SourceName`, `SourceStatus`, `Census*Code` are provenance only and are not stored):

```
StateCode,Name,SourceSystem,SourceKey,Code,Classification
```
`StateCode` must exist in `states.csv`. `SourceSystem` + `SourceKey` is the stable identity used for idempotent re-imports
(city names are **not** unique). `Classification`: `StatutoryTown` | `CensusTown`.

## Rebuilding the cities file (needs the Census xlsx, no database)

```powershell
dotnet run --project tools\NSG.Mobile.GeoImport -- prepare-census `
  --xlsx C:\path\PC11_TV_DIR.xlsx --a01 C:\path\A01.xlsx --lgd-states data\geo\source\state-lgd-codes.csv `
  --out data\geo\cities-census2011.csv --rejected data\geo\census2011-rejected.csv `
  --raw data\geo\source\census2011-town-rows.tsv --report data\geo\census2011-prepare-report.txt
```

## Commands (PowerShell, from the repo root)

```powershell
# connection string comes ONLY from the environment (never from source control)
$env:ConnectionStrings__MySql = "Server=...;Port=3306;Database=nsg_mobile_dev;Uid=...;Pwd=...;AllowUserVariables=True;"

# 1. schema (migration Add_States_Cities)
dotnet ef database update --project src\NSG.Mobile.Infrastructure --startup-project src\NSG.Mobile.API

# 2. import (dry-run first; --allow-rejects skips bad rows instead of aborting). NOTE: a dry run consumes AUTO_INCREMENT ids, so rehearse on a scratch database, not the real one
dotnet run --project tools\NSG.Mobile.GeoImport -- import --states data\geo\states.csv --cities data\geo\cities-census2011.csv --dry-run
dotnet run --project tools\NSG.Mobile.GeoImport -- import --states data\geo\states.csv --cities data\geo\cities-census2011.csv --report data\geo\import-report.txt

# 3. verify (exit code 0 = valid)
dotnet run --project tools\NSG.Mobile.GeoImport -- validate --expected-states 28 --expected-uts 8
```

The importer upserts only (never deletes), runs in one transaction, and is idempotent. Nothing runs at application startup.

Notes:
* `States.Id` is an internal auto-increment key. Rows are matched by the official `Code` (two digits, stored as text so `01` keeps its zero), never by `Id`.
* A `--dry-run` inserts and then rolls back, and MySQL does **not** roll back `AUTO_INCREMENT`, so a dry run consumes IDs. This is why the dev `States` IDs start at 37 (one dry run of 36 rows came first). Harmless; do not renumber.
* `source/lgd-official-states.csv` is the state list read from the official LGD page (`lgdirectory.gov.in/globalviewstateforcitizen.do`) on 2026-10-12; `states.csv` matches it on all 36 codes and State/UT types (names differ only in letter case, e.g. "and" vs "And").

## Ubuntu 24.04

```bash
dotnet ef migrations script --idempotent --project src/NSG.Mobile.Infrastructure \
  --startup-project src/NSG.Mobile.API -o /tmp/nsg-migrations.sql    # review, then: mysql nsg_mobile < /tmp/nsg-migrations.sql
export ConnectionStrings__MySql='Server=...;Database=nsg_mobile;Uid=...;Pwd=...;'
dotnet publish tools/NSG.Mobile.GeoImport -c Release -o /opt/nsg/geoimport
dotnet /opt/nsg/geoimport/NSG.Mobile.GeoImport.dll import --states data/geo/states.csv --cities data/geo/cities-census2011.csv
dotnet /opt/nsg/geoimport/NSG.Mobile.GeoImport.dll validate
```
Table names are PascalCase (`States`, `Cities`) and used consistently; on Linux MySQL (case-sensitive by default) keep
`lower_case_table_names` the same for creation and use. Paths above use forward slashes and exact casing.

## Tests

`tests/NSG.Mobile.Tests` runs against a **real MySQL** database whose name must end in `_test` (it is dropped and recreated by migrations):

```powershell
$env:NSG_TEST_MYSQL = "Server=127.0.0.1;Port=3306;Database=nsg_mobile_test;Uid=...;Pwd=...;AllowUserVariables=True;"
dotnet test tests\NSG.Mobile.Tests
```


