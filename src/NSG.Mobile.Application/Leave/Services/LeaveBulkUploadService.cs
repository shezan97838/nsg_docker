using System.Text.Json;
using Microsoft.Extensions.Logging;
using NSG.Mobile.Application.Common;
using NSG.Mobile.Application.Interfaces.Repositories;
using NSG.Mobile.Application.Interfaces.Services;
using NSG.Mobile.Application.Leave.DTOs;
using NSG.Mobile.Application.Leave.Excel;
using NSG.Mobile.Domain.Common;
using NSG.Mobile.Domain.Constants;
using NSG.Mobile.Domain.Entities;
using NSG.Mobile.Domain.Enums;

namespace NSG.Mobile.Application.Leave.Services;

/// <summary>An uploaded workbook handed over by the API layer (no ASP.NET types in the Application layer).</summary>
public sealed record LeaveUploadFile(Stream Content, string FileName, long Length);

/// <summary>Excel bulk upload of leave totals and the read side of upload batches (Leave Admin only, enforced by the policy on the controller).</summary>
public interface ILeaveBulkUploadService
{
    /// <summary>
    /// Validates the whole workbook and, only if every row is valid, applies it in ONE transaction (all or nothing). Every attempt that reaches this
    /// method is recorded as a batch, including failed ones. A failure result names the batch so the admin can fetch the row-level report.
    /// </summary>
    Task<Result<LeaveUploadBatchDto>> UploadAsync(LeaveUploadFile file, CancellationToken cancellationToken);

    Task<Result<LeaveUploadBatchDto>> GetBatchAsync(long batchId, CancellationToken cancellationToken);

    Task<Result<PaginatedResponse<LeaveUploadBatchDto>>> GetBatchesAsync(int pageNumber, int pageSize, LeaveUploadStatus? status, CancellationToken cancellationToken);

    Task<Result<PaginatedResponse<LeaveUploadErrorDto>>> GetErrorsAsync(long batchId, int pageNumber, int pageSize, CancellationToken cancellationToken);
}

public class LeaveBulkUploadService : ILeaveBulkUploadService
{
    private readonly ILeaveRepository _repository;
    private readonly LeaveSettings _settings;
    private readonly IAuditService _audit;
    private readonly ICurrentUserService _currentUser;
    private readonly TimeProvider _time;
    private readonly ILogger<LeaveBulkUploadService> _logger;

    public LeaveBulkUploadService(ILeaveRepository repository, LeaveSettings settings, IAuditService audit, ICurrentUserService currentUser,
        TimeProvider time, ILogger<LeaveBulkUploadService> logger)
    {
        _repository = repository;
        _settings = settings;
        _audit = audit;
        _currentUser = currentUser;
        _time = time;
        _logger = logger;
    }

    private DateTime NowIst() => IndianStandardTime.ConvertFromUtc(_time.GetUtcNow().UtcDateTime);

