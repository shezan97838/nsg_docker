using Microsoft.EntityFrameworkCore;
using NSG.Mobile.Application.Interfaces.Repositories;
using NSG.Mobile.Domain.Constants;
using NSG.Mobile.Domain.Entities;
using NSG.Mobile.Domain.Enums;
using NSG.Mobile.Infrastructure.Persistence;

namespace NSG.Mobile.Infrastructure.Repositories;

public class PisOwnershipRepository : IPisOwnershipRepository
{
    private readonly AppDbContext _context;

    public PisOwnershipRepository(AppDbContext context) => _context = context;

    public async Task<PisOwnershipTransferResult> TransferAsync(
        string nsgNumber, long targetAdminId, long actorAdminId, string? reason, DateTime nowIst, CancellationToken cancellationToken = default)
    {
        var strategy = _context.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            _context.ChangeTracker.Clear();
            await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);

            // 1. Lock the personnel row FIRST (no earlier read may fix a snapshot). A no-op write of the value the row already holds: it changes
            //    nothing but holds the row lock until commit, so a registration decision or a PIS Admin edit running at the same moment either
            //    finished before this point or waits for the transfer and then re-evaluates the committed owner. Lock order: the users_pis row
            //    only (the history insert takes no lock anyone else needs), which composes with a decision's users_pis -> approve_mobile_users order.
            await _context.UsersPis.Where(p => p.NsgNumber == nsgNumber)
                .ExecuteUpdateAsync(s => s.SetProperty(p => p.AssignedTo, p => p.AssignedTo), cancellationToken);

            // 2. The current owner, read after the lock (the first consistent read of this transaction), so it cannot change underneath us.
            var current = await _context.UsersPis.AsNoTracking().Where(p => p.NsgNumber == nsgNumber)
                .Select(p => new { p.Id, p.NsgNumber, p.AssignedTo }).FirstOrDefaultAsync(cancellationToken);
            if (current == null) return new PisOwnershipTransferResult(PisOwnershipTransferOutcome.PersonnelNotFound);

            if (current.AssignedTo == targetAdminId)
                return new PisOwnershipTransferResult(PisOwnershipTransferOutcome.AlreadyAssigned, current.Id, current.NsgNumber, current.AssignedTo);

            // 3. The target must still be an active, activated PIS_ADMIN (not locked: a deactivation racing this very moment is not prevented).
            bool eligible = await _context.AdminProfiles.AsNoTracking().AnyAsync(p =>
                p.Id == targetAdminId && p.IsActive && p.ActivationStatus == AdminActivationStatus.Active
                && p.Role!.IsActive && p.Role.RoleCode == AdminRoleCodes.PisAdmin, cancellationToken);
            if (!eligible) return new PisOwnershipTransferResult(PisOwnershipTransferOutcome.TargetNotEligible, current.Id, current.NsgNumber, current.AssignedTo);

            // 4. Change ONLY assigned_to (ExecuteUpdate writes exactly the named column: created_by, updated_by and updated_at_ist stay as they are).
            await _context.UsersPis.Where(p => p.Id == current.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(p => p.AssignedTo, (long?)targetAdminId), cancellationToken);

            // 5. History, same transaction.
            _context.PisOwnershipTransfers.Add(new PisOwnershipTransfer
            {
                PisRecordId = current.Id,
                NsgNumber = current.NsgNumber,
                PreviousAssignedTo = current.AssignedTo,
                NewAssignedTo = targetAdminId,
                TransferredBy = actorAdminId,
                Reason = reason,
                TransferredAtIst = nowIst
            });
            await _context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            _context.ChangeTracker.Clear();

            return new PisOwnershipTransferResult(PisOwnershipTransferOutcome.Transferred, current.Id, current.NsgNumber, current.AssignedTo);
        });
    }

    public async Task<PisBulkOwnershipTransferResult> BulkTransferAsync(
        long sourceAdminId, long targetAdminId, long actorAdminId, string? reason, DateTime nowIst, CancellationToken cancellationToken = default)
    {
        var strategy = _context.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            _context.ChangeTracker.Clear();
            await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);

            // 1. Lock all source-owned personnel rows FIRST (no earlier read may fix a snapshot).
            //    ix_users_pis_assigned_to is indexed, so InnoDB acquires record and gap/next-key locks on assigned_to = sourceAdminId
            //    until commit, preventing concurrent individual or bulk transfers from slipping rows in or out mid-operation.
            //    Lock order matches TransferAsync and registration decisions: users_pis rows first, then history insert.
            await _context.UsersPis.Where(p => p.AssignedTo == sourceAdminId)
                .ExecuteUpdateAsync(s => s.SetProperty(p => p.AssignedTo, p => p.AssignedTo), cancellationToken);

            // 2. Read the locked rows (first consistent read of this transaction).
            var records = await _context.UsersPis.AsNoTracking()
                .Where(p => p.AssignedTo == sourceAdminId)
                .Select(p => new { p.Id, p.NsgNumber })
                .ToListAsync(cancellationToken);

            if (records.Count == 0)
                return new PisBulkOwnershipTransferResult(PisBulkOwnershipTransferOutcome.NothingToTransfer);

            // 3. Re-verify target eligibility inside the transaction.
            bool eligible = await _context.AdminProfiles.AsNoTracking().AnyAsync(p =>
                p.Id == targetAdminId && p.IsActive && p.ActivationStatus == AdminActivationStatus.Active
                && p.Role!.IsActive && p.Role.RoleCode == AdminRoleCodes.PisAdmin, cancellationToken);
            if (!eligible)
                return new PisBulkOwnershipTransferResult(PisBulkOwnershipTransferOutcome.TargetNotEligible);

            // 4. Update ONLY assigned_to (created_by, updated_by, updated_at_ist and all personnel columns stay untouched).
            int updated = await _context.UsersPis.Where(p => p.AssignedTo == sourceAdminId)
                .ExecuteUpdateAsync(s => s.SetProperty(p => p.AssignedTo, (long?)targetAdminId), cancellationToken);
            if (updated != records.Count)
                throw new InvalidOperationException($"Bulk ownership transfer updated {updated} rows but locked {records.Count} rows.");

            // 5. History, same transaction (one row per transferred personnel record).
            List<PisOwnershipTransfer> history = records.Select(r => new PisOwnershipTransfer
            {
                PisRecordId = r.Id,
                NsgNumber = r.NsgNumber,
                PreviousAssignedTo = sourceAdminId,
                NewAssignedTo = targetAdminId,
                TransferredBy = actorAdminId,
                Reason = reason,
                TransferredAtIst = nowIst
            }).ToList();
            _context.PisOwnershipTransfers.AddRange(history);
            await _context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            _context.ChangeTracker.Clear();

            return new PisBulkOwnershipTransferResult(PisBulkOwnershipTransferOutcome.Transferred, updated);
        });
    }
}
