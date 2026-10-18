namespace NSG.Mobile.Domain.Constants;

/// <summary>The fixed role codes stored in <c>admin_roles.role_code</c>. Also the role claim values of admin access tokens.</summary>
public static class AdminRoleCodes
{
    public const string SuperAdmin = "SUPER_ADMIN";
    public const string RegistrationAdmin = "REGISTRATION_ADMIN";
    public const string PisAdmin = "PIS_ADMIN";
    public const string GrievanceAdmin = "GRIEVANCE_ADMIN";
    public const string MediaCellAdmin = "MEDIA_CELL_ADMIN";
    public const string SalaryManagementAdmin = "SALARY_MANAGEMENT_ADMIN";
}
