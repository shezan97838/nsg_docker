using NSG.Mobile.Domain.Common;

namespace NSG.Mobile.Domain.Entities;

/// <summary>
/// One change of the responsible PIS Admin of a personnel record (<c>users_pis.assigned_to</c>), written in the same transaction as the change.
/// Append-only history. Table <c>pis_ownership_transfers</c>. Like the rest of the PIS tables it has no foreign keys: the admin ids are
/// <c>admin_profiles.id</c> values and <see cref="PisRecordId"/> is <c>users_pis.id</c>, so the history survives any later change of either.
/// </summary>
public class PisOwnershipTransfer
{
    public long Id { get; set; }

    /// <summary><c>users_pis.id</c> of the personnel record.</summary>
    public long PisRecordId { get; set; }

    /// <summary>NSG number of the record at the time of the transfer.</summary>
    public string NsgNumber { get; set; } = string.Empty;

    /// <summary><c>assigned_to</c> before the transfer; null when the record was unassigned.</summary>
    public long? PreviousAssignedTo { get; set; }

    /// <summary><c>assigned_to</c> after the transfer: the target PIS Admin's <c>admin_profiles.id</c>.</summary>
    public long NewAssignedTo { get; set; }

    /// <summary><c>admin_profiles.id</c> of the Super Admin who performed the transfer, taken from the token.</summary>
    public long TransferredBy { get; set; }

    public string? Reason { get; set; }

    public DateTime TransferredAtIst { get; set; } = IndianStandardTime.Now;
}