    public async Task<Result<LeaveUploadBatchDto>> UploadAsync(LeaveUploadFile file, CancellationToken cancellationToken)
    {
        long? adminId = _currentUser.AdminProfileId;
        if (adminId == null)
            return Result.Failure<LeaveUploadBatchDto>(ApiErrorCodes.Unauthorized, LeaveMessages.IdentityMissing);

        DateTime startedAt = NowIst();
        int year = LeaveRules.CurrentYear(startedAt);

        // The batch is written first: from here on every outcome, including a crash, leaves a trace the admin can look up.
        long? batchId = await _repository.TryStartBatchAsync(SafeFileName(file.FileName), year, adminId.Value, startedAt,
            TimeSpan.FromMinutes(_settings.StaleBatchMinutes), cancellationToken);
        if (batchId == null)
            return Result.Failure<LeaveUploadBatchDto>(ApiErrorCodes.Conflict, LeaveMessages.UploadInProgress);

        int totalRows = 0;
        try
        {
            // ── 1. File-level checks ──
            string? fileProblem = CheckFile(file);
            byte[]? content = null;
            if (fileProblem == null)
            {
                content = await ReadBoundedAsync(file.Content, _settings.MaxFileSizeBytes, cancellationToken);
                if (content == null) fileProblem = FileTooLarge();
            }

            IReadOnlyList<LeaveExcelRow>? excelRows = null;
            if (fileProblem == null)
            {
                Result<IReadOnlyList<LeaveExcelRow>> parsed = LeaveExcelReader.Read(content!, _settings.MaxRows);
                if (parsed.IsSuccess) excelRows = parsed.Data!;
                else fileProblem = parsed.ErrorMessage;
            }

            if (fileProblem != null)
                return await RejectAsync(batchId.Value, adminId.Value, 0, 1, new[] { new LeaveUploadIssue(null, null, fileProblem) }, cancellationToken);

            totalRows = excelRows!.Count;

            // ── 2. Row validation: format, duplicates inside the file, existence in the personnel records ──
            List<LeaveUploadIssue> issues = new();
            List<(LeaveExcelRow Row, string Nsg, int Leave)> candidates = new();
            foreach (LeaveExcelRow row in excelRows)
            {
                string? nsgProblem = LeaveRules.ValidateNsgNumber(row.NsgNumber, LeaveLimits.TemplateNsgColumn, out string nsg);
                string? leaveProblem = LeaveRules.ValidateLeaveText(row.LeaveTaken, LeaveLimits.TemplateLeaveColumn, out int leave);
                if (nsgProblem != null) issues.Add(Issue(row, nsgProblem));
                if (leaveProblem != null) issues.Add(Issue(row, leaveProblem));
                if (nsgProblem == null && leaveProblem == null) candidates.Add((row, nsg, leave));
            }

            // A duplicate is an error for EVERY row that carries the number: the system never picks one silently.
            HashSet<int> duplicateRows = new();
            foreach (IGrouping<string, (LeaveExcelRow Row, string Nsg, int Leave)> group in
                     candidates.GroupBy(c => c.Nsg.ToUpperInvariant()).Where(g => g.Count() > 1))
            {
                string rowList = string.Join(", ", group.Select(c => c.Row.RowNumber));
                foreach ((LeaveExcelRow row, _, _) in group)
                {
                    issues.Add(Issue(row, $"{LeaveLimits.TemplateNsgColumn} appears more than once in the file (rows {rowList})."));
                    duplicateRows.Add(row.RowNumber);
                }
            }

            List<(LeaveExcelRow Row, string Nsg, int Leave)> unique = candidates.Where(c => !duplicateRows.Contains(c.Row.RowNumber)).ToList();

            IReadOnlyDictionary<string, string> personnel =
                await _repository.GetActivePersonnelNsgNumbersAsync(unique.Select(c => c.Nsg).ToList(), cancellationToken);
            List<LeaveBulkRow> valid = new();
            foreach ((LeaveExcelRow row, string nsg, int leave) in unique)
            {
                if (personnel.TryGetValue(nsg.ToUpperInvariant(), out string? canonical))
                    valid.Add(new LeaveBulkRow(row.RowNumber, canonical, leave));
                else
                    issues.Add(Issue(row, $"{LeaveLimits.TemplateNsgColumn} was not found among active personnel."));
            }

            if (issues.Count > 0)
            {
                int failedRows = issues.Select(i => i.RowNumber).Distinct().Count();
                return await RejectAsync(batchId.Value, adminId.Value, totalRows, failedRows,
                    issues.OrderBy(i => i.RowNumber).ThenBy(i => i.Message, StringComparer.Ordinal).ToList(), cancellationToken);
            }

            // ── 3. Apply: one transaction, all or nothing ──
            LeaveBulkResult applied = await _repository.ApplyBulkAsync(batchId.Value, year, totalRows, valid, adminId.Value, NowIst(), cancellationToken);
            switch (applied.Outcome)
            {
                case LeaveBulkOutcome.Committed:
                    await AuditAsync(batchId.Value, adminId.Value, DomainConstants.AuditResults.Success, "Completed", applied.Created, applied.Updated, applied.Unchanged);
                    PersonnelLeaveUploadBatch? done = await _repository.GetBatchAsync(batchId.Value, CancellationToken.None);
                    return Result.Success(done != null ? done.ToDto() : SummaryOf(batchId.Value, file, year, startedAt, adminId.Value, totalRows, applied));

                case LeaveBulkOutcome.RolledBackConflict:
                    return await FailProcessingAsync(batchId.Value, adminId.Value, totalRows, ApiErrorCodes.Conflict,
                        "A leave record was created or changed by someone else while the upload was running. Nothing was saved; upload the file again.", cancellationToken);

                case LeaveBulkOutcome.RolledBackFailure:
                    return await FailProcessingAsync(batchId.Value, adminId.Value, totalRows, LeaveErrorCodes.ProcessingFailed,
                        "The upload could not be saved because of a database error. The transaction was rolled back and no leave record was changed.", cancellationToken);

                default:
                    // The transaction failed and its outcome could not be read back. Nothing is claimed: the batch stays Processing and
                    // is neither marked failed nor completed; the admin checks it before retrying.
                    await AuditAsync(batchId.Value, adminId.Value, DomainConstants.AuditResults.Failure, "OutcomeUnknown", 0, 0, 0);
                    return Result.Failure<LeaveUploadBatchDto>(LeaveErrorCodes.OutcomeUnknown,
                        $"The upload failed and the result could not be confirmed. Do not upload again yet: check the status of batch {batchId.Value} " +
                        "(it stays 'Processing' until it is resolved) and the leave records first.");
            }
        }
        catch (Exception ex)
        {
            // Unexpected failure before or after the transaction. The transaction itself reports through ApplyBulkAsync, so anything caught here
            // happened outside it, or inside its verification: record the failure if possible, then let the global handler answer with a generic 500.
            _logger.LogError(ex, "Leave upload batch {BatchId} failed unexpectedly.", batchId);
            await TryFinishAsync(batchId.Value, LeaveUploadStatus.ProcessingFailed, totalRows, 1,
                new[] { new LeaveUploadIssue(null, null, "The upload failed unexpectedly. No leave record was changed by this step.") });
            throw;
        }
    }

