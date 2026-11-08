using Microsoft.Extensions.Logging;
using NSG.Mobile.Application.Common;
using NSG.Mobile.Application.Interfaces.Infrastructure;
using NSG.Mobile.Application.Interfaces.Repositories;
using NSG.Mobile.Application.Interfaces.Services;
using NSG.Mobile.Domain.Constants;
using NSG.Mobile.Domain.Entities;
using NSG.Mobile.Domain.Enums;

namespace NSG.Mobile.Application.SalarySlips;

/// <summary>One upload batch in a list.</summary>
public sealed class SalarySlipBatchSummaryDto
{
    public long BatchId { get; set; }
    public string OriginalFileName { get; set; } = string.Empty;

    /// <summary>First day of the salary month (yyyy-MM-dd).</summary>
    public DateOnly SalaryMonth { get; set; }

    public string Status { get; set; } = string.Empty;
    public int TotalPages { get; set; }
    public int ProcessedPages { get; set; }
    public int UnmatchedPages { get; set; }
    public int SalarySlipCount { get; set; }
    public string? FailureMessage { get; set; }
    public DateTimeOffset UploadedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>A batch with its unmatched page numbers and, paged, the salary slips it produced.</summary>
public sealed class SalarySlipBatchDetailDto
{
    public SalarySlipBatchSummaryDto Batch { get; set; } = new();

    /// <summary>1-based page numbers of the PDF that were not turned into a salary slip, ascending.</summary>
    public IReadOnlyList<int> UnmatchedPageNumbers { get; set; } = Array.Empty<int>();

    public PaginatedResponse<SalarySlipItemDto> SalarySlips { get; set; } = new(Array.Empty<SalarySlipItemDto>(), 0, 1, 1);
}

/// <summary>One generated salary slip. The account number is masked to its last four digits; the storage path is never exposed.</summary>
public sealed class SalarySlipItemDto
{
    public long SalarySlipId { get; set; }
    public DateOnly SalaryMonth { get; set; }
    public string? AccountNumberMasked { get; set; }
    public int PageCount { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>Relative API route that downloads the PDF.</summary>
    public string DownloadUrl { get; set; } = string.Empty;
}

/// <summary>An opened salary slip PDF. The caller owns (and must dispose) <see cref="Content"/>.</summary>
public sealed record SalarySlipDownload(Stream Content, string FileName);

/// <summary>The Admin Portal's view of its own salary slip uploads. Everything is limited to the uploading admin, taken from the token.</summary>
public interface ISalarySlipAdminService
{
    Task<Result<PaginatedResponse<SalarySlipBatchSummaryDto>>> GetBatchesAsync(DateOnly? salaryMonth, int pageNumber, int pageSize, CancellationToken cancellationToken);

    /// <summary>The admin's own batch; 404 for any other batch id (existence of other admins' batches is not revealed).</summary>
    Task<Result<SalarySlipBatchDetailDto>> GetBatchAsync(long batchId, int pageNumber, int pageSize, CancellationToken cancellationToken);

    /// <summary>Opens the PDF of one salary slip after verifying that its batch was uploaded by the calling admin.</summary>
    Task<Result<SalarySlipDownload>> OpenDownloadAsync(long salarySlipId, CancellationToken cancellationToken);
}

public class SalarySlipAdminService : ISalarySlipAdminService
{
    public const int MaxPageSize = 100;
    public const string DownloadRoute = "api/v1/admin/salary-slips/{0}/download";

    private readonly ISalarySlipQueryRepository _repository;
    private readonly ISalarySlipStorageService _storage;
    private readonly IEncryptionService _encryption;
    private readonly IAuditService _audit;
    private readonly ICurrentUserService _currentUser;
    private readonly ILogger<SalarySlipAdminService> _logger;

    public SalarySlipAdminService(ISalarySlipQueryRepository repository, ISalarySlipStorageService storage, IEncryptionService encryption,
        IAuditService audit, ICurrentUserService currentUser, ILogger<SalarySlipAdminService> logger)
    {
        _repository = repository;
        _storage = storage;
        _encryption = encryption;
        _audit = audit;
        _currentUser = currentUser;
        _logger = logger;
    }

    public async Task<Result<PaginatedResponse<SalarySlipBatchSummaryDto>>> GetBatchesAsync(DateOnly? salaryMonth, int pageNumber, int pageSize, CancellationToken cancellationToken)
    {
        if (!_currentUser.TryGetAdminActor(out AdminActor actor))
            return Result.Failure<PaginatedResponse<SalarySlipBatchSummaryDto>>(ApiErrorCodes.Unauthorized, "The administrator could not be identified.");
        if (pageNumber < 1 || pageSize < 1 || pageSize > MaxPageSize)
            return Result.Failure<PaginatedResponse<SalarySlipBatchSummaryDto>>(ApiErrorCodes.ValidationFailed, $"pageNumber must be 1 or greater and pageSize between 1 and {MaxPageSize}.");

        DateOnly? month = salaryMonth == null ? null : new DateOnly(salaryMonth.Value.Year, salaryMonth.Value.Month, 1);
        (IReadOnlyList<SalarySlipBatchRow> items, int total) = await _repository.GetBatchesAsync(actor.Id, month, pageNumber, pageSize, cancellationToken);
        return Result.Success(new PaginatedResponse<SalarySlipBatchSummaryDto>(items.Select(ToSummary).ToList(), total, pageNumber, pageSize));
    }

