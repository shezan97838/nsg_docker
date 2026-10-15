using System.Text.Json.Serialization;
using NSG.Mobile.Domain.Enums;

namespace NSG.Mobile.Application.MobileAuth.DTOs;

// ─── Requests ───
// None of these carries a role, an approval status or a user id: the server decides all three.

/// <summary>The three identifiers a person uses to identify themselves before an account exists.</summary>
public class MobileIdentityRequestDto
{
    /// <summary>NSG number as printed on the identity card.</summary>
    public string NsgNumber { get; set; } = string.Empty;

    /// <summary>Personal mobile number registered in PIS: ten digits, an optional +91 prefix is accepted.</summary>
    public string MobileNumber { get; set; } = string.Empty;

    /// <summary>Date of birth (yyyy-MM-dd).</summary>
    public DateOnly DateOfBirth { get; set; }
}

/// <summary>Which OTP flow a request belongs to. Eligibility for the flow is always re-checked on the server.</summary>
public enum MobileOtpFlow
{
    /// <summary>First-time registration of a person who has no profile yet.</summary>
    Registration = 0,

    /// <summary>MPIN recovery for a person who already has an active profile.</summary>
    MpinReset = 1
}

public class MobileLoginCheckRequestDto : MobileIdentityRequestDto { }

public class MobileLoginRequestDto : MobileIdentityRequestDto
{
    public string Mpin { get; set; } = string.Empty;
}

public class MobileOtpSendRequestDto : MobileIdentityRequestDto
{
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public MobileOtpFlow Flow { get; set; } = MobileOtpFlow.Registration;
}

public class MobileOtpVerifyRequestDto : MobileIdentityRequestDto
{
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public MobileOtpFlow Flow { get; set; } = MobileOtpFlow.Registration;
    public Guid OtpReferenceId { get; set; }
    public string OtpCode { get; set; } = string.Empty;
}

public class RegistrationSubmitRequestDto
{
    /// <summary>The registration grant returned by otp/verify.</summary>
    public string GrantToken { get; set; } = string.Empty;
}

public class MpinSetupRequestDto
{
    /// <summary>The MPIN-setup grant returned by registration/submit.</summary>
    public string GrantToken { get; set; } = string.Empty;
    public string Mpin { get; set; } = string.Empty;
    public string ConfirmMpin { get; set; } = string.Empty;
}

public class MpinChangeRequestDto
{
    public string CurrentMpin { get; set; } = string.Empty;
    public string NewMpin { get; set; } = string.Empty;
    public string ConfirmNewMpin { get; set; } = string.Empty;
}

public class MpinResetRequestDto
{
    /// <summary>The MPIN-reset grant returned by otp/verify with flow MpinReset.</summary>
    public string GrantToken { get; set; } = string.Empty;
    public string NewMpin { get; set; } = string.Empty;
    public string ConfirmNewMpin { get; set; } = string.Empty;
}

// ─── Responses ───

/// <summary>
/// Outcome of a step in the registration / recovery flow. <see cref="Status"/> tells the app what to do next; only the
/// fields relevant to that status are present.
/// </summary>
public class MobileAuthFlowDto
{
    /// <summary>One of <see cref="MobileAuthStatus"/>.</summary>
    public string Status { get; set; } = string.Empty;

    private string? _message;

    /// <summary>Human-readable message for the status. Not part of <c>data</c>: the controller returns it as the response <c>message</c>.</summary>
    [JsonIgnore]
    public string Message
    {
        get => _message ?? MobileAuthMessages.For(Status);
        set => _message = value;
    }

    /// <summary>Reference to quote when verifying the OTP.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Guid? OtpReferenceId { get; set; }

    /// <summary>The registered number the OTP went to, masked (only the last four digits show).</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? MaskedMobileNumber { get; set; }

    /// <summary>True when OTPs are in demonstration mode: no SMS was sent and the OTP is the fixed demonstration code.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool DemoOtpMode { get; set; }

    /// <summary>When the OTP expires, in IST (+05:30).</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DateTimeOffset? OtpExpiresAt { get; set; }

    /// <summary>Short-lived single-use grant for the next step. It is not an access token and opens no protected API.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? GrantToken { get; set; }

    /// <summary>When the grant expires, in IST (+05:30).</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DateTimeOffset? GrantExpiresAt { get; set; }
}

/// <summary>Successful MPIN login.</summary>
public class MobileLoginResponseDto
{
    public string AccessToken { get; set; } = string.Empty;
    public string TokenType { get; set; } = "Bearer";

    /// <summary>Access-token lifetime in seconds.</summary>
    public int ExpiresIn { get; set; }

    /// <summary>Opaque refresh token. Single use: every refresh returns a new one and invalidates this one. Store it in the platform secure storage.</summary>
    public string RefreshToken { get; set; } = string.Empty;

    /// <summary>When the refresh token expires, in IST (+05:30).</summary>
    public DateTimeOffset RefreshTokenExpiresAt { get; set; }

    public MobileProfileSummaryDto Profile { get; set; } = new();
}

/// <summary>Minimal profile summary: no hashes, date of birth or mobile number.</summary>
public class MobileProfileSummaryDto
{
    public long Id { get; set; }
    public string NsgNumber { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
}

// ─── Admin approval ───

public class MobileApprovalDecisionRequestDto
{
    /// <summary>Approved or Rejected. Pending is not a decision.</summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public MobileAppActivationStatus Decision { get; set; }

    /// <summary>Optional reviewer remarks (at most 500 characters).</summary>
    public string? Remarks { get; set; }
}

public class MobileApprovalRequestDto
{
    public long Id { get; set; }
    public string NsgNumber { get; set; } = string.Empty;
    public string? FullName { get; set; }

    /// <summary>Personal mobile number, masked.</summary>
    public string MaskedMobileNumber { get; set; } = string.Empty;

    /// <summary>Pending, Approved or Rejected.</summary>
    public string Status { get; set; } = string.Empty;

    /// <summary>When the request was raised, in IST (+05:30).</summary>
    public DateTimeOffset RequestedAt { get; set; }

    /// <summary>User id of the IT admin who decided; absent while Pending.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? ReviewedBy { get; set; }

    /// <summary>When the request was decided, in IST (+05:30); absent while Pending.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DateTimeOffset? ReviewedAt { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Remarks { get; set; }
}

// ─── Sessions ───

public class MobileRefreshRequestDto
{
    public string RefreshToken { get; set; } = string.Empty;
}

public class MobileLogoutRequestDto
{
    /// <summary>The refresh token of the session to end.</summary>
    public string RefreshToken { get; set; } = string.Empty;
}
