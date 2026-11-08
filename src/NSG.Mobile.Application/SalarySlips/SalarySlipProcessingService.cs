using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using NSG.Mobile.Application.Common;
using NSG.Mobile.Application.Interfaces.Infrastructure;
using NSG.Mobile.Application.Interfaces.Repositories;
using NSG.Mobile.Application.Interfaces.Services;
using NSG.Mobile.Domain.Common;
using NSG.Mobile.Domain.Constants;
using NSG.Mobile.Domain.Entities;
using NSG.Mobile.Domain.Enums;

namespace NSG.Mobile.Application.SalarySlips;

/// <summary>The PDF an admin uploaded for one salary month. <see cref="SalaryMonth"/> may be any day of the month; it is stored as the first.</summary>
public sealed record SalarySlipUploadRequest(Stream Content, string FileName, DateOnly SalaryMonth);

/// <summary>Pages that were not turned into a salary slip, grouped by reason. Page numbers only: no page content, no account numbers.</summary>
public sealed record SalarySlipUnmatchedGroup([property: JsonConverter(typeof(JsonStringEnumConverter))] SalarySlipPageIssue Reason, IReadOnlyList<int> PageNumbers);

/// <summary>Summary of one processed upload.</summary>
public sealed class SalarySlipUploadResultDto
{
    public long BatchId { get; init; }
    public string OriginalFileName { get; init; } = string.Empty;

    /// <summary>First day of the salary month.</summary>
    public DateOnly SalaryMonth { get; init; }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public SalarySlipBatchStatus Status { get; init; }
    public int TotalPages { get; init; }
    public int ProcessedPages { get; init; }
    public int UnmatchedPages { get; init; }

    /// <summary>Salary slip PDFs created: one per distinct account number.</summary>
    public int SalarySlipsCreated { get; init; }

    /// <summary>All unmatched page numbers in ascending order.</summary>
    public IReadOnlyList<int> UnmatchedPageNumbers { get; init; } = Array.Empty<int>();

    public IReadOnlyList<SalarySlipUnmatchedGroup> UnmatchedByReason { get; init; } = Array.Empty<SalarySlipUnmatchedGroup>();
    public string Message { get; init; } = string.Empty;
}

/// <summary>Splits a multipage salary slip PDF into one stored PDF per bank account. Admin only (enforced by the controller policy).</summary>
public interface ISalarySlipProcessingService
{
    /// <summary>
    /// Processes the PDF of one salary month. File-level problems fail the call; page-level problems (no account number, an account that
    /// already has a slip for the month) are reported as unmatched pages and the rest of the file is still processed.
    /// </summary>
    Task<Result<SalarySlipUploadResultDto>> ProcessAsync(SalarySlipUploadRequest request, CancellationToken cancellationToken);
}

public class SalarySlipProcessingService : ISalarySlipProcessingService
{
    private readonly ISalarySlipRepository _repository;
    private readonly ISalarySlipStorageService _storage;
    private readonly ISalarySlipPdfExtractionService _extraction;
    private readonly ISalarySlipPdfGenerationService _generation;
    private readonly IEncryptionService _encryption;
    private readonly IAuditService _audit;
    private readonly ICurrentUserService _currentUser;
    private readonly SalarySlipSettings _settings;
    private readonly ILogger<SalarySlipProcessingService> _logger;

    public SalarySlipProcessingService(ISalarySlipRepository repository, ISalarySlipStorageService storage,
        ISalarySlipPdfExtractionService extraction, ISalarySlipPdfGenerationService generation, IEncryptionService encryption,
        IAuditService audit, ICurrentUserService currentUser, SalarySlipSettings settings, ILogger<SalarySlipProcessingService> logger)
    {
        _repository = repository;
        _storage = storage;
        _extraction = extraction;
        _generation = generation;
        _encryption = encryption;
        _audit = audit;
        _currentUser = currentUser;
        _settings = settings;
        _logger = logger;
    }