    public async Task<Result<SalarySlipBatchDetailDto>> GetBatchAsync(long batchId, int pageNumber, int pageSize, CancellationToken cancellationToken)
    {
        if (!_currentUser.TryGetAdminActor(out AdminActor actor))
            return Result.Failure<SalarySlipBatchDetailDto>(ApiErrorCodes.Unauthorized, "The administrator could not be identified.");
        if (pageNumber < 1 || pageSize < 1 || pageSize > MaxPageSize)
            return Result.Failure<SalarySlipBatchDetailDto>(ApiErrorCodes.ValidationFailed, $"pageNumber must be 1 or greater and pageSize between 1 and {MaxPageSize}.");

        SalarySlipBatchRow? row = await _repository.GetBatchAsync(actor.Id, batchId, cancellationToken);
        if (row == null)
            return Result.Failure<SalarySlipBatchDetailDto>(ApiErrorCodes.NotFound, "Salary slip upload batch not found.");

        (IReadOnlyList<SalarySlip> slips, int total) = await _repository.GetSlipsOfBatchAsync(batchId, pageNumber, pageSize, cancellationToken);
        return Result.Success(new SalarySlipBatchDetailDto
        {
            Batch = ToSummary(row),
            UnmatchedPageNumbers = ParsePageNumbers(row.Batch.UnmatchedPageNumbers),
            SalarySlips = new PaginatedResponse<SalarySlipItemDto>(slips.Select(ToItem).ToList(), total, pageNumber, pageSize)
        });
    }

    public async Task<Result<SalarySlipDownload>> OpenDownloadAsync(long salarySlipId, CancellationToken cancellationToken)
    {
        if (!_currentUser.TryGetAdminActor(out AdminActor actor))
            return Result.Failure<SalarySlipDownload>(ApiErrorCodes.Unauthorized, "The administrator could not be identified.");

        // Ownership is part of the query: another admin's slip is indistinguishable from a missing one.
        SalarySlip? slip = await _repository.GetSlipOfAdminAsync(actor.Id, salarySlipId, cancellationToken);
        if (slip == null)
            return Result.Failure<SalarySlipDownload>(ApiErrorCodes.NotFound, "Salary slip not found.");

        Stream? content;
        try
        {
            content = _storage.OpenRead(slip.FilePath);
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            _logger.LogError(ex, "Salary slip {SalarySlipId} could not be opened from storage", slip.Id);
            return Result.Failure<SalarySlipDownload>(ApiErrorCodes.InternalError, "The salary slip file could not be retrieved.");
        }

        if (content == null)
        {
            _logger.LogError("Salary slip {SalarySlipId} (batch {BatchId}) has no file in storage", slip.Id, slip.OriginalBatchId);
            return Result.Failure<SalarySlipDownload>(ApiErrorCodes.NotFound, "The salary slip file is not available.");
        }

        await AuditAsync(slip, actor);
        return Result.Success(new SalarySlipDownload(content, $"SalarySlip_{slip.SalaryMonth:yyyy-MM}_{slip.Id}.pdf"));
    }

    private async Task AuditAsync(SalarySlip slip, AdminActor actor)
    {
        try
        {
            await _audit.LogAsync(AuditAction.AdminAction, DomainConstants.AuditResults.Success,
                userId: actor.Key, entityType: "SalarySlip", entityId: slip.Id.ToString(), correlationId: _currentUser.CorrelationId,
                additionalData: $"{{\"operation\":\"SalarySlipDownload\",\"adminProfileId\":{actor.Id},\"batchId\":{slip.OriginalBatchId},\"salaryMonth\":\"{slip.SalaryMonth:yyyy-MM}\"}}",
                cancellationToken: CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Audit entry for the download of salary slip {SalarySlipId} could not be written", slip.Id);
        }
    }

    private static SalarySlipBatchSummaryDto ToSummary(SalarySlipBatchRow row) => new()
    {
        BatchId = row.Batch.Id,
        OriginalFileName = row.Batch.OriginalFileName,
        SalaryMonth = row.Batch.SalaryMonth,
        Status = row.Batch.Status.ToString(),
        TotalPages = row.Batch.TotalPages,
        ProcessedPages = row.Batch.ProcessedPages,
        UnmatchedPages = row.Batch.UnmatchedPages,
        SalarySlipCount = row.SlipCount,
        FailureMessage = row.Batch.FailureMessage,
        UploadedAt = IstTime.FromIst(row.Batch.CreatedAtIst),
        UpdatedAt = IstTime.FromIst(row.Batch.UpdatedAtIst)
    };

    private SalarySlipItemDto ToItem(SalarySlip slip) => new()
    {
        SalarySlipId = slip.Id,
        SalaryMonth = slip.SalaryMonth,
        AccountNumberMasked = MaskAccount(slip.AccountNumberEncrypted),
        PageCount = slip.PageCount,
        CreatedAt = IstTime.FromIst(slip.CreatedAtIst),
        DownloadUrl = string.Format(System.Globalization.CultureInfo.InvariantCulture, DownloadRoute, slip.Id)
    };

    private string? MaskAccount(string cipherText)
    {
        try
        {
            string plain = _encryption.Decrypt(cipherText);
            return plain.Length <= 4 ? new string('X', plain.Length) : new string('X', plain.Length - 4) + plain[^4..];
        }
        catch (Exception ex) when (ex is FormatException or System.Security.Cryptography.CryptographicException or ArgumentException)
        {
            _logger.LogWarning("A stored account number could not be decrypted for display.");
            return null;
        }
    }

    private static IReadOnlyList<int> ParsePageNumbers(string? stored)
        => string.IsNullOrWhiteSpace(stored)
            ? Array.Empty<int>()
            : stored.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(s => int.TryParse(s, out int n) ? n : 0).Where(n => n > 0).ToList();
}
