using Microsoft.EntityFrameworkCore;
using NSG.Mobile.Application.Interfaces.Repositories;
using NSG.Mobile.Domain.Entities;
using NSG.Mobile.Infrastructure.Persistence;

namespace NSG.Mobile.Infrastructure.Repositories;

public class Form16Repository : IForm16Repository
{
    private readonly AppDbContext _context;

    public Form16Repository(AppDbContext context)
    {
        _context = context;
    }

    public Task<PersonnelForm16?> GetByKeyForUpdateAsync(string nsgNumber, string assessmentYear, CancellationToken cancellationToken)
        => _context.PersonnelForm16.FirstOrDefaultAsync(f => f.NsgNumber == nsgNumber && f.AssessmentYear == assessmentYear, cancellationToken);

    public Task<PersonnelForm16?> GetByKeyAsync(string nsgNumber, string assessmentYear, CancellationToken cancellationToken)
        => _context.PersonnelForm16.AsNoTracking().FirstOrDefaultAsync(f => f.NsgNumber == nsgNumber && f.AssessmentYear == assessmentYear, cancellationToken);

    public Task<Form16Row?> GetRowByIdAsync(long id, CancellationToken cancellationToken)
        => Rows(_context.PersonnelForm16.AsNoTracking().Where(f => f.Id == id)).FirstOrDefaultAsync(cancellationToken);

    public async Task<(IReadOnlyList<Form16Row> Items, int TotalCount)> GetPagedAsync(string? nsgNumber, string? assessmentYear, int pageNumber, int pageSize, CancellationToken cancellationToken)
    {
        IQueryable<PersonnelForm16> query = _context.PersonnelForm16.AsNoTracking();
        if (!string.IsNullOrEmpty(nsgNumber)) query = query.Where(f => f.NsgNumber == nsgNumber);
        if (!string.IsNullOrEmpty(assessmentYear)) query = query.Where(f => f.AssessmentYear == assessmentYear);

        int total = await query.CountAsync(cancellationToken);
        List<Form16Row> items = await Rows(query.OrderByDescending(f => f.AssessmentYear).ThenBy(f => f.NsgNumber).ThenBy(f => f.Id)
                .Skip((pageNumber - 1) * pageSize).Take(pageSize))
            .ToListAsync(cancellationToken);
        return (items, total);
    }

    public async Task<IReadOnlyList<PersonnelForm16>> ListByNsgNumberAsync(string nsgNumber, CancellationToken cancellationToken)
        => await _context.PersonnelForm16.AsNoTracking().Where(f => f.NsgNumber == nsgNumber)
            .OrderByDescending(f => f.AssessmentYear).ToListAsync(cancellationToken);

    public async Task<Form16InsertOutcome> AddAsync(PersonnelForm16 entity, CancellationToken cancellationToken)
    {
        _context.PersonnelForm16.Add(entity);
        try
        {
            await _context.SaveChangesAsync(cancellationToken);
            return Form16InsertOutcome.Inserted;
        }
        catch (DbUpdateException)
        {
            // The unique index (nsg_number, assessment_year) is the only expected cause; anything else is rethrown.
            _context.Entry(entity).State = EntityState.Detached;
            bool exists = await _context.PersonnelForm16.AsNoTracking()
                .AnyAsync(f => f.NsgNumber == entity.NsgNumber && f.AssessmentYear == entity.AssessmentYear, cancellationToken);
            if (exists) return Form16InsertOutcome.Duplicate;
            throw;
        }
    }

    public async Task UpdateAsync(PersonnelForm16 entity, CancellationToken cancellationToken)
        => await _context.SaveChangesAsync(cancellationToken);

    /// <summary>Left join to the personnel record for the person's name (users_pis has no foreign keys, ADR-020).</summary>
    private IQueryable<Form16Row> Rows(IQueryable<PersonnelForm16> forms)
        => from f in forms
           join p in _context.UsersPis.AsNoTracking() on f.NsgNumber equals p.NsgNumber into people
           from p in people.DefaultIfEmpty()
           select new Form16Row(f, p == null ? null : p.FullName);
}
