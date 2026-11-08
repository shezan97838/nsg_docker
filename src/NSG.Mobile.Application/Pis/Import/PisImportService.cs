using Microsoft.Extensions.Logging;
using NSG.Mobile.Application.Common;
using NSG.Mobile.Application.Interfaces.Infrastructure;
using NSG.Mobile.Application.Interfaces.Repositories;
using NSG.Mobile.Application.Interfaces.Services;
using NSG.Mobile.Application.Pis.Mappings;
using NSG.Mobile.Domain.Common;
using NSG.Mobile.Domain.Constants;
using NSG.Mobile.Domain.Entities;
using NSG.Mobile.Domain.Enums;

namespace NSG.Mobile.Application.Pis.Import;

/// <summary>PIS Excel upload (insert + update, or update only) and the admin's own upload history. PIS Admin only (enforced by the controller policy).</summary>
public interface IPisImportService
{
    /// <summary>
    /// Processes the workbook row by row (partial success): every valid row is saved in its own transaction, every rejected row is
    /// reported with its worksheet row number. A file-level problem (format, headings, size) fails the whole call and saves nothing.
    /// </summary>
    Task<Result<PisUploadResultDto>> UploadAsync(PisUploadFile file, PisUploadType type, CancellationToken cancellationToken);

    /// <summary>The calling admin's batches, newest first.</summary>
    Task<Result<PisPageDto<PisUploadBatchSummaryDto>>> GetBatchesAsync(int pageNumber, int pageSize, CancellationToken cancellationToken);

    /// <summary>One of the calling admin's batches; 404 for any other batch id.</summary>
    Task<Result<PisUploadBatchDetailDto>> GetBatchAsync(long batchId, int pageNumber, int pageSize, CancellationToken cancellationToken);
}

public class PisImportService : IPisImportService
{
    private readonly IPisImportRepository _repository;
    private readonly IEncryptionService _encryption;
    private readonly IAuditService _audit;
    private readonly ICurrentUserService _currentUser;
    private readonly ILogger<PisImportService> _logger;

    public PisImportService(IPisImportRepository repository, IEncryptionService encryption, IAuditService audit,
        ICurrentUserService currentUser, ILogger<PisImportService> logger)
    {
        _repository = repository;
        _encryption = encryption;
        _audit = audit;
        _currentUser = currentUser;
        _logger = logger;
    }

