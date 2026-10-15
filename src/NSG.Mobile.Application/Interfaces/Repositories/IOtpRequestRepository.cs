using NSG.Mobile.Domain.Entities;
using NSG.Mobile.Domain.Enums;

namespace NSG.Mobile.Application.Interfaces.Repositories;

/// <summary>
/// Repository abstraction for OtpRequest lifecycle management.
/// </summary>
public interface IOtpRequestRepository
{
    Task<OtpRequest?> GetByReferenceIdAsync(Guid referenceId, CancellationToken cancellationToken = default);
    Task<OtpRequest?> GetLatestByMobileAndPurposeAsync(string normalizedMobileNumber, OtpPurpose purpose, CancellationToken cancellationToken = default);
    /// <summary>Number of OTPs created for a mobile number and purpose at or after <paramref name="sinceIst"/> (send-rate limit).</summary>
    Task<int> CountSinceAsync(string normalizedMobileNumber, OtpPurpose purpose, DateTime sinceIst, CancellationToken cancellationToken = default);
    Task AddAsync(OtpRequest otpRequest, CancellationToken cancellationToken = default);
    Task UpdateAsync(OtpRequest otpRequest, CancellationToken cancellationToken = default);
}
