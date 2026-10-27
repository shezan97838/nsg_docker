namespace NSG.Mobile.Domain.Enums;

/// <summary>
/// Audit log action types for security and compliance tracking.
/// SOW §3.8: "Comprehensive audit logging of user actions."
/// </summary>
public enum AuditAction
{
    // Authentication events
    LoginSuccess = 0,
    LoginFailure = 1,
    Logout = 2,
    TokenRefreshed = 3,
    TokenReuseDetected = 4,

    // OTP events
    OtpRequested = 10,
    OtpVerified = 11,
    OtpFailed = 12,
    OtpResendRequested = 13,

    // Onboarding events
    RegistrationSubmitted = 20,
    ITApprovalGranted = 21,
    ITApprovalRejected = 22,
    MobileApprovalRequested = 23,
    MobileProfileCreated = 24,

    // Device events
    DeviceRegistered = 30,
    DeviceRevoked = 31,

    // Account events
    PasswordChanged = 40,
    AccountLocked = 41,
    AccountUnlocked = 42,
    AccountSuspended = 43,
    MpinChanged = 44,
    MpinReset = 45,

    // PIS events
    ProfileViewed = 50,
    ChangeRequestSubmitted = 51,
    ChangeRequestApproved = 52,
    ChangeRequestRejected = 53,

    // Document access events
    DocumentAccessed = 60,
    DocumentDownloaded = 61,

    // Administrative events
    AdminAction = 70,
    ConfigurationChanged = 71,

    // Grievance management events
    GrievanceCreated = 80,
    GrievanceStatusChanged = 81,
    GrievanceDocumentUploaded = 82,
    GrievanceCategoryChanged = 83,
    GrievanceViewed = 84,
    GrievanceCommentAdded = 85,
    GrievanceForwarded = 86,

    // DG message events
    DgMessageCreated = 90,
    DgMessageUpdated = 91,
    DgMessageDeleted = 92,

    // Leave management events
    LeaveRecordChanged = 100,
    LeaveBulkUploadProcessed = 101,
    LeaveRecordViewed = 102
}
