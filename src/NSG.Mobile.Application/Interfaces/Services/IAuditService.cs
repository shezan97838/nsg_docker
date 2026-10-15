using NSG.Mobile.Domain.Enums;

namespace NSG.Mobile.Application.Interfaces.Services;

/// <summary>
/// Audit logging service abstraction.
/// SOW §3.8: "Comprehensive audit logging of user actions."
/// </summary>
public interface IAuditService
{
    /// <summary>Record an audit event. Must NOT include sensitive data (passwords, OTPs, tokens).</summary>
    Task LogAsync(
        AuditAction action,
        string result,
        int? userId = null,
        string? entityType = null,
        string? entityId = null,
        string? ipAddress = null,
        int? deviceId = null,
        string? correlationId = null,
        string? additionalData = null,
        CancellationToken cancellationToken = default);
}
