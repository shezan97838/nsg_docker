# Geography master data (States / Union Territories)

Cities were removed from the NSG Mobile API (see the note at the top of `docs/DATABASE-SCHEMA-AUDIT.md`). There is no CSV import mechanism any more: States/UTs are
managed through the States API (`/api/v1/states`). The files below are reference material only; nothing reads them at runtime.

| File | Content |
|------|---------|
| `states.csv` | 28 States + 8 Union Territories (LGD codes), the list originally loaded into `states` |
| `source/lgd-official-states.csv` | State list read from the official LGD page (`lgdirectory.gov.in/globalviewstateforcitizen.do`) on 2026-10-12; all 36 codes and State/UT types match `states.csv` (names differ only in letter case) |
| `source/state-lgd-codes.csv` | India Data Portal mirror (ODC-BY) that `states.csv` was first built from; its pre-2020 rows `25` and `26` were excluded (merged into `38`) |

Notes:
* `states.id` is an internal auto-increment key. A State is identified by its official `code` (two digits, stored as text so `01` keeps its zero), never by `id`.
* Existing `states` rows in every database are untouched by the City removal.