    public async Task<Result<LeaveUploadBatchDto>> GetBatchAsync(long batchId, CancellationToken cancellationToken)
    {
        PersonnelLeaveUploadBatch? batch = await _repository.GetBatchAsync(batchId, cancellationToken);
        return batch == null
            ? Result.Failure<LeaveUploadBatchDto>(ApiErrorCodes.NotFound, LeaveMessages.BatchNotFound)
            : Result.Success(batch.ToDto());
    }

    public async Task<Result<PaginatedResponse<LeaveUploadBatchDto>>> GetBatchesAsync(int pageNumber, int pageSize, LeaveUploadStatus? status, CancellationToken cancellationToken)
    {
        (IReadOnlyList<PersonnelLeaveUploadBatch> items, int total) = await _repository.GetBatchesPagedAsync(pageNumber, pageSize, status, cancellationToken);
        return Result.Success(new PaginatedResponse<LeaveUploadBatchDto>(items.Select(b => b.ToDto()).ToList(), total, pageNumber, pageSize));
    }

    public async Task<Result<PaginatedResponse<LeaveUploadErrorDto>>> GetErrorsAsync(long batchId, int pageNumber, int pageSize, CancellationToken cancellationToken)
    {
        if (await _repository.GetBatchAsync(batchId, cancellationToken) == null)
            return Result.Failure<PaginatedResponse<LeaveUploadErrorDto>>(ApiErrorCodes.NotFound, LeaveMessages.BatchNotFound);

        (IReadOnlyList<PersonnelLeaveUploadError> items, int total) = await _repository.GetBatchErrorsPagedAsync(batchId, pageNumber, pageSize, cancellationToken);
        return Result.Success(new PaginatedResponse<LeaveUploadErrorDto>(items.Select(e => e.ToDto()).ToList(), total, pageNumber, pageSize));
    }

    // ── Outcomes ──

    private async Task<Result<LeaveUploadBatchDto>> RejectAsync(
        long batchId, long adminId, int totalRows, int failedRows, IReadOnlyList<LeaveUploadIssue> issues, CancellationToken cancellationToken)
    {
        List<LeaveUploadIssue> stored = CapIssues(issues);
        bool recorded = await TryFinishAsync(batchId, LeaveUploadStatus.ValidationFailed, totalRows, failedRows, stored);
        await AuditAsync(batchId, adminId, DomainConstants.AuditResults.Failure, "ValidationFailed", 0, 0, 0);

        string first = issues[0].RowNumber is int row ? $"First problem, row {row}: {issues[0].Message}" : issues[0].Message;
        string message = totalRows == 0
            ? $"Upload rejected: {first} No leave record was changed."
            : $"Upload rejected: {failedRows} of {totalRows} rows have errors. {first} No leave record was changed.";
        message += recorded
            ? $" Row-level report: batch {batchId} (GET /api/v1/admin/leave-uploads/{batchId}/errors)."
            : $" The error report for batch {batchId} could not be saved; fix the file using the message above and upload again.";
        return Result.Failure<LeaveUploadBatchDto>(ApiErrorCodes.ValidationFailed, message);
    }

    private async Task<Result<LeaveUploadBatchDto>> FailProcessingAsync(
        long batchId, long adminId, int totalRows, string errorCode, string reason, CancellationToken cancellationToken)
    {
        bool recorded = await TryFinishAsync(batchId, LeaveUploadStatus.ProcessingFailed, totalRows, totalRows, new[] { new LeaveUploadIssue(null, null, reason) });
        await AuditAsync(batchId, adminId, DomainConstants.AuditResults.Failure, "ProcessingFailed", 0, 0, 0);

        string message = recorded
            ? $"{reason} See batch {batchId}."
            : $"{reason} The failure record for batch {batchId} could not be saved; the batch may still show 'Processing' until it is closed automatically.";
        return Result.Failure<LeaveUploadBatchDto>(errorCode, message);
    }

