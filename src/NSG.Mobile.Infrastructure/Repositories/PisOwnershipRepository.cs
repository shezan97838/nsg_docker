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
}
