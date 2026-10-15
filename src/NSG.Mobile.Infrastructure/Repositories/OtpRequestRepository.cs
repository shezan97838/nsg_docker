using Microsoft.EntityFrameworkCore;
using NSG.Mobile.Application.Interfaces.Repositories;
using NSG.Mobile.Domain.Entities;
using NSG.Mobile.Domain.Enums;
using NSG.Mobile.Infrastructure.Persistence;

namespace NSG.Mobile.Infrastructure.Repositories;

public class OtpRequestRepository : IOtpRequestRepository
{
    private readonly AppDbContext _context;

    public OtpRequestRepository(AppDbContext context) => _context = context;

    public async Task<OtpRequest?> GetByReferenceIdAsync(Guid referenceId, CancellationToken cancellationToken = default)
        => await _context.OtpRequests
            .FirstOrDefaultAsync(o => o.ReferenceId == referenceId, cancellationToken);

    public async Task<OtpRequest?> GetLatestByMobileAndPurposeAsync(string normalizedMobileNumber, OtpPurpose purpose, CancellationToken cancellationToken = default)
        => await _context.OtpRequests
            .Where(o => o.MobileNumber == normalizedMobileNumber && o.Purpose == purpose)
            .OrderByDescending(o => o.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<int> CountSinceAsync(string normalizedMobileNumber, OtpPurpose purpose, DateTime sinceIst, CancellationToken cancellationToken = default)
        => await _context.OtpRequests.CountAsync(o => o.MobileNumber == normalizedMobileNumber && o.Purpose == purpose && o.CreatedAt >= sinceIst, cancellationToken);

    public async Task AddAsync(OtpRequest otpRequest, CancellationToken cancellationToken = default)
    {
        await _context.OtpRequests.AddAsync(otpRequest, cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateAsync(OtpRequest otpRequest, CancellationToken cancellationToken = default)
    {
        _context.OtpRequests.Update(otpRequest);
        await _context.SaveChangesAsync(cancellationToken);
    }
}