    public async Task<Result<SalarySlipUploadResultDto>> ProcessAsync(SalarySlipUploadRequest request, CancellationToken cancellationToken)
    {
        // The uploader comes from the token, never from the request.
        if (!_currentUser.TryGetAdminActor(out AdminActor actor))
            return Result.Failure<SalarySlipUploadResultDto>(ApiErrorCodes.Unauthorized, "The administrator could not be identified.");

        string fileName = SafeFileName(request.FileName);
        if (!fileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
            return Result.Failure<SalarySlipUploadResultDto>(ApiErrorCodes.ValidationFailed, "Only .pdf files are accepted.");

        DateOnly month = new(request.SalaryMonth.Year, request.SalaryMonth.Month, 1);

        // 1. Stage the upload on disk (bounded, signature-checked). Nothing is recorded for a file that is refused here.
        Result<SalarySlipIncomingFile> staged = await _storage.SaveIncomingAsync(request.Content, _settings.MaxPdfSizeBytes, cancellationToken);
        if (!staged.IsSuccess)
            return Result.Failure<SalarySlipUploadResultDto>(staged.ErrorCode ?? ApiErrorCodes.ValidationFailed, staged.ErrorMessage ?? "The file could not be read.");

        await using SalarySlipIncomingFile incoming = staged.Data!;

        // 2. Exclusive lock for this salary month on a dedicated connection. It stays held, on that same connection, until the batch is
        //    saved; disposing the scope releases it on every path (success, failure, cancellation).
        await using ISalarySlipMonthScope? scope = await _repository.TryAcquireMonthScopeAsync(
            month, TimeSpan.FromSeconds(_settings.MonthLockTimeoutSeconds), cancellationToken);
        if (scope == null)
            return Result.Failure<SalarySlipUploadResultDto>(ApiErrorCodes.Conflict,
                "Another salary slip upload for this month is in progress. Please try again shortly.");

        SalarySlipUploadBatch batch = new()
        {
            OriginalFileName = fileName,
            SalaryMonth = month,
            UploadedBy = actor.Id,
            Status = SalarySlipBatchStatus.Processing,
            CreatedAtIst = IndianStandardTime.Now,
            UpdatedAtIst = IndianStandardTime.Now
        };
        batch.Id = await scope.CreateBatchAsync(batch, cancellationToken);

        List<SalarySlipFileLocation> written = new();
        bool committed = false;
        try
        {
            Result<SalarySlipUploadResultDto> result = await RunAsync(scope, batch, incoming.Path, month, written, cancellationToken);
            committed = result.IsSuccess;
            return result;
        }
        catch (OperationCanceledException ex)
        {
            committed = await WasCommittedAsync(scope, batch, ex);
            if (!committed)
                await FailBatchAsync(scope, batch, "The upload was cancelled before it finished.");
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Salary slip upload batch {BatchId} failed", batch.Id);
            committed = await WasCommittedAsync(scope, batch, ex);
            if (!committed)
                await FailBatchAsync(scope, batch, "The upload could not be processed.");
            return committed
                ? Result.Failure<SalarySlipUploadResultDto>(ApiErrorCodes.InternalError, "The upload finished but its result could not be reported. Check the upload history.")
                : Result.Failure<SalarySlipUploadResultDto>(ApiErrorCodes.InternalError, "The upload could not be processed. No salary slip was saved.");
        }
        finally
        {
            // Files exist only for committed rows: anything else (failure, cancellation, rollback) is removed.
            if (!committed)
                await DeleteFilesAsync(written);
        }
    }

    private async Task<Result<SalarySlipUploadResultDto>> RunAsync(ISalarySlipMonthScope scope, SalarySlipUploadBatch batch, string pdfPath,
        DateOnly month, List<SalarySlipFileLocation> written, CancellationToken cancellationToken)
    {
        // 3. Page-wise extraction.
        Result<IReadOnlyList<SalarySlipPageInfo>> extracted = await _extraction.ExtractAsync(pdfPath, _settings.MaxPages, cancellationToken);
        if (!extracted.IsSuccess)
        {
            string message = extracted.ErrorMessage ?? "The PDF could not be read.";
            await FailBatchAsync(scope, batch, message);
            return Result.Failure<SalarySlipUploadResultDto>(ApiErrorCodes.ValidationFailed, message);
        }

        IReadOnlyList<SalarySlipPageInfo> pages = extracted.Data!;
        batch.TotalPages = pages.Count;
        if (pages.Count == 0)
        {
            await FailBatchAsync(scope, batch, "The PDF has no pages.");
            return Result.Failure<SalarySlipUploadResultDto>(ApiErrorCodes.ValidationFailed, "The PDF has no pages.");
        }

        if (pages.All(p => p.Issue == SalarySlipPageIssue.NoText))
        {
            const string noText = "The PDF has no extractable text (scanned images are not supported).";
            await FailBatchAsync(scope, batch, noText);
            return Result.Failure<SalarySlipUploadResultDto>(ApiErrorCodes.ValidationFailed, noText);
        }

        // 4. Group pages by account number; the pages of one account need not be consecutive. Pages without one are unmatched.
        SortedDictionary<int, SalarySlipPageIssue> unmatched = new();
        Dictionary<string, List<int>> groups = new(StringComparer.Ordinal);
        List<string> order = new();
        foreach (SalarySlipPageInfo page in pages)
        {
            if (page.AccountNumber == null)
            {
                unmatched[page.PageNumber] = page.Issue == SalarySlipPageIssue.None ? SalarySlipPageIssue.AccountNumberNotFound : page.Issue;
                continue;
            }

            if (!groups.TryGetValue(page.AccountNumber, out List<int>? list))
            {
                list = new List<int>();
                groups[page.AccountNumber] = list;
                order.Add(page.AccountNumber);
            }

            list.Add(page.PageNumber);
        }

        // 5. Duplicates against earlier uploads. Only this month's slips are read, and only then decrypted, under the month lock.
        HashSet<string> existing = new(StringComparer.Ordinal);
        foreach (string cipher in await scope.GetExistingAccountNumbersAsync(cancellationToken))
        {
            string? plain = TryDecrypt(cipher);
            if (plain != null) existing.Add(plain);
        }

        List<string> accounts = new();
        foreach (string account in order)
        {
            if (existing.Contains(account))
                foreach (int pageNumber in groups[account]) unmatched[pageNumber] = SalarySlipPageIssue.AlreadyExists;
            else
                accounts.Add(account);
        }

        // 6. Which personnel record owns each account (informational user_pis_id). An account shared by two records stays unlinked.
        Dictionary<string, long?> pisByAccount = new(StringComparer.Ordinal);
        if (accounts.Count > 0)
        {
            foreach (PisAccountReference pis in await scope.GetPisAccountsAsync(cancellationToken))
            {
                string? plain = TryDecrypt(pis.AccountNumberEncrypted);
                if (plain == null) continue;
                pisByAccount[plain] = pisByAccount.ContainsKey(plain) ? null : pis.PisId;
            }
        }

        // 7. One PDF per remaining account, copied from the original pages. The source is opened once and closed before the save.
        List<SalarySlip> slips = new();
        if (accounts.Count > 0)
        {
            ISalarySlipPdfSource source;
            try
            {
                source = await _generation.OpenSourceAsync(pdfPath, cancellationToken);
            }
            catch (InvalidDataException)
            {
                const string unreadable = "The PDF could not be split into pages (it may be encrypted or damaged).";
                await FailBatchAsync(scope, batch, unreadable);
                return Result.Failure<SalarySlipUploadResultDto>(ApiErrorCodes.ValidationFailed, unreadable);
            }

            using (source)
            {
                foreach (string account in accounts)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    List<int> accountPages = groups[account];
                    SalarySlipFileLocation location = _storage.ReserveSlipLocation(month);
                    written.Add(location);
                    try
                    {
                        await source.WritePagesAsync(accountPages, location.AbsolutePath, cancellationToken);
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Salary slip PDF could not be generated in batch {BatchId} ({PageCount} pages)", batch.Id, accountPages.Count);
                        await _storage.DeleteAsync(location, CancellationToken.None);
                        written.Remove(location);
                        foreach (int pageNumber in accountPages) unmatched[pageNumber] = SalarySlipPageIssue.GenerationFailed;
                        continue;
                    }

                    DateTime now = IndianStandardTime.Now;
                    slips.Add(new SalarySlip
                    {
                        AccountNumberEncrypted = _encryption.Encrypt(account),
                        SalaryMonth = month,
                        UserPisId = pisByAccount.TryGetValue(account, out long? pisId) ? pisId : null,
                        FilePath = location.RelativePath,
                        PageCount = accountPages.Count,
                        OriginalBatchId = batch.Id,
                        CreatedAtIst = now,
                        UpdatedAtIst = now
                    });
                }
            }
        }

        // 8. Final statistics and status.
        batch.ProcessedPages = slips.Sum(s => s.PageCount);
        batch.UnmatchedPages = unmatched.Count;
        batch.UnmatchedPageNumbers = unmatched.Count == 0 ? null : string.Join(',', unmatched.Keys);
        batch.UpdatedAtIst = IndianStandardTime.Now;

        if (slips.Count == 0)
        {
            const string none = "No salary slip could be created from this file.";
            await FailBatchAsync(scope, batch, none);
            return Result.Failure<SalarySlipUploadResultDto>(ApiErrorCodes.ValidationFailed, none);
        }

        batch.Status = unmatched.Count == 0 ? SalarySlipBatchStatus.Completed : SalarySlipBatchStatus.CompletedWithErrors;

        // 9. All slips and the batch summary in one transaction on the lock connection: all or nothing.
        await scope.SaveSlipsAndCompleteBatchAsync(batch, slips, cancellationToken);

        await AuditAsync(batch, slips.Count);
        return Result.Success(ToResult(batch, slips.Count, unmatched));
    }

