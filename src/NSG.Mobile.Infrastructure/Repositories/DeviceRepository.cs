using Microsoft.EntityFrameworkCore;
using NSG.Mobile.Application.Interfaces.Repositories;
using NSG.Mobile.Domain.Common;
using NSG.Mobile.Domain.Entities;
using NSG.Mobile.Domain.Enums;
using NSG.Mobile.Infrastructure.Persistence;

namespace NSG.Mobile.Infrastructure.Repositories;

public class DeviceRepository : IDeviceRepository
{
    private readonly AppDbContext _context;

    public DeviceRepository(AppDbContext context) => _context = context;

    public async Task<Device?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
        => await _context.Devices.FindAsync(new object[] { id }, cancellationToken);

    public async Task<Device?> GetActiveDeviceByUserIdAsync(int userId, CancellationToken cancellationToken = default)
        => await _context.Devices
            .FirstOrDefaultAsync(d => d.UserId == userId && d.Status == DeviceStatus.Active, cancellationToken);

    public async Task<Device?> GetByUserIdAndIdentifierHashAsync(int userId, string deviceIdentifierHash, CancellationToken cancellationToken = default)
        => await _context.Devices
            .FirstOrDefaultAsync(d => d.UserId == userId && d.DeviceIdentifierHash == deviceIdentifierHash, cancellationToken);

    public async Task AddAsync(Device device, CancellationToken cancellationToken = default)
    {
        await _context.Devices.AddAsync(device, cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateAsync(Device device, CancellationToken cancellationToken = default)
    {
        _context.Devices.Update(device);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task RevokeAllUserDevicesAsync(int userId, CancellationToken cancellationToken = default)
    {
        var activeDevices = await _context.Devices
            .Where(d => d.UserId == userId && d.Status == DeviceStatus.Active)
            .ToListAsync(cancellationToken);

        foreach (var device in activeDevices)
        {
            device.Status = DeviceStatus.Revoked;
            device.RevokedAt = IndianStandardTime.Now;
            device.UpdatedAt = IndianStandardTime.Now;
        }

        await _context.SaveChangesAsync(cancellationToken);
    }
}
