namespace NSG.Mobile.Domain.Constants;

/// <summary>
/// Domain-level constants. Security-sensitive defaults are intentionally conservative.
/// Production values should be loaded from secure configuration.
/// </summary>
public static class DomainConstants
{
    /// <summary>Authorization policy and role names.</summary>
    public static class Roles
    {
        public const string Employee = nameof(Employee);
        public const string ITAdmin = nameof(ITAdmin);
        public const string NodalOfficer = nameof(NodalOfficer);
        public const string SystemAdmin = nameof(SystemAdmin);
    }

    /// <summary>Authorization policy names.</summary>
    public static class Policies
    {
        public const string RequireITAdmin = nameof(RequireITAdmin);
        public const string RequireNodalOfficer = nameof(RequireNodalOfficer);
        public const string RequireSystemAdmin = nameof(RequireSystemAdmin);
        public const string RequireActiveAccount = nameof(RequireActiveAccount);

        /// <summary>A mobile-app access token issued by mobile-auth/login (registration grants are opaque secrets, not JWTs).</summary>
        public const string RequireMobileProfile = nameof(RequireMobileProfile);
    }

    /// <summary>Custom JWT claims used by mobile-app access tokens.</summary>
    public static class MobileClaims
    {
        public const string ProfileId = "profile_id";
        public const string TokenUse = "token_use";
        /// <summary>Session (refresh-token family) the access token belongs to; checked on every request.</summary>
        public const string SessionId = "sid";
        public const string MobileAccess = "mobile_access";
    }

    /// <summary>Mobile number normalization constants.</summary>
    public static class MobileNumber
    {
        public const string IndiaCountryCode = "+91";
        public const int ExpectedDigitCount = 10;
    }

    /// <summary>Format rules for PIS (personnel master) fields. Shared by validators; mirrored by database check constraints.</summary>
    public static class PisFormats
    {
        /// <summary>Exactly ten digits, starting 6-9 (Indian mobile, no country code).</summary>
        public const string MobileNumberPattern = @"^[6-9][0-9]{9}$";

        /// <summary>Permanent Account Number: five letters, four digits, one letter (upper-case after normalisation).</summary>
        public const string PanPattern = @"^[A-Z]{5}[0-9]{4}[A-Z]$";

        /// <summary>Aadhaar: exactly twelve digits, kept as text.</summary>
        public const string AadhaarPattern = @"^[0-9]{12}$";

        /// <summary>
        /// PRAN: digits only, twelve long. PROVISIONAL — no client-approved format exists yet (see OQ-024);
        /// it matches the NPS PRAN length and the workbook sample.
        /// </summary>
        public const string PranPattern = @"^[0-9]{12}$";

        /// <summary>
        /// Salary account number: digits only, 9-18 long (not a fixed length). PROVISIONAL — no client-approved
        /// rule exists yet (see OQ-024).
        /// </summary>
        public const string SalaryAccountPattern = @"^[0-9]{9,18}$";

        /// <summary>Blind index: 64 lower-case hex characters (HMAC-SHA256).</summary>
        public const string BlindIndexPattern = @"^[0-9a-f]{64}$";

        /// <summary>Six-digit PIN code, first digit 1-9.</summary>
        public const string PinCodePattern = @"^[1-9][0-9]{5}$";

        /// <summary>Largest value of MySQL DECIMAL(5,2), used for weight in kilograms.</summary>
        public const decimal MaxWeightKg = 999.99m;

        /// <summary>Largest value of MySQL DECIMAL(7,2), used for the leave-due quantities.</summary>
        public const decimal MaxLeaveDue = 99999.99m;
    }

    /// <summary>Audit log result values.</summary>
    public static class AuditResults
    {
        public const string Success = "Success";
        public const string Failure = "Failure";
    }

    /// <summary>Refresh token revocation reasons.</summary>
    public static class TokenRevokeReasons
    {
        public const string Rotated = "Rotated";
        public const string Logout = "Logout";
        public const string ReuseDetected = "ReuseDetected";
        public const string DeviceRevoked = "DeviceRevoked";
        public const string AdminAction = "AdminAction";
    }
}