    private string? TryDecrypt(string cipherText)
    {
        try
        {
            return _encryption.Decrypt(cipherText);
        }
        catch (Exception ex) when (ex is FormatException or System.Security.Cryptography.CryptographicException or ArgumentException)
        {
            _logger.LogWarning("A stored account number could not be decrypted and was skipped.");
            return null;
        }
    }

    /// <summary>After a failed save: did the transaction commit anyway? If that cannot be determined the files are kept (orphans are safer than dangling rows).</summary>
    private async Task<bool> WasCommittedAsync(ISalarySlipMonthScope scope, SalarySlipUploadBatch batch, Exception cause)
    {
        if (batch.Status is not (SalarySlipBatchStatus.Completed or SalarySlipBatchStatus.CompletedWithErrors))
            return false;   // the failure happened before the save started

        try
        {
            return await scope.CountSlipsOfBatchAsync(batch.Id, CancellationToken.None) > 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not determine whether salary slip batch {BatchId} was committed after {Cause}; generated files are kept", batch.Id, cause.GetType().Name);
            return true;
        }
    }

    private async Task FailBatchAsync(ISalarySlipMonthScope scope, SalarySlipUploadBatch batch, string message)
    {
        batch.Status = SalarySlipBatchStatus.Failed;
        batch.ProcessedPages = 0;
        batch.FailureMessage = Truncate(message, 500);
        batch.UpdatedAtIst = IndianStandardTime.Now;
        try
        {
            await scope.UpdateBatchAsync(batch, CancellationToken.None);
            await AuditAsync(batch, 0);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Salary slip batch {BatchId} could not be marked Failed", batch.Id);
        }
    }

