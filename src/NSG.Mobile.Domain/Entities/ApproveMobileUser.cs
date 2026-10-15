using NSG.Mobile.Domain.Enums;

namespace NSG.Mobile.Domain.Entities;

/// <summary>
/// Request to activate the mobile app for one person. Table <c>approve_mobile_users</c>. Approved and rejected requests are
/// kept as history, so none of NSG number, mobile number or date of birth is unique here.
///
/// <b>At most one pending request per NSG number.</b> MySQL has no partial unique index, so the database enforces this with
/// <see cref="PendingNsgNumber"/>, a stored generated column (<c>IF(approval_status = 0, nsg_number, NULL)</c>) that carries a
/// unique index: NULLs may repeat, so only pending rows can collide. See
/// docs/architecture/ADR-014-mobile-user-profile-and-approval-request.md.
///
/// All timestamps are UTC instants; convert to Asia/Kolkata (IST, UTC+05:30) only at the API/UI boundary.
///
/// The future approval workflow must keep <see cref="UserPis.MobileAppActivationStatus"/> in step with
/// <see cref="ApprovalStatus"/>; that workflow is not part of this entity.
/// </summary>
public class ApproveMobileUser
{
    /// <summary>Surrogate key (MySQL BIGINT).</summary>
    public long Id { get; set; }

    /// <summary>NSG number as submitted and matched against PIS. Required, not unique (history).</summary>
    public string NsgNumber { get; set; } = string.Empty;

    /// <summary>Personal mobile number: exactly ten digits starting 6-9, no country code. Required, not unique (history).</summary>
    public string MobileNumber { get; set; } = string.Empty;

    public DateOnly DateOfBirth { get; set; }

    /// <summary>Review state; reuses the existing enum (Pending = 0, Approved = 1, Rejected = 2). New requests are Pending.</summary>
    public MobileAppActivationStatus ApprovalStatus { get; set; } = MobileAppActivationStatus.Pending;

    /// <summary>
    /// Database-generated: equals <see cref="NsgNumber"/> while the request is Pending, otherwise NULL. It exists only to carry
    /// the unique index that allows one pending request per NSG number. Read-only: never set by application code.
    /// </summary>
    public string? PendingNsgNumber { get; private set; }

    public DateTime RequestedAtIst { get; set; } = NSG.Mobile.Domain.Common.IndianStandardTime.Now;

    /// <summary>User id of the reviewer (no FK, following <see cref="User.ApprovedBy"/>). Null until reviewed.</summary>
    public int? ApprovedBy { get; set; }

    /// <summary>When the request was reviewed (IST). Null until reviewed.</summary>
    public DateTime? ApprovedAtIst { get; set; }

    /// <summary>Reviewer's remarks. Optional.</summary>
    public string? Remarks { get; set; }

    // ── Audit ──

    public DateTime CreatedAtIst { get; set; } = NSG.Mobile.Domain.Common.IndianStandardTime.Now;
    public int? CreatedBy { get; set; }
    public DateTime? UpdatedAtIst { get; set; }
    public int? UpdatedBy { get; set; }
}
