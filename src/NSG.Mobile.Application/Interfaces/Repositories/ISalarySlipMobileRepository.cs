using NSG.Mobile.Domain.Entities;

namespace NSG.Mobile.Application.Interfaces.Repositories;

/// <summary>A stored salary slip reduced to what is needed to decide ownership and list it. The storage path is deliberately absent.</summary>
public sealed record SalarySlipCandidate(long Id, DateOnly SalaryMonth, string AccountNumberEncrypted, int PageCount, DateTime CreatedAtIst);

/// <summary>
/// Read access to salary slips for the mobile app. The account number is stored as random-IV ciphertext, so the database cannot filter
/// by it: the service decrypts the candidates returned here and keeps only those matching the signed-in person's account.
/// </summary>
public interface ISalarySlipMobileRepository
{
    /// <summary>Every stored slip (optionally of one salary month, the first day of it), newest month first. No personnel filter is possible in SQL.</summary>
    Task<IReadOnlyList<SalarySlipCandidate>> GetCandidatesAsync(DateOnly? salaryMonth, CancellationToken cancellationToken);

    /// <summary>One slip by id (including its storage path, for the download), or null.</summary>
    Task<SalarySlip?> GetByIdAsync(long salarySlipId, CancellationToken cancellationToken);
}
