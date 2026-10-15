namespace NSG.Mobile.Domain.Enums;

/// <summary>
/// Authorization roles for NSG personnel.
/// SOW §3.5, §3.7, §3.8: IT Cell approval, Nodal Officer routing, admin broadcasts.
/// Intentionally minimal — do NOT expand without confirmed SOW requirements.
/// </summary>
public enum UserRole
{
    /// <summary>Standard NSG personnel.</summary>
    Employee = 0,

    /// <summary>IT Cell administrator — manages onboarding approvals and system configuration.</summary>
    ITAdmin = 1,

    /// <summary>Nodal Officer — handles grievance review and routing (SOW §3.8 Grievance).</summary>
    NodalOfficer = 2,

    /// <summary>System Administrator — full system access for operations and maintenance.</summary>
    SystemAdmin = 3
}