    private async Task DeleteFilesAsync(IEnumerable<SalarySlipFileLocation> files)
    {
        foreach (SalarySlipFileLocation file in files.ToList())
        {
            try
            {
                await _storage.DeleteAsync(file, CancellationToken.None);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Generated salary slip file {RelativePath} could not be removed", file.RelativePath);
            }
        }
    }

    private async Task AuditAsync(SalarySlipUploadBatch batch, int slipsCreated)
    {
        try
        {
            // Counts and ids only: never account numbers or page content.
            await _audit.LogAsync(AuditAction.AdminAction, batch.Status == SalarySlipBatchStatus.Failed ? DomainConstants.AuditResults.Failure : DomainConstants.AuditResults.Success,
                userId: _currentUser.AuditUserId(), entityType: "SalarySlipUploadBatch", entityId: batch.Id.ToString(), correlationId: _currentUser.CorrelationId,
                additionalData: $"{{\"operation\":\"SalarySlipUpload\",\"outcome\":\"{batch.Status}\",\"adminProfileId\":{batch.UploadedBy},\"salaryMonth\":\"{batch.SalaryMonth:yyyy-MM}\",\"totalPages\":{batch.TotalPages},\"processedPages\":{batch.ProcessedPages},\"unmatchedPages\":{batch.UnmatchedPages},\"slips\":{slipsCreated}}}",
                cancellationToken: CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Audit entry for salary slip batch {BatchId} could not be written", batch.Id);
        }

        _logger.LogInformation("Salary slip batch {BatchId} by admin {AdminId}: {Status} (pages {Total}, processed {Processed}, unmatched {Unmatched}, slips {Slips})",
            batch.Id, batch.UploadedBy, batch.Status, batch.TotalPages, batch.ProcessedPages, batch.UnmatchedPages, slipsCreated);
    }

    private static SalarySlipUploadResultDto ToResult(SalarySlipUploadBatch batch, int slipsCreated, SortedDictionary<int, SalarySlipPageIssue> unmatched)
    {
        List<SalarySlipUnmatchedGroup> groups = unmatched
            .GroupBy(p => p.Value, p => p.Key)
            .Select(g => new SalarySlipUnmatchedGroup(g.Key, g.OrderBy(n => n).ToList()))
            .OrderBy(g => g.Reason)
            .ToList();

        string message = unmatched.Count == 0
            ? "Salary slips created successfully."
            : $"Salary slips created, but {unmatched.Count} page(s) were not processed. See the unmatched page numbers.";

        return new SalarySlipUploadResultDto
        {
            BatchId = batch.Id,
            OriginalFileName = batch.OriginalFileName,
            SalaryMonth = batch.SalaryMonth,
            Status = batch.Status,
            TotalPages = batch.TotalPages,
            ProcessedPages = batch.ProcessedPages,
            UnmatchedPages = batch.UnmatchedPages,
            SalarySlipsCreated = slipsCreated,
            UnmatchedPageNumbers = unmatched.Keys.ToList(),
            UnmatchedByReason = groups,
            Message = message
        };
    }

    private static string SafeFileName(string? name)
    {
        string baseName = Path.GetFileName((name ?? string.Empty).Replace('\\', '/'));
        baseName = new string(baseName.Where(c => !char.IsControl(c)).ToArray()).Trim();
        if (baseName.Length == 0) baseName = "salary-slips.pdf";
        return baseName.Length > 255 ? baseName[^255..] : baseName;
    }

    private static string Truncate(string value, int max) => value.Length <= max ? value : value[..max];
}
