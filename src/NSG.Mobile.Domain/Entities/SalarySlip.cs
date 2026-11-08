using NSG.Mobile.Domain.Common;
using NSG.Mobile.Domain.Enums;

namespace NSG.Mobile.Domain.Entities;

/// <summary>
/// One bulk upload of a multipage salary slip PDF by one admin, whatever its outcome. Table <c>salary_slip_upload_batches</c>.
/// Counting rule: every page of the PDF is counted once, as processed or unmatched, so
/// <c>TotalPages = ProcessedPages + UnmatchedPages</c> for a batch that was read to the end.
/// </summary>
public class SalarySlipUploadBatch
{
    public long Id { get; set; }

    /// <summary>File name as supplied by the client. Display only; never used to build a storage path.</summary>
    public string OriginalFileName { get; set; } = string.Empty;

    /// <summary>First day of the salary month (a date; no time and no timezone).</summary>
    public DateOnly SalaryMonth { get; set; }

    public int TotalPages { get; set; }

    /// <summary>Pages that were matched to an account number and written into a stored salary slip.</summary>
    public int ProcessedPages { get; set; }

    /// <summary>Pages without a readable <c>Bank A/c No</c>, without text, or rejected as a duplicate account and month.</summary>
    public int UnmatchedPages { get; set; }

    /// <summary>
    /// 1-based page numbers that were not processed, comma separated (for example <c>3,17,204</c>); null when none.
    /// Never contains page text or account numbers.
    /// </summary>
    public string? UnmatchedPageNumbers { get; set; }

    /// <summary>Client-safe reason when the whole file was refused; null otherwise.</summary>
    public string? FailureMessage { get; set; }

    /// <summary>Admin portal account (<c>admin_profiles.id</c>) that uploaded the file, taken from the token. No foreign key (as <see cref="PisUploadBatch.UploadedBy"/>).</summary>
    public long UploadedBy { get; set; }

    public SalarySlipBatchStatus Status { get; set; } = SalarySlipBatchStatus.Processing;

    public DateTime CreatedAtIst { get; set; } = IndianStandardTime.Now;
    public DateTime UpdatedAtIst { get; set; } = IndianStandardTime.Now;

    public List<SalarySlip> SalarySlips { get; set; } = new();
}

/// <summary>
/// The salary slip PDF of one bank account for one month. Table <c>salary_slips</c>. The PDF holds every page of the
/// upload that carried that account's <c>Bank A/c No</c>.
/// </summary>
public class SalarySlip
{
    public long Id { get; set; }

    /// <summary>
    /// Bank account number from the PDF, encrypted with <c>IEncryptionService</c> (AES-256-CBC, random IV). Ciphertext differs
    /// on every call, so the database cannot compare, index or make this column unique. It is the account identity: the
    /// business rule "one slip per account and <see cref="SalaryMonth"/>" is enforced in code by decrypting and comparing,
    /// under a per-month lock. Mobile retrieval matches on the decrypted PIS bank account number.
    /// </summary>
    public string AccountNumberEncrypted { get; set; } = string.Empty;

    /// <summary>First day of the salary month.</summary>
    public DateOnly SalaryMonth { get; set; }

    /// <summary>
    /// <c>users_pis.id</c> of the person whose <c>SalaryAccountNumberEncrypted</c> matched this account when the slip was
    /// stored; null when no PIS record matched. A soft reference with no foreign key, because <c>users_pis</c> has none in
    /// either direction (ADR-020). Informational only: not unique and not used to authorise or match mobile requests.
    /// </summary>
    public long? UserPisId { get; set; }

    /// <summary>Path of the PDF relative to the configured storage root (never absolute, never inside webroot, never returned to clients).</summary>
    public string FilePath { get; set; } = string.Empty;

    public int PageCount { get; set; }

    /// <summary>The upload batch that produced this slip.</summary>
    public long OriginalBatchId { get; set; }
    public SalarySlipUploadBatch? OriginalBatch { get; set; }

    public DateTime CreatedAtIst { get; set; } = IndianStandardTime.Now;
    public DateTime UpdatedAtIst { get; set; } = IndianStandardTime.Now;
}
