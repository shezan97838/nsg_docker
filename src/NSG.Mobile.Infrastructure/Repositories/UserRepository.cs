using Microsoft.EntityFrameworkCore;
using NSG.Mobile.Application.Interfaces.Repositories;
using NSG.Mobile.Domain.Entities;
using NSG.Mobile.Domain.Enums;
using NSG.Mobile.Infrastructure.Persistence;

namespace NSG.Mobile.Infrastructure.Repositories;

public class UserRepository : IUserRepository
{
    private readonly AppDbContext _context;

    public UserRepository(AppDbContext context) => _context = context;

    public async Task<User?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
        => await _context.Users.FindAsync(new object[] { id }, cancellationToken);

    public async Task<User?> GetByMobileNumberAsync(string normalizedMobileNumber, CancellationToken cancellationToken = default)
        => await _context.Users.FirstOrDefaultAsync(u => u.MobileNumber == normalizedMobileNumber, cancellationToken);

    public async Task<User?> GetByForceNumberAsync(string forceNumber, CancellationToken cancellationToken = default)
        => await _context.Users.FirstOrDefaultAsync(u => u.ForceNumber == forceNumber, cancellationToken);

    public async Task<bool> ExistsByMobileNumberAsync(string normalizedMobileNumber, CancellationToken cancellationToken = default)
        => await _context.Users.AnyAsync(u => u.MobileNumber == normalizedMobileNumber, cancellationToken);

    public async Task<bool> ExistsByForceNumberAsync(string forceNumber, CancellationToken cancellationToken = default)
        => await _context.Users.AnyAsync(u => u.ForceNumber == forceNumber, cancellationToken);

    public async Task<IReadOnlyList<User>> GetByStatusAsync(UserStatus status, int pageNumber, int pageSize, CancellationToken cancellationToken = default)
        => await _context.Users
            .AsNoTracking()
            .Where(u => u.Status == status)
            .OrderBy(u => u.CreatedAt)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

    public async Task<int> CountByStatusAsync(UserStatus status, CancellationToken cancellationToken = default)
        => await _context.Users.CountAsync(u => u.Status == status, cancellationToken);

    public async Task AddAsync(User user, CancellationToken cancellationToken = default)
    {
        await _context.Users.AddAsync(user, cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateAsync(User user, CancellationToken cancellationToken = default)
    {
        _context.Users.Update(user);
        await _context.SaveChangesAsync(cancellationToken);
    }
}
