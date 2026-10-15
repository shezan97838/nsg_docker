using Microsoft.EntityFrameworkCore;
using NSG.Mobile.Application.Interfaces.Repositories;
using NSG.Mobile.Domain.Common;
using NSG.Mobile.Domain.Entities;
using NSG.Mobile.Infrastructure.Persistence;

namespace NSG.Mobile.Infrastructure.Repositories;

public class RefreshTokenRepository : IRefreshTokenRepository
{
    private readonly AppDbContext _context;

    public RefreshTokenRepository(AppDbContext context) => _context = context;

    public async Task<RefreshToken?> GetByTokenHashAsync(string tokenHash, CancellationToken cancellationToken = default)
        => await _context.RefreshTokens
            .Include(rt => rt.User)
            .Include(rt => rt.Device)
            .FirstOrDefaultAsync(rt => rt.TokenHash == tokenHash, cancellationToken);

    public async Task<IReadOnlyList<RefreshToken>> GetByFamilyIdAsync(Guid familyId, CancellationToken cancellationToken = default)
        => await _context.RefreshTokens
            .Where(rt => rt.FamilyId == familyId)
            .ToListAsync(cancellationToken);

    public async Task AddAsync(RefreshToken token, CancellationToken cancellationToken = default)
    {
        await _context.RefreshTokens.AddAsync(token, cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateAsync(RefreshToken token, CancellationToken cancellationToken = default)
    {
        _context.RefreshTokens.Update(token);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task RevokeAllByUserIdAsync(int userId, string reason, string? ipAddress, CancellationToken cancellationToken = default)
    {
        var activeTokens = await _context.RefreshTokens
            .Where(rt => rt.UserId == userId && rt.RevokedAt == null)
            .ToListAsync(cancellationToken);

        foreach (var token in activeTokens)
        {
            token.RevokedAt = IndianStandardTime.Now;
            token.RevokedByIp = ipAddress;
            token.RevokeReason = reason;
        }

        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task RevokeAllByDeviceIdAsync(int deviceId, string reason, string? ipAddress, CancellationToken cancellationToken = default)
    {
        var activeTokens = await _context.RefreshTokens
            .Where(rt => rt.DeviceId == deviceId && rt.RevokedAt == null)
            .ToListAsync(cancellationToken);

        foreach (var token in activeTokens)
        {
            token.RevokedAt = IndianStandardTime.Now;
            token.RevokedByIp = ipAddress;
            token.RevokeReason = reason;
        }

        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task RevokeFamilyAsync(Guid familyId, string reason, string? ipAddress, CancellationToken cancellationToken = default)
    {
        var familyTokens = await _context.RefreshTokens
            .Where(rt => rt.FamilyId == familyId && rt.RevokedAt == null)
            .ToListAsync(cancellationToken);

        foreach (var token in familyTokens)
        {
            token.RevokedAt = IndianStandardTime.Now;
            token.RevokedByIp = ipAddress;
            token.RevokeReason = reason;
        }

        await _context.SaveChangesAsync(cancellationToken);
    }
}
