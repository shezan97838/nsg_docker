using Microsoft.EntityFrameworkCore;
using NSG.Mobile.Application.Interfaces.Repositories;
using NSG.Mobile.Domain.Entities;
using NSG.Mobile.Infrastructure.Persistence;

namespace NSG.Mobile.Infrastructure.Repositories;

public class SalarySlipQueryRepository : ISalarySlipQueryRepository
{
    private readonly AppDbContext _context;

    public SalarySlipQueryRepository(AppDbContext context)
    {
        _context = context;
    }

    public async Task<(IReadOnlyList<SalarySlipBatchRow> Items, int TotalCount)> GetBatchesAsync(
        long adminId, DateOnly? salaryMonth, int pageNumber, int pageSize, CancellationToken cancellationToken)
    {
        IQueryable<SalarySlipUploadBatch> query = _context.SalarySlipUploadBatches.AsNoTracking().Where(b => b.UploadedBy == adminId);
        if (salaryMonth != null) query = query.Where(b => b.SalaryMonth == salaryMonth.Value);

        int total = await query.CountAsync(cancellationToken);
        var page = await query.OrderByDescending(b => b.CreatedAtIst).ThenByDescending(b => b.Id)
            .Skip((pageNumber - 1) * pageSize).Take(pageSize)
            .Select(b => new { Batch = b, SlipCount = _context.SalarySlips.Count(s => s.OriginalBatchId == b.Id) })
            .ToListAsync(cancellationToken);

        return (page.Select(p => new SalarySlipBatchRow(p.Batch, p.SlipCount)).ToList(), total);
    }

    public async Task<SalarySlipBatchRow?> GetBatchAsync(long adminId, long batchId, CancellationToken cancellationToken)
    {
        var row = await _context.SalarySlipUploadBatches.AsNoTracking().Where(b => b.Id == batchId && b.UploadedBy == adminId)
            .Select(b => new { Batch = b, SlipCount = _context.SalarySlips.Count(s => s.OriginalBatchId == b.Id) })
            .FirstOrDefaultAsync(cancellationToken);
        return row == null ? null : new SalarySlipBatchRow(row.Batch, row.SlipCount);
    }

    public async Task<(IReadOnlyList<SalarySlip> Items, int TotalCount)> GetSlipsOfBatchAsync(long batchId, int pageNumber, int pageSize, CancellationToken cancellationToken)
    {
        IQueryable<SalarySlip> query = _context.SalarySlips.AsNoTracking().Where(s => s.OriginalBatchId == batchId);
        int total = await query.CountAsync(cancellationToken);
        List<SalarySlip> items = await query.OrderBy(s => s.Id).Skip((pageNumber - 1) * pageSize).Take(pageSize).ToListAsync(cancellationToken);
        return (items, total);
    }

    public Task<SalarySlip?> GetSlipOfAdminAsync(long adminId, long slipId, CancellationToken cancellationToken)
        => _context.SalarySlips.AsNoTracking()
            .Where(s => s.Id == slipId && _context.SalarySlipUploadBatches.Any(b => b.Id == s.OriginalBatchId && b.UploadedBy == adminId))
            .FirstOrDefaultAsync(cancellationToken);
}
