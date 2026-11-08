using NSG.Mobile.Domain.Entities;

namespace NSG.Mobile.Application.Interfaces.Repositories;

/// <summary>A personnel record's id and its encrypted salary bank account (<c>users_pis.salary_account_number_encrypted</c>).</summary>
public sealed record PisAccountReference(long PisId, string AccountNumberEncrypted);

/// <summary>Data access of the salary slip upload.</summary>
public interface ISalarySlipRepository
{
    /// <summary>
    /// Takes the exclusive upload lock of <paramref name="salaryMonth"/> (MySQL <c>GET_LOCK</c>) on a dedicated database connection and
    /// returns the scope that owns it, or null when the lock was not obtained within <paramref name="timeout"/>.
    /// </summary>
    Task<ISalarySlipMonthScope?> TryAcquireMonthScopeAsync(DateOnly salaryMonth, TimeSpan timeout, CancellationToken cancellationToken);
}

/// <summary>
/// One salary month locked for upload. Every operation here runs on the SAME open connection that holds the lock, so the duplicate check
/// and the save are covered by it. Disposing releases the lock (also when the upload failed or was cancelled) and closes the connection.
/// </summary>
public interface ISalarySlipMonthScope : IAsyncDisposable
{
    /// <summary>Persists a new batch (status Processing) and returns its id.</summary>
    Task<long> CreateBatchAsync(SalarySlipUploadBatch batch, CancellationToken cancellationToken);

    /// <summary>The encrypted account numbers of the slips that already exist for this month, and nothing else.</summary>
    Task<IReadOnlyList<string>> GetExistingAccountNumbersAsync(CancellationToken cancellationToken);

    /// <summary>Every personnel record that has a salary account number (id and ciphertext), used to fill <c>user_pis_id</c>.</summary>
    Task<IReadOnlyList<PisAccountReference>> GetPisAccountsAsync(CancellationToken cancellationToken);

    /// <summary>Inserts all slips and writes the final state of the batch in ONE transaction: all or nothing.</summary>
    Task SaveSlipsAndCompleteBatchAsync(SalarySlipUploadBatch batch, IReadOnlyList<SalarySlip> slips, CancellationToken cancellationToken);

    /// <summary>Writes the final state of a batch that produced no slips (failed or cancelled).</summary>
    Task UpdateBatchAsync(SalarySlipUploadBatch batch, CancellationToken cancellationToken);

    /// <summary>How many slips the batch owns; used to learn whether an interrupted save was committed.</summary>
    Task<int> CountSlipsOfBatchAsync(long batchId, CancellationToken cancellationToken);
}
