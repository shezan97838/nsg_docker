namespace NSG.Mobile.Domain.Entities;

/// <summary>
/// One of the six fixed Admin Portal roles. Table <c>admin_roles</c>. The rows are seeded by migration and are never created,
/// renamed or deleted by portal operations; authorization is decided by <see cref="RoleCode"/> through fixed policies, so there
/// is deliberately no permission table. <see cref="IsActive"/> exists only so a role can be withdrawn by a reviewed migration.
/// </summary>
public class AdminRole
{
    /// <summary>Surrogate key (MySQL INT, like the other small lookup tables).</summary>
    public int Id { get; set; }

    /// <summary>Stable identifier, see <see cref="NSG.Mobile.Domain.Constants.AdminRoleCodes"/>. Required, unique, never changes.</summary>
    public string RoleCode { get; set; } = string.Empty;

    /// <summary>Display name.</summary>
    public string RoleName { get; set; } = string.Empty;

    public string? Description { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAtIst { get; set; } = NSG.Mobile.Domain.Common.IndianStandardTime.Now;
    public DateTime? UpdatedAtIst { get; set; }
}

