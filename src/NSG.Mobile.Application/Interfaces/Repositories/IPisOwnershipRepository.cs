namespace NSG.Mobile.Application.Interfaces.Repositories;

public enum PisBulkOwnershipTransferOutcome
{
    Transferred = 0,

    /// <summary>Source admin has no assigned personnel; nothing was changed.</summary>
    NothingToTransfer,

    /// <summary>Target stopped being an active, activated PIS_ADMIN between the service check and the transaction.</summary>
    TargetNotEligible
}

/// <summary>Outcome of a bulk transfer. <see cref="TransferredCount"/> is 0 on <see cref="PisBulkOwnershipTransferOutcome.NothingToTransfer"/>.</summary>
public sealed record PisBulkOwnershipTransferResult(PisBulkOwnershipTransferOutcome Outcome, int TransferredCount = 0);

public enum PisOwnershipTransferOutcome
{
    Transferred = 0,

    /// <summary>The record is already assigned to the target admin; nothing was changed and no history was written.</summary>
    AlreadyAssigned,

    PersonnelNotFound,

    /// <summary>The target stopped being an active, activated PIS_ADMIN between the service's check and the transaction; nothing was changed.</summary>
    TargetNotEligible
}

/// <summary>Outcome of a transfer. <see cref="PreviousAssignedTo"/> is the owner found under the row lock (null = unassigned).</summary>
public sealed record PisOwnershipTransferResult(
    PisOwnershipTransferOutcome Outcome, long PisRecordId = 0, string? NsgNumber = null, long? PreviousAssignedTo = null);

/// <summary>Changes the responsible PIS Admin of a personnel record. The only code that writes <c>users_pis.assigned_to</c> after creation.</summary>
public interface IPisOwnershipRepository
{
    /// <summary>
    /// In ONE transaction: locks the personnel row (first statement; the same lock order as a registration decision: personnel row, then
    /// approval row), reads the current owner, re-verifies that <paramref name="targetAdminId"/> is an active, activated PIS_ADMIN, updates
    /// ONLY <c>assigned_to</c> (never <c>created_by</c>, <c>updated_by</c> or any other column) and appends the history row.
    /// Approval requests are not touched: they follow the owner through the live join.
    /// </summary>
    Task<PisOwnershipTransferResult> TransferAsync(
        string nsgNumber, long targetAdminId, long actorAdminId, string? reason, DateTime nowIst, CancellationToken cancellationToken = default);

    /// <summary>
    /// In ONE transaction: reads all <c>users_pis</c> rows where <c>assigned_to = sourceAdminId</c>, re-verifies that
    /// <paramref name="targetAdminId"/> is still an active, activated PIS_ADMIN, updates <c>assigned_to</c> on every
    /// matched row (ONLY that column), inserts one <see cref="PisOwnershipTransfer"/> history row per transferred record,
    /// and commits. Returns the count of records actually updated (rows already owned by target are excluded).
    /// </summary>
    Task<PisBulkOwnershipTransferResult> BulkTransferAsync(
        long sourceAdminId, long targetAdminId, long actorAdminId, string? reason, DateTime nowIst, CancellationToken cancellationToken = default);
}
