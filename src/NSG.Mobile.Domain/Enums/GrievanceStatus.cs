namespace NSG.Mobile.Domain.Enums;

/// <summary>
/// Lifecycle of a grievance (SOW §3.8). Stored as INT. The only allowed transitions are
/// Submitted → UnderReview → ActionTaken → Closed; the check lives in the grievance service.
/// </summary>
public enum GrievanceStatus
{
    Submitted = 1,
    UnderReview = 2,
    ActionTaken = 3,
    Closed = 4
}

/// <summary>Kind of history row in <c>grievance_status_log</c> (stored as INT).</summary>
public enum GrievanceLogAction
{
    /// <summary>Creation (no from-status) or a status change.</summary>
    StatusChange = 1,

    /// <summary>A comment added by an admin; the status is unchanged.</summary>
    Comment = 2,

    /// <summary>Forwarded or reassigned to another Grievance Admin; the status is unchanged.</summary>
    Forward = 3
}

/// <summary>Which identity space a status-log actor id belongs to (employees and admins are different tables).</summary>
public enum GrievanceActorType
{
    /// <summary><c>mobile_user_profiles.id</c> — the employee who raised the grievance.</summary>
    Employee = 1,

    /// <summary><c>admin_profiles.id</c> — a Grievance Admin.</summary>
    Admin = 2
}
