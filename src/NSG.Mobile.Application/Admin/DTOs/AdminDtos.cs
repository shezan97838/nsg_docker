namespace NSG.Mobile.Application.Admin.DTOs;

// ─── Requests ───
// None of these carries an admin id, a status or an OTP destination: the server decides all three.

/// <summary>Super Admin request to provision an account. The personal details are checked against PIS, never stored.</summary>
public class CreateAdminAccountRequestDto
{
    public string NsgNumber { get; set; } = string.Empty;

    /// <summary>Mobile number registered in PIS: ten digits, an optional +91 prefix is accepted. Used only to check the PIS record.</summary>
    public string MobileNumber { get; set; } = string.Empty;

    /// <summary>Date of birth (yyyy-MM-dd) as held in PIS.</summary>
    public DateOnly DateOfBirth { get; set; }

    /// <summary>One of the six fixed role codes, for example <c>PIS_ADMIN</c>.</summary>
    public string RoleCode { get; set; } = string.Empty;
}

/// <summary>Identity of a person about to activate an account.</summary>
public class AdminActivationOtpRequestDto
{
    public string NsgNumber { get; set; } = string.Empty;
    public string MobileNumber { get; set; } = string.Empty;
    public DateOnly DateOfBirth { get; set; }
}

public class AdminActivationCompleteRequestDto : AdminActivationOtpRequestDto
{
    public Guid OtpReferenceId { get; set; }
    public string OtpCode { get; set; } = string.Empty;
    public string NewPassword { get; set; } = string.Empty;
    public string ConfirmPassword { get; set; } = string.Empty;
}

/// <summary>Step 1 of sign-in: NSG number and password. The OTP goes to the number registered in PIS.</summary>
public class AdminLoginRequestDto
{
    public string NsgNumber { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}

/// <summary>Step 2 of sign-in: the password again, plus the OTP from step 1.</summary>
public class AdminLoginVerifyRequestDto : AdminLoginRequestDto
{
    public Guid OtpReferenceId { get; set; }
    public string OtpCode { get; set; } = string.Empty;
}

public class AdminRefreshRequestDto { public string RefreshToken { get; set; } = string.Empty; }
public class AdminLogoutRequestDto { public string RefreshToken { get; set; } = string.Empty; }

// ─── Responses ───

public class AdminRoleDto
{
    public int Id { get; set; }
    public string RoleCode { get; set; } = string.Empty;
    public string RoleName { get; set; } = string.Empty;
    public string? Description { get; set; }
}

/// <summary>An admin account as the Super Admin sees it. Holds no password hash, no mobile number and no date of birth.</summary>
public class AdminAccountDto
{
    public long Id { get; set; }
    public string NsgNumber { get; set; } = string.Empty;
    public string? FullName { get; set; }
    public string RoleCode { get; set; } = string.Empty;
    public string RoleName { get; set; } = string.Empty;
    public string ActivationStatus { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public DateTimeOffset? LastLoginAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
}

/// <summary>Step result of activation and of sign-in step 1.</summary>
public class AdminOtpSentDto
{
    public Guid OtpReferenceId { get; set; }
    public string MaskedMobileNumber { get; set; } = string.Empty;
    public DateTimeOffset OtpExpiresAt { get; set; }

    /// <summary>True when the fixed demonstration OTP is in use and no SMS was sent.</summary>
    public bool DemoOtpMode { get; set; }
}

public class AdminProfileSummaryDto
{
    public long Id { get; set; }
    public string NsgNumber { get; set; } = string.Empty;
    public string RoleCode { get; set; } = string.Empty;
    public string RoleName { get; set; } = string.Empty;
}

public class AdminLoginResponseDto
{
    public string AccessToken { get; set; } = string.Empty;
    public string TokenType { get; set; } = "Bearer";
    public int ExpiresIn { get; set; }
    public string RefreshToken { get; set; } = string.Empty;
    public DateTimeOffset RefreshTokenExpiresAt { get; set; }
    public AdminProfileSummaryDto Profile { get; set; } = new();
}
