# ADR-021: Salary slip PDF processing, storage and third-party licences

Status: Accepted (Phases 1–4, 2026-11-04). Mobile access (Phase 5) is not yet built.

## Context
The Admin Portal uploads one multipage salary slip PDF per month. Each page is one person's slip; the system must split it into one PDF per bank account, keep the original page appearance, and store it for later retrieval by the mobile app. No OCR or cloud document service may be used.

## Decisions
1. **Libraries (local only):** UglyToad **PdfPig** reads page text; **PDFsharp** copies the original page objects into per-account PDFs (nothing is re-rendered, so layout and quality are unchanged). Both are managed code with `net8.0` builds and no native dependency.
2. **Account identity:** the number after the `Bank A/c No` label only (`BankAccountNumberParser`). PRAN No and any unlabelled number are never used. A label with two different numbers on one page is ambiguous and the page is reported as unmatched.
3. **Account number storage:** `salary_slips.account_number_encrypted` via `IEncryptionService` (AES-256-CBC, random IV). No hash or lookup column. Because ciphertext is not comparable, **uniqueness of (account, salary month) is enforced in code**: under a per-month lock the month's slips are read, decrypted and compared (`SalarySlipProcessingService`). `user_pis_id` is informational only (no foreign key, ADR-020).
4. **Concurrency:** one MySQL `GET_LOCK('nsg:salary_slips:yyyy-MM')` per salary month, taken on a dedicated open `MySqlConnection`. Batch creation, the duplicate check and the transactional save run on that same connection; `RELEASE_LOCK` runs in `finally` with a non-cancellable token. If the lock cannot be obtained within `SalarySlips:MonthLockTimeoutSeconds` the upload is refused with 409.
5. **Atomicity:** all slips of an upload and the batch summary are saved in one transaction; generated files not covered by a committed save are deleted. Existing PDFs are never overwritten (`FileMode.CreateNew`).
6. **Storage:** local persistent disk under `SalarySlipStorage:RootPath` (absolute, never inside `wwwroot`; since ADR-023 the common `Storage:RootPath`): `salary-slips/{yyyy}/{MM}/{guid}.pdf`, uploads staged in `salary-slips/_incoming`. The database holds the relative path only; every path is verified to stay inside the root. File names carry no personal data.
7. **Admin access:** role `SALARY_MANAGEMENT_ADMIN` (policy `RequireSalaryManagementAdmin`, already seeded and unused before this module). The uploader is always the admin profile id from the token; an admin only sees and downloads their own batches and slips (others read as 404).

## Third-party licences (required by the approval of PDFsharp 6.2.4)
| Package | Version | Licence | Notes |
|---|---|---|---|
| PdfPig | 0.1.16 | Apache-2.0 | Permissive. |
| PDFsharp | **6.2.4 (pinned)** | **MIT** (empira Software GmbH) | No licence key, no runtime licence check. |
| Microsoft.Extensions.Logging.Abstractions, System.Security.Cryptography.Pkcs | 8.0.x | MIT (Microsoft) | Transitive dependencies of PDFsharp. |

**Do not upgrade PDFsharp to 6.5.0 or later without a licensing decision.** PDFsharp 6.5.0 is published under **AGPL-3.0-only** and throws `PdfSharpLicenseException` unless a licence is loaded; the AGPL network-use clause would oblige the deployer to offer the application source to its users unless a commercial licence is bought from empira. 6.3 and 6.4 were never published. 6.2.4 is the last MIT release and receives no further upstream fixes; the package version is therefore pinned explicitly in `NSG.Mobile.Infrastructure.csproj`, and any future upgrade or replacement (for example a PdfPig-based writer) needs a new ADR.

## Consequences / known limits
- Verified on Windows only; Ubuntu 24.04 and the real MySQL lock/transaction path must be smoke-tested (see OQ-045).
- Extraction was tested on generated text PDFs; the real NSG slip layout may need the regex adjusted.
- A hard process crash mid-upload can leave an unreferenced PDF on disk or a batch stuck in `Processing`; neither is ever served.
- Mobile retrieval (Phase 5) must decrypt the month's slips and compare with the person's decrypted PIS account; there is no index on the account.
