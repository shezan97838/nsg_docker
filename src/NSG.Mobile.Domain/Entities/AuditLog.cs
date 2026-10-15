using NSG.Mobile.Domain.Enums;

namespace NSG.Mobile.Domain.Entities;

/// <summary>
/// Append-oriented audit log for security and compliance tracking.
/// SOW §3.8: "Comprehensive audit logging of user actions."
/// 
/// IMPORTANT: Never store passwords, OTPs, tokens, or sensitive PII in AdditionalData.
/// </summary>
public class AuditLog
{
    public long Id { get; set; }

    /// <summary>User who performed the action. Null for pre-authentication events.</summary>
    public int? UserId { get; set; }

    /// <summary>The action being audited.</summary>
    public AuditAction Action { get; set; }

    /// <summary>Type of entity affected (e.g., "User", "Device", "RefreshToken").</summary>
    public string? EntityType { get; set; }

    /// <summary>ID of the affected entity.</summary>
    public string? EntityId { get; set; }

    /// <summary>Outcome: "Success" or "Failure".</summary>
    public string Result { get; set; } = string.Empty;

    /// <summary>Client IP address.</summary>
    public string? IpAddress { get; set; }

    /// <summary>Device ID if action was performed from a registered device.</summary>
    public int? DeviceId { get; set; }

    /// <summary>Request correlation/trace ID for distributed tracing.</summary>
    public string? CorrelationId { get; set; }

    /// <summary>
    /// Additional metadata as JSON. Must NOT contain secrets, tokens, passwords, or OTPs.
    /// Example: {"reason": "Password mismatch", "attemptNumber": 3}
    /// </summary>
    public string? AdditionalData { get; set; }

    /// <summary>IST timestamp when the event occurred.</summary>
    public DateTime CreatedAt { get; set; } = NSG.Mobile.Domain.Common.IndianStandardTime.Now;
}