    /// <summary>Ends a failed batch. Uses its own token: a client that disconnected must not stop the failure from being recorded. False when it could not be saved.</summary>
    private async Task<bool> TryFinishAsync(long batchId, LeaveUploadStatus status, int totalRows, int failedCount, IReadOnlyList<LeaveUploadIssue> issues)
    {
        try
        {
            await _repository.FinishFailedBatchAsync(batchId, status, totalRows, failedCount, issues, NowIst(), CancellationToken.None);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "The failure of leave upload batch {BatchId} could not be recorded.", batchId);
            return false;
        }
    }

    private static List<LeaveUploadIssue> CapIssues(IReadOnlyList<LeaveUploadIssue> issues)
    {
        List<LeaveUploadIssue> stored = issues.Take(LeaveLimits.MaxStoredErrors).Select(Trimmed).ToList();
        if (issues.Count > LeaveLimits.MaxStoredErrors)
            stored.Add(new LeaveUploadIssue(null, null, $"{issues.Count - LeaveLimits.MaxStoredErrors} more problems were found and are not listed."));
        return stored;
    }

    private static LeaveUploadIssue Trimmed(LeaveUploadIssue i) => new(
        i.RowNumber,
        i.NsgNumber is { Length: > LeaveLimits.NsgNumberMaxLength } n ? n[..LeaveLimits.NsgNumberMaxLength] : i.NsgNumber,
        i.Message.Length > LeaveLimits.ErrorMessageMaxLength ? i.Message[..LeaveLimits.ErrorMessageMaxLength] : i.Message);

    private static LeaveUploadIssue Issue(LeaveExcelRow row, string message) => new(row.RowNumber, row.NsgNumber, message);

    private static LeaveUploadBatchDto SummaryOf(long id, LeaveUploadFile file, int year, DateTime startedAt, long adminId, int totalRows, LeaveBulkResult applied) => new()
    {
        Id = id,
        FileName = SafeFileName(file.FileName),
        LeaveYear = year,
        UploadStatus = LeaveUploadStatus.Completed.ToString(),
        TotalRows = totalRows,
        CreatedCount = applied.Created,
        UpdatedCount = applied.Updated,
        UnchangedCount = applied.Unchanged,
        CreatedAt = IstTime.FromIst(startedAt),
        CreatedBy = adminId
    };

    private Task AuditAsync(long batchId, long adminId, string result, string status, int created, int updated, int unchanged)
        => _audit.LogAsync(AuditAction.LeaveBulkUploadProcessed, result,
            entityType: nameof(PersonnelLeaveUploadBatch), entityId: batchId.ToString(), correlationId: _currentUser.CorrelationId,
            additionalData: JsonSerializer.Serialize(new { actorId = adminId, status, created, updated, unchanged }),
            cancellationToken: CancellationToken.None);

    // ── File helpers ──

    private string? CheckFile(LeaveUploadFile file)
    {
        if (file.Length <= 0) return LeaveMessages.FileRequired;
        if (!string.Equals(Path.GetExtension(file.FileName ?? string.Empty), LeaveLimits.AllowedExtension, StringComparison.OrdinalIgnoreCase))
            return LeaveMessages.UnsupportedFile;
        if (file.Length > _settings.MaxFileSizeBytes) return FileTooLarge();
        return null;
    }

    private string FileTooLarge() => $"The file is larger than {_settings.MaxFileSizeBytes / (1024.0 * 1024.0):0.#} MB, which is the most one upload accepts.";

    /// <summary>Reads at most <paramref name="limit"/> bytes; null when the stream has more (the declared length cannot be trusted).</summary>
    private static async Task<byte[]?> ReadBoundedAsync(Stream stream, long limit, CancellationToken cancellationToken)
    {
        using MemoryStream buffer = new();
        byte[] chunk = new byte[81920];
        int read;
        while ((read = await stream.ReadAsync(chunk, cancellationToken)) > 0)
        {
            if (buffer.Length + read > limit) return null;
            buffer.Write(chunk, 0, read);
        }

        return buffer.ToArray();
    }

    /// <summary>The client's file name reduced to a safe display value: no directories, no control characters, at most 255 characters. Never used as a path.</summary>
    public static string SafeFileName(string? name)
    {
        string leaf = Path.GetFileName((name ?? string.Empty).Replace('\\', '/'));
        string cleaned = new(leaf.Where(c => !char.IsControl(c)).ToArray());
        cleaned = cleaned.Trim();
        if (cleaned.Length == 0) cleaned = "upload.xlsx";
        return cleaned.Length > LeaveLimits.FileNameMaxLength ? cleaned[..LeaveLimits.FileNameMaxLength] : cleaned;
    }
}