    public async Task<Result<PisUploadResultDto>> UploadAsync(PisUploadFile file, PisUploadType type, CancellationToken cancellationToken)
    {
        // The uploader is resolved ONCE per request and passed explicitly to everything below. An unusable id (missing, not positive, or too large for
        // the integer provenance/audit columns) rejects the whole upload here, before the batch row or any personnel row is written.
        if (!_currentUser.TryGetAdminActor(out AdminActor actor))
            return Result.Failure<PisUploadResultDto>(ApiErrorCodes.Unauthorized, AdminActorExtensions.AdminNotIdentifiedMessage);

        string fileName = SafeFileName(file.FileName);
        PisUploadBatch batch = new() { FileName = fileName, UploadType = type, UploadedBy = actor.Id, CreatedAtIst = IndianStandardTime.Now };
        batch.Id = await _repository.CreateBatchAsync(batch, cancellationToken);

        List<PisUploadBatchError> errors = new();
        try
        {
            // ── 1. File-level checks ──
            string? problem = null;
            byte[]? content = null;
            if (!fileName.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase))
                problem = "Only .xlsx workbooks are accepted.";
            else if (file.Length > PisImportLimits.MaxFileBytes)
                problem = $"The file is larger than the {PisImportLimits.MaxFileBytes / (1024 * 1024)} MB limit.";
            else
            {
                content = await ReadBoundedAsync(file.Content, PisImportLimits.MaxFileBytes, cancellationToken);
                if (content == null) problem = $"The file is larger than the {PisImportLimits.MaxFileBytes / (1024 * 1024)} MB limit.";
            }

            PisExcelSheet? sheet = null;
            if (problem == null)
            {
                Result<PisExcelSheet> read = PisExcelReader.Read(content!, PisImportLimits.MaxRows);
                if (read.IsSuccess) sheet = read.Data;
                else problem = read.ErrorMessage;
            }

            if (problem != null || sheet == null)
            {
                batch.Status = PisUploadStatus.Rejected;
                batch.FailureMessage = Truncate(problem ?? "The file could not be read.", 500);
                batch.CompletedAtIst = IndianStandardTime.Now;
                await _repository.CompleteBatchAsync(batch, CancellationToken.None);
                await AuditAsync(batch, "Rejected", actor);
                return Result.Failure<PisUploadResultDto>(ApiErrorCodes.ValidationFailed, batch.FailureMessage);
            }

            // ── 2. Parse and validate every row ──
            List<PisParsedRow> rows = sheet.Rows.Select(PisRowParser.Parse).ToList();
            batch.TotalRows = rows.Count;
            ApplyFileLevelConflicts(rows);

            // ── 3. Existing records, mandatory-field rule, mobile ownership ──
            IReadOnlyDictionary<string, UserPis> existing = await _repository.GetByNsgNumbersAsync(
                rows.Where(r => r.NsgNumber != null).Select(r => r.NsgNumber!).ToList(), cancellationToken);
            await ApplyDatabaseChecksAsync(rows, type, existing, actor.Id, cancellationToken);

            // ── 4. Save the clean rows, each in its own transaction ──
            List<int> untrackedRows = new();
            foreach (PisParsedRow row in rows)
            {
                if (row.Issues.Count == 0)
                    await SaveRowAsync(row, type, existing, batch, actor, cancellationToken);

                if (row.Issues.Count > 0)
                {
                    // The row's own transaction has already rolled back (or it was never started). Its error rows are written NOW, in
                    // a separate transaction, so a later crash cannot lose them; a failure to write them is surfaced, never swallowed.
                    List<PisUploadBatchError> rowErrors = CountRejected(row, batch);
                    errors.AddRange(rowErrors);
                    if (!await TrySaveErrorsAsync(batch.Id, row.RowNumber, rowErrors))
                        untrackedRows.Add(row.RowNumber);
                }
            }

            // Final summary: only after every row has been processed.
            batch.Status = batch.FailedCount + batch.ConflictCount == 0 ? PisUploadStatus.Completed : PisUploadStatus.CompletedWithErrors;
            if (untrackedRows.Count > 0)
            {
                batch.Status = PisUploadStatus.CompletedWithErrors;
                batch.FailureMessage = Truncate("Error details could not be saved for worksheet rows: " + string.Join(", ", untrackedRows)
                    + ". They are listed in the upload response only.", 500);
            }

            batch.CompletedAtIst = IndianStandardTime.Now;
            await _repository.CompleteBatchAsync(batch, CancellationToken.None);
            await AuditAsync(batch, batch.Status.ToString(), actor);

            PisUploadResultDto result = ToResult(batch, errors);
            result.ErrorTrackingFailedRows = untrackedRows;
            return Result.Success(result);
        }
        catch (OperationCanceledException)
        {
            await TryFailBatchAsync(batch, errors, "The upload was cancelled before it finished.");
            throw;
        }
        catch (Exception ex)
        {
            // Only the exception type is logged: messages from the database layer can echo row values.
            _logger.LogError("PIS upload batch {BatchId} stopped unexpectedly ({ExceptionType}).", batch.Id, ex.GetType().Name);
            await TryFailBatchAsync(batch, errors, "The upload stopped unexpectedly. Rows saved before the stop are kept; check the batch for details.");
            return Result.Failure<PisUploadResultDto>(ApiErrorCodes.InternalError,
                $"The upload stopped unexpectedly (batch {batch.Id}). Rows saved before the stop are kept; review the batch before uploading again.");
        }
    }

    // ── Cross-row conflicts inside the file ──

    private static void ApplyFileLevelConflicts(List<PisParsedRow> rows)
    {
        // A duplicated NSG number rejects EVERY row that carries it: the system never picks one silently.
        foreach (IGrouping<string, PisParsedRow> group in rows.Where(r => r.NsgNumber != null)
                     .GroupBy(r => r.NsgNumber!, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1))
        {
            string rowList = string.Join(", ", group.Select(r => r.RowNumber));
            foreach (PisParsedRow row in group)
                row.Issues.Add(new PisRowIssue(PisRowStatus.Conflict, PisImportConflictTypes.DuplicateNsgNumberInFile, "nsgNumber",
                    row.NsgNumber, $"This NSG number appears more than once in the file (rows {rowList})."));
        }

        // The same mobile number on different NSG numbers rejects all of those rows.
        foreach (IGrouping<string, PisParsedRow> group in rows.Where(r => r.NsgNumber != null && !string.IsNullOrEmpty(r.Values.MobileNumber))
                     .GroupBy(r => r.Values.MobileNumber))
        {
            List<PisParsedRow> members = group.ToList();
            if (members.Select(r => r.NsgNumber!).Distinct(StringComparer.OrdinalIgnoreCase).Count() < 2) continue;

            string rowList = string.Join(", ", members.Select(r => r.RowNumber));
            foreach (PisParsedRow row in members)
            {
                string? other = members.Select(r => r.NsgNumber!).FirstOrDefault(n => !string.Equals(n, row.NsgNumber, StringComparison.OrdinalIgnoreCase));
                row.Issues.Add(new PisRowIssue(PisRowStatus.Conflict, PisImportConflictTypes.DuplicateMobileNumberInFile, "mobileNumber",
                    row.Values.MobileNumber,
                    $"This mobile number is used by different NSG numbers in the file (rows {rowList}: NSG numbers {string.Join(", ", members.Select(r => r.NsgNumber).Distinct(StringComparer.OrdinalIgnoreCase))}).",
                    other));
            }
        }
    }

    private async Task ApplyDatabaseChecksAsync(List<PisParsedRow> rows, PisUploadType type,
        IReadOnlyDictionary<string, UserPis> existing, long adminId, CancellationToken cancellationToken)
    {
        foreach (PisParsedRow row in rows.Where(r => r.NsgNumber != null))
        {
            bool exists = existing.TryGetValue(row.NsgNumber!, out UserPis? current);

            // Ownership first, for both upload types: only the PIS Admin the record is assigned to may update it. An unassigned record is
            // never claimed by an upload. Nothing else about the stored record is checked or reported to a non-owner.
            if (exists && OwnershipIssue(current!, adminId, row.NsgNumber) is { } ownership)
            {
                row.Issues.Add(ownership);
                continue;
            }

            if (type == PisUploadType.UpdateOnly && !exists)
            {
                if (row.Issues.Count == 0)
                    row.Issues.Add(new PisRowIssue(PisRowStatus.Failed, PisImportConflictTypes.NsgNumberNotFound, "nsgNumber", row.NsgNumber,
                        "Personnel record does not exist. Update-only upload cannot insert new records."));
                continue;
            }

            AddMandatoryIssues(row, current);
        }

        List<PisParsedRow> clean = rows.Where(r => r.Issues.Count == 0).ToList();
        if (clean.Count == 0) return;

        IReadOnlyDictionary<string, string> owners =
            await _repository.GetNsgNumbersByMobileAsync(clean.Select(r => r.Values.MobileNumber).Where(m => m.Length > 0).Distinct().ToList(), cancellationToken);
        foreach (PisParsedRow row in clean)
        {
            if (owners.TryGetValue(row.Values.MobileNumber, out string? owner) && !string.Equals(owner, row.NsgNumber, StringComparison.OrdinalIgnoreCase))
                row.Issues.Add(new PisRowIssue(PisRowStatus.Conflict, PisImportConflictTypes.DuplicateMobileNumber, "mobileNumber",
                    row.Values.MobileNumber, "This mobile number is already assigned to another NSG number.", owner));
        }
    }

    private static PisRowIssue? OwnershipIssue(UserPis stored, long adminId, string? nsgNumber)
    {
        if (stored.AssignedTo == adminId) return null;

        return stored.AssignedTo == null
            ? new PisRowIssue(PisRowStatus.Failed, PisImportConflictTypes.RecordUnassigned, "nsgNumber", nsgNumber,
                "This personnel record is not assigned to any PIS Admin yet, so it cannot be updated by an upload. Ask the Super Admin to assign it.")
            : new PisRowIssue(PisRowStatus.Failed, PisImportConflictTypes.AssignedToAnotherAdmin, "nsgNumber", nsgNumber,
                "This personnel record is assigned to another PIS Admin, so it cannot be updated by your upload.");
    }

    private static readonly Dictionary<string, System.Reflection.PropertyInfo> EntityProperties =
        typeof(UserPis).GetProperties().ToDictionary(p => p.Name);
    private static readonly Dictionary<string, System.Reflection.PropertyInfo> SensitiveProperties =
        typeof(NSG.Mobile.Application.Pis.DTOs.PisSensitiveIdentifiersInput).GetProperties().ToDictionary(p => p.Name);

    /// <summary>
    /// Mandatory-field rule (35 of the 42 fields). A blank mandatory cell is rejected for a new record; for an existing record it is
    /// acceptable only when the stored record already has a value, which the update then preserves.
    /// </summary>
    private static void AddMandatoryIssues(PisParsedRow row, UserPis? current)
    {
        HashSet<string> reported = new(row.Issues.Select(i => i.Field ?? string.Empty));
        foreach (PisImportField field in PisImportFields.All)
        {
            if (!PisImportFields.IsMandatory(field) || field.Name == nameof(UserPis.NsgNumber) || reported.Contains(field.ReportName)) continue;
            if (!IsBlank(row, field)) continue;
            if (current != null && StoredHasValue(current, field)) continue;

            string message = current == null
                ? $"{field.Heading} is required."
                : $"{field.Heading} is blank in the file and the existing record has no value for it.";
            row.Issues.Add(new PisRowIssue(PisRowStatus.Failed, PisImportConflictTypes.RequiredFieldMissing, field.ReportName, null, message));
        }
    }

    private static bool IsBlank(PisParsedRow row, PisImportField field)
        => field.Kind == PisFieldKind.Sensitive
            ? string.IsNullOrWhiteSpace((string?)SensitiveProperties[field.Name].GetValue(row.Sensitive))
            : IsEmpty(EntityProperties[field.Name].GetValue(row.Values));

    private static bool StoredHasValue(UserPis stored, PisImportField field)
        => !IsEmpty(EntityProperties[field.Kind == PisFieldKind.Sensitive ? field.Name + "Encrypted" : field.Name].GetValue(stored));

    private static bool IsEmpty(object? value) => value == null || (value is string s && string.IsNullOrWhiteSpace(s));

    // ── Saving ──

    private async Task SaveRowAsync(PisParsedRow row, PisUploadType type, IReadOnlyDictionary<string, UserPis> existing,
        PisUploadBatch batch, AdminActor actor, CancellationToken cancellationToken)
    {
        bool exists = existing.TryGetValue(row.NsgNumber!, out UserPis? current);
        bool insert = !exists && type == PisUploadType.Upsert;
        if (!exists && !insert)
        {
            row.Issues.Add(new PisRowIssue(PisRowStatus.Failed, PisImportConflictTypes.NsgNumberNotFound, "nsgNumber", row.NsgNumber,
                "Personnel record does not exist. Update-only upload cannot insert new records."));
            return;
        }

        DateTime now = IndianStandardTime.Now;
        UserPis values = row.Values;
        values.AadhaarNumberEncrypted = Encrypt(row.Sensitive.AadhaarNumber);
        values.PanNumberEncrypted = Encrypt(PisNormalizer.Pan(row.Sensitive.PanNumber));
        values.PranNumberEncrypted = Encrypt(row.Sensitive.PranNumber);
        values.SalaryAccountNumberEncrypted = Encrypt(PisNormalizer.SalaryAccountNumber(row.Sensitive.SalaryAccountNumber));

        // Ownership is set by the server only: the new record belongs to the authenticated admin; an update never carries a value for it.
        values.AssignedTo = insert ? actor.Id : null;

        if (insert)
        {
            values.IsActive = true;
            values.MobileAppActivationStatus = MobileAppActivationStatus.Pending;
            values.CreatedAtIst = now;
            values.CreatedBy = actor.Key;   // the uploader; there is no fallback: an id that does not fit never gets here
            values.UpdatedAtIst = null;
            values.UpdatedBy = null;
        }
        else
        {
            values.Id = current!.Id;
            values.UpdatedAtIst = now;
            values.UpdatedBy = actor.Key;   // CreatedBy and AssignedTo are protected properties: an update never writes them
        }

        PisRowSaveResult result = await _repository.SaveRowAsync(batch.Id, row.RowNumber, values, insert, actor.Id, now, cancellationToken);
        switch (result.Status)
        {
            case PisRowSaveStatus.Saved:
                if (insert) batch.InsertedCount++;
                else batch.UpdatedCount++;
                break;
            case PisRowSaveStatus.DuplicateNsgNumber:
                row.Issues.Add(new PisRowIssue(PisRowStatus.Conflict, PisImportConflictTypes.DuplicateNsgNumber, "nsgNumber", row.NsgNumber,
                    "A personnel record with this NSG number was created by another request while this file was processed."));
                break;
            case PisRowSaveStatus.DuplicateMobileNumber:
                row.Issues.Add(new PisRowIssue(PisRowStatus.Conflict, PisImportConflictTypes.DuplicateMobileNumber, "mobileNumber",
                    row.Values.MobileNumber, "This mobile number is already assigned to another NSG number.", result.ConflictingNsgNumber));
                break;
            case PisRowSaveStatus.NotOwner:
                // The record was reassigned or unassigned between the pre-check and the row's transaction: nothing was changed.
                row.Issues.Add(new PisRowIssue(PisRowStatus.Failed, PisImportConflictTypes.AssignedToAnotherAdmin, "nsgNumber", row.NsgNumber,
                    "This personnel record is not assigned to you, so it cannot be updated by your upload. Nothing was changed for this row."));
                break;
            case PisRowSaveStatus.NotFound:
                row.Issues.Add(new PisRowIssue(PisRowStatus.Failed, PisImportConflictTypes.NsgNumberNotFound, "nsgNumber", row.NsgNumber,
                    "Personnel record no longer exists."));
                break;
            default:
                row.Issues.Add(new PisRowIssue(PisRowStatus.Failed, PisImportConflictTypes.SaveFailed, null, null,
                    "The row could not be saved. Nothing was changed for this row."));
                break;
        }
    }

    private string? Encrypt(string? plain) => string.IsNullOrWhiteSpace(plain) ? null : _encryption.Encrypt(plain.Trim());

    /// <summary>A rejected row counts once: as a conflict if any of its problems is a conflict, otherwise as failed.</summary>
    private static List<PisUploadBatchError> CountRejected(PisParsedRow row, PisUploadBatch batch)
    {
        bool conflict = row.Issues.Any(i => i.Status == PisRowStatus.Conflict);
        if (conflict) batch.ConflictCount++;
        else batch.FailedCount++;

        List<PisUploadBatchError> errors = new();
        foreach (PisRowIssue issue in row.Issues)
        {
            errors.Add(new PisUploadBatchError
            {
                BatchId = batch.Id,
                RowNumber = row.RowNumber,
                NsgNumber = row.NsgNumber == null ? null : Truncate(row.NsgNumber, 30),
                Status = conflict ? PisRowStatus.Conflict : PisRowStatus.Failed,
                ConflictType = issue.ConflictType,
                Field = issue.Field,
                Value = issue.Value == null ? null : Truncate(issue.Value, 200),
                Message = Truncate(issue.Message, 500),
                ConflictingNsgNumber = issue.ConflictingNsgNumber == null ? null : Truncate(issue.ConflictingNsgNumber, 30),
                CreatedAtIst = IndianStandardTime.Now
            });
        }

        return errors;
    }

    /// <summary>Persists a rejected row's errors in their own transaction. False (and an error log with no row data) when that fails.</summary>
    private async Task<bool> TrySaveErrorsAsync(long batchId, int rowNumber, IReadOnlyList<PisUploadBatchError> rowErrors)
    {
        try
        {
            await _repository.SaveErrorsAsync(rowErrors, CancellationToken.None);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError("PIS upload batch {BatchId}: the error details of worksheet row {RowNumber} could not be saved ({ExceptionType}).",
                batchId, rowNumber, ex.GetType().Name);
            return false;
        }
    }

    private async Task TryFailBatchAsync(PisUploadBatch batch, List<PisUploadBatchError> errors, string message)
    {
        try
        {
            batch.Status = PisUploadStatus.Failed;
            batch.FailureMessage = message;
            batch.CompletedAtIst = IndianStandardTime.Now;
            await _repository.CompleteBatchAsync(batch, CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogError("PIS upload batch {BatchId} could not be marked failed ({ExceptionType}).", batch.Id, ex.GetType().Name);
        }
    }

    // ── History ──

    public async Task<Result<PisPageDto<PisUploadBatchSummaryDto>>> GetBatchesAsync(int pageNumber, int pageSize, CancellationToken cancellationToken)
    {
        if (!_currentUser.TryGetAdminActor(out AdminActor actor))
            return Result.Failure<PisPageDto<PisUploadBatchSummaryDto>>(ApiErrorCodes.Unauthorized, AdminActorExtensions.AdminNotIdentifiedMessage);

        (IReadOnlyList<PisUploadBatch> items, int total) = await _repository.GetBatchesAsync(actor.Id, pageNumber, pageSize, cancellationToken);
        return Result.Success(new PisPageDto<PisUploadBatchSummaryDto>
        {
            Items = items.Select(ToSummary).ToList(),
            Pagination = PisPaginationDto.Of(pageNumber, pageSize, total)
        });
    }

    public async Task<Result<PisUploadBatchDetailDto>> GetBatchAsync(long batchId, int pageNumber, int pageSize, CancellationToken cancellationToken)
    {
        if (!_currentUser.TryGetAdminActor(out AdminActor actor))
            return Result.Failure<PisUploadBatchDetailDto>(ApiErrorCodes.Unauthorized, AdminActorExtensions.AdminNotIdentifiedMessage);

        // Another admin's batch is indistinguishable from a missing one.
        PisUploadBatch? batch = await _repository.GetBatchAsync(actor.Id, batchId, cancellationToken);
        if (batch == null)
            return Result.Failure<PisUploadBatchDetailDto>(ApiErrorCodes.NotFound, "Upload batch not found.");

        (IReadOnlyList<PisUploadBatchRecord> records, int recordTotal) = await _repository.GetBatchRecordsAsync(batchId, pageNumber, pageSize, cancellationToken);
        (IReadOnlyList<PisUploadBatchError> errors, int errorTotal) = await _repository.GetBatchErrorsAsync(batchId, pageNumber, pageSize, cancellationToken);

        return Result.Success(new PisUploadBatchDetailDto
        {
            Batch = ToSummary(batch),
            Records = new PisPageDto<PisUploadBatchRecordDto>
            {
                Items = records.Select(r => new PisUploadBatchRecordDto
                {
                    RowNumber = r.RowNumber,
                    PisRecordId = r.PisRecordId,
                    NsgNumber = r.NsgNumber,
                    Operation = r.Operation == PisRowOperation.Inserted ? "INSERTED" : "UPDATED"
                }).ToList(),
                Pagination = PisPaginationDto.Of(pageNumber, pageSize, recordTotal)
            },
            Errors = new PisPageDto<PisUploadErrorDto>
            {
                Items = errors.Select(ToErrorDto).ToList(),
                Pagination = PisPaginationDto.Of(pageNumber, pageSize, errorTotal)
            }
        });
    }

    // ── Mapping ──

    private static string TypeName(PisUploadType type) => type == PisUploadType.Upsert ? "UPSERT" : "UPDATE_ONLY";

    private static PisUploadErrorDto ToErrorDto(PisUploadBatchError e) => new()
    {
        RowNumber = e.RowNumber,
        NsgNumber = e.NsgNumber,
        Status = e.Status == PisRowStatus.Conflict ? "conflict" : "failed",
        ConflictType = e.ConflictType,
        Field = e.Field,
        Value = e.Value,
        Message = e.Message,
        ConflictingNsgNumber = e.ConflictingNsgNumber
    };

    private static PisUploadResultDto ToResult(PisUploadBatch b, List<PisUploadBatchError> errors) => new()
    {
        BatchId = b.Id,
        UploadType = TypeName(b.UploadType),
        FileName = b.FileName,
        Status = b.Status.ToString(),
        TotalRows = b.TotalRows,
        Inserted = b.InsertedCount,
        Updated = b.UpdatedCount,
        Failed = b.FailedCount,
        Conflicts = b.ConflictCount,
        Errors = errors.Select(ToErrorDto).ToList()
    };

    private static PisUploadBatchSummaryDto ToSummary(PisUploadBatch b) => new()
    {
        BatchId = b.Id,
        FileName = b.FileName,
        UploadType = TypeName(b.UploadType),
        Status = b.Status.ToString(),
        TotalRows = b.TotalRows,
        Inserted = b.InsertedCount,
        Updated = b.UpdatedCount,
        Failed = b.FailedCount,
        Conflicts = b.ConflictCount,
        FailureMessage = b.FailureMessage,
        UploadedAt = IstTime.FromIst(b.CreatedAtIst),
        CompletedAt = b.CompletedAtIst.HasValue ? IstTime.FromIst(b.CompletedAtIst.Value) : null
    };

    private async Task AuditAsync(PisUploadBatch batch, string outcome, AdminActor actor)
    {
        // Counts and ids only: never row data. The audit user is the uploader's Admin Profile ID.
        await _audit.LogAsync(AuditAction.AdminAction, DomainConstants.AuditResults.Success,
            userId: actor.Key, entityType: "PisUploadBatch", entityId: batch.Id.ToString(), correlationId: _currentUser.CorrelationId,
            additionalData: $"{{\"operation\":\"PisExcelUpload\",\"type\":\"{TypeName(batch.UploadType)}\",\"outcome\":\"{outcome}\",\"adminProfileId\":{batch.UploadedBy},\"total\":{batch.TotalRows},\"inserted\":{batch.InsertedCount},\"updated\":{batch.UpdatedCount},\"failed\":{batch.FailedCount},\"conflicts\":{batch.ConflictCount}}}",
            cancellationToken: CancellationToken.None);
        _logger.LogInformation("PIS upload batch {BatchId} by admin {AdminId}: {Outcome} (total {Total}, inserted {Inserted}, updated {Updated}, failed {Failed}, conflicts {Conflicts})",
            batch.Id, batch.UploadedBy, outcome, batch.TotalRows, batch.InsertedCount, batch.UpdatedCount, batch.FailedCount, batch.ConflictCount);
    }

    private static string SafeFileName(string? name)
    {
        string leaf = Path.GetFileName((name ?? string.Empty).Replace('\\', '/'));
        leaf = new string(leaf.Where(c => !char.IsControl(c)).ToArray()).Trim();
        return Truncate(string.IsNullOrEmpty(leaf) ? "upload.xlsx" : leaf, 255);
    }

    private static string Truncate(string value, int length) => value.Length <= length ? value : value[..length];

    private static async Task<byte[]?> ReadBoundedAsync(Stream stream, long max, CancellationToken cancellationToken)
    {
        using MemoryStream buffer = new();
        byte[] chunk = new byte[81920];
        int read;
        while ((read = await stream.ReadAsync(chunk, cancellationToken)) > 0)
        {
            if (buffer.Length + read > max) return null;
            buffer.Write(chunk, 0, read);
        }

        return buffer.ToArray();
    }
}
