namespace NSG.Mobile.Application.Pis.Import;

/// <summary>Hard limits of one upload.</summary>
public static class PisImportLimits
{
    public const long MaxFileBytes = 10L * 1024 * 1024;
    public const int MaxRows = 10_000;
}

/// <summary>An uploaded workbook handed over by the API layer (no ASP.NET types in the Application layer).</summary>
public sealed record PisUploadFile(Stream Content, string FileName, long Length);

/// <summary>One row-level rejection. <c>Status</c> is "failed" or "conflict".</summary>
public sealed class PisUploadErrorDto
{
    public int RowNumber { get; set; }
    public string? NsgNumber { get; set; }
    public string Status { get; set; } = string.Empty;
    public string ConflictType { get; set; } = string.Empty;
    public string? Field { get; set; }
    public string? Value { get; set; }
    public string Message { get; set; } = string.Empty;
    public string? ConflictingNsgNumber { get; set; }
}

/// <summary>
/// Summary of a processed upload. Every non-blank row is counted once: <c>TotalRows = Inserted + Updated + Failed + Conflicts</c>.
/// <c>Conflicts</c> are rows rejected for a duplicate NSG/mobile number; <c>Failed</c> are all other rejected rows. A row with several
/// problems appears once in the counts but may have several entries in <c>Errors</c>.
/// </summary>
public sealed class PisUploadResultDto
{
    public long BatchId { get; set; }
    public string UploadType { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public int TotalRows { get; set; }
    public int Inserted { get; set; }
    public int Updated { get; set; }
    public int Failed { get; set; }
    public int Conflicts { get; set; }
    public List<PisUploadErrorDto> Errors { get; set; } = new();

    /// <summary>Worksheet rows whose error details could NOT be saved in the batch history (they appear in <c>Errors</c> only). Normally empty.</summary>
    public List<int> ErrorTrackingFailedRows { get; set; } = new();
}

public sealed class PisUploadBatchSummaryDto
{
    public long BatchId { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string UploadType { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public int TotalRows { get; set; }
    public int Inserted { get; set; }
    public int Updated { get; set; }
    public int Failed { get; set; }
    public int Conflicts { get; set; }
    public string? FailureMessage { get; set; }
    public DateTimeOffset UploadedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
}

public sealed class PisUploadBatchRecordDto
{
    public int RowNumber { get; set; }
    public long PisRecordId { get; set; }
    public string NsgNumber { get; set; } = string.Empty;
    public string Operation { get; set; } = string.Empty;
}

public sealed class PisPaginationDto
{
    public int PageNumber { get; set; }
    public int PageSize { get; set; }
    public int TotalRecords { get; set; }
    public int TotalPages { get; set; }
    public bool HasPreviousPage { get; set; }
    public bool HasNextPage { get; set; }

    public static PisPaginationDto Of(int pageNumber, int pageSize, int total) => new()
    {
        PageNumber = pageNumber,
        PageSize = pageSize,
        TotalRecords = total,
        TotalPages = (int)Math.Ceiling(total / (double)pageSize),
        HasPreviousPage = pageNumber > 1,
        HasNextPage = (long)pageNumber * pageSize < total
    };
}

public sealed class PisPageDto<T>
{
    public IReadOnlyList<T> Items { get; set; } = Array.Empty<T>();
    public PisPaginationDto Pagination { get; set; } = new();
}

/// <summary>A batch with its summary and, paged, the records it inserted/updated and its row-level errors.</summary>
public sealed class PisUploadBatchDetailDto
{
    public PisUploadBatchSummaryDto Batch { get; set; } = new();
    public PisPageDto<PisUploadBatchRecordDto> Records { get; set; } = new();
    public PisPageDto<PisUploadErrorDto> Errors { get; set; } = new();
}
