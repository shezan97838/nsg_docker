using NSG.Mobile.Domain.Enums;

namespace NSG.Mobile.Domain.Entities;

/// <summary>
/// Grievance category master (SOW §3.8). Table <c>grievance_category</c>. Rows are never physically deleted: <see cref="IsActive"/> hides a
/// category from new submissions and <see cref="IsDeleted"/> marks it deleted (soft delete); existing grievances keep pointing at it.
/// Creator and updater are <c>admin_profiles.id</c> values taken from the token (no foreign key, like the other admin id columns).
/// </summary>
public class GrievanceCategory
{
    public int Id { get; set; }

    /// <summary>Display name. Required, unique (also among deactivated and deleted categories).</summary>
    public string GrievanceName { get; set; } = string.Empty;

    /// <summary>Reserved for future use. No table exists yet, so there is no foreign key.</summary>
    public int? DepartmentId { get; set; }

    public bool IsActive { get; set; } = true;
    public bool IsDeleted { get; set; }

    public DateTime CreatedAtIst { get; set; } = NSG.Mobile.Domain.Common.IndianStandardTime.Now;
    public long? CreatedBy { get; set; }
    public DateTime? UpdatedAtIst { get; set; }
    public long? UpdatedBy { get; set; }
}

/// <summary>
/// A grievance raised by an employee. Table <c>grievance</c>. Soft delete only (<see cref="IsActive"/>). Timestamps are IST instants.
/// <see cref="CreatedBy"/> is a mobile profile id; <see cref="AssignedTo"/>, <see cref="ActionTakenBy"/> and <see cref="ClosedBy"/>
/// are admin profile ids. The admin columns carry no foreign key (they are filled by the service from the token).
/// </summary>
public class Grievance
{
    public long Id { get; set; }

    /// <summary>Ticket number, format <c>NSG-YYYY-000001</c> (column <c>grievance_id</c>). Required, unique.</summary>
    public string TicketNumber { get; set; } = string.Empty;

    public int CategoryId { get; set; }
    public GrievanceCategory? Category { get; set; }

    public string Subject { get; set; } = string.Empty;
    public string GrievanceNote { get; set; } = string.Empty;

    /// <summary>
    /// JSON array of document objects { key, name, contentType, sizeBytes, uploadedAtIst } (column <c>document_urls</c>).
    /// <c>key</c> is the private-bucket object key, never a URL.
    /// </summary>
    public string? DocumentsJson { get; set; }

    public GrievanceStatus CurrentStatus { get; set; } = GrievanceStatus.Submitted;

    /// <summary><c>mobile_user_profiles.id</c> of the employee, taken from the token.</summary>
    public long CreatedBy { get; set; }
    public DateTime CreatedAtIst { get; set; } = NSG.Mobile.Domain.Common.IndianStandardTime.Now;
    public DateTime? UpdatedAtIst { get; set; }

    /// <summary><c>admin_profiles.id</c> of the Grievance Admin the grievance was forwarded or assigned to; null while unassigned.</summary>
    public long? AssignedTo { get; set; }

    public long? ActionTakenBy { get; set; }
    public DateTime? ActionTakenAtIst { get; set; }
    public string? ActionRemarks { get; set; }

    public long? ClosedBy { get; set; }
    public DateTime? ClosedAtIst { get; set; }
    public string? ClosedRemarks { get; set; }


    /// <summary>
    /// Object key (reference) of the supporting file the Grievance Admin uploaded when resolving, in the private bucket (column <c>resolution_document</c>,
    /// varchar). Never a public URL: the file is served only through the authenticated download endpoints. Null on open grievances and on
    /// grievances closed before resolution files existed.
    /// </summary>
    public string? ResolutionDocument { get; set; }

    public bool IsActive { get; set; } = true;
}

/// <summary>Append-only history of a grievance: creation, status changes, comments and forwards. Table <c>grievance_status_log</c>. Append-only.</summary>
public class GrievanceStatusLog
{
    public long Id { get; set; }

    /// <summary>What this row records. A comment or a forward leaves the status unchanged (<see cref="FromStatus"/> equals <see cref="ToStatus"/>).</summary>
    public GrievanceLogAction LogAction { get; set; } = GrievanceLogAction.StatusChange;

    /// <summary><c>admin_profiles.id</c> the grievance was forwarded to; set only when <see cref="LogAction"/> is Forward.</summary>
    public long? ForwardedTo { get; set; }

    /// <summary><c>grievance.id</c> (column <c>grievance_pk</c>).</summary>
    public long GrievancePk { get; set; }

    /// <summary>Null for the first row (creation).</summary>
    public GrievanceStatus? FromStatus { get; set; }
    public GrievanceStatus ToStatus { get; set; }

    public string? Remarks { get; set; }

    /// <summary>Id in the table named by <see cref="ChangedByType"/>.</summary>
    public long ChangedBy { get; set; }
    public GrievanceActorType ChangedByType { get; set; }
    public DateTime ChangedAtIst { get; set; } = NSG.Mobile.Domain.Common.IndianStandardTime.Now;
}
