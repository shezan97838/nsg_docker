using NSG.Mobile.Domain.Entities;

namespace NSG.Mobile.Application.Interfaces.Repositories;

/// <summary>A batch with the number of salary slips it produced.</summary>
public sealed record SalarySlipBatchRow(SalarySlipUploadBatch Batch, int SlipCount);

/// <summary>Read access to salary slip batches and slips for the Admin Portal. Every query is filtered by the uploading admin.</summary>
public interface ISalarySlipQueryRepository
{
    /// <summary>The admin's own batches, newest first, optionally limited to one salary month (the first day of it).</summary>
    Task<(IReadOnlyList<SalarySlipBatchRow> Items, int TotalCount)> GetBatchesAsync(long adminId, DateOnly? salaryMonth, int pageNumber, int pageSize, CancellationToken cancellationToken);

    /// <summary>The batch only when it was uploaded by <paramref name="adminId"/>; otherwise null.</summary>
    Task<SalarySlipBatchRow?> GetBatchAsync(long adminId, long batchId, CancellationToken cancellationToken);

    /// <summary>The slips of a batch (the caller has already proven ownership of the batch), in id order.</summary>
    Task<(IReadOnlyList<SalarySlip> Items, int TotalCount)> GetSlipsOfBatchAsync(long batchId, int pageNumber, int pageSize, CancellationToken cancellationToken);

    /// <summary>The slip only when its batch was uploaded by <paramref name="adminId"/>; otherwise null.</summary>
    Task<SalarySlip?> GetSlipOfAdminAsync(long adminId, long slipId, CancellationToken cancellationToken);
}
