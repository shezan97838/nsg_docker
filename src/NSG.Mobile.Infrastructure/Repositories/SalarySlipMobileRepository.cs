using Microsoft.EntityFrameworkCore;
using NSG.Mobile.Application.Interfaces.Repositories;
using NSG.Mobile.Domain.Entities;
using NSG.Mobile.Infrastructure.Persistence;

namespace NSG.Mobile.Infrastructure.Repositories;

public class SalarySlipMobileRepository : ISalarySlipMobileRepository
{
    private readonly AppDbContext _context;

    public SalarySlipMobileRepository(AppDbContext context)
    {
        _context = context;
    }

    public async Task<IReadOnlyList<SalarySlipCandidate>> GetCandidatesAsync(DateOnly? salaryMonth, CancellationToken cancellationToken)
    {
        IQueryable<SalarySlip> query = _context.SalarySlips.AsNoTracking();
        if (salaryMonth != null) query = query.Where(s => s.SalaryMonth == salaryMonth.Value);

        // Only the columns needed to decide ownership; the storage path is not read here.
        return await query.OrderByDescending(s => s.SalaryMonth).ThenByDescending(s => s.Id)
            .Select(s => new SalarySlipCandidate(s.Id, s.SalaryMonth, s.AccountNumberEncrypted, s.PageCount, s.CreatedAtIst))
            .ToListAsync(cancellationToken);
    }

    public Task<SalarySlip?> GetByIdAsync(long salarySlipId, CancellationToken cancellationToken)
        => _context.SalarySlips.AsNoTracking().FirstOrDefaultAsync(s => s.Id == salarySlipId, cancellationToken);
}
