using NSG.Mobile.Application.Interfaces.Repositories;
using NSG.Mobile.Application.Interfaces.Services;
using NSG.Mobile.Domain.Common;
using NSG.Mobile.Domain.Entities;
using NSG.Mobile.Domain.Enums;

namespace NSG.Mobile.Infrastructure.Services;

/// <summary>
/// Audit service implementation — persists audit events to the database.
/// Handles exceptions gracefully to avoid breaking the primary operation.
/// </summary>
public class AuditService : IAuditService
{
    private readonly IAuditLogRepository _auditLogRepository;

    public AuditService(IAuditLogRepository auditLogRepository)
    {
        _auditLogRepository = auditLogRepository;
    }

    public async Task LogAsync(
        AuditAction action,
        string result,
        int? userId = null,
        string? entityType = null,
        string? entityId = null,
        string? ipAddress = null,
        int? deviceId = null,
        string? correlationId = null,
        string? additionalData = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var auditLog = new AuditLog
            {
                UserId = userId,
                Action = action,
                EntityType = entityType,
                EntityId = entityId,
                Result = result,
                IpAddress = ipAddress,
                DeviceId = deviceId,
                CorrelationId = correlationId,
                AdditionalData = additionalData,
                CreatedAt = IndianStandardTime.Now
            };

            await _auditLogRepository.AddAsync(auditLog, cancellationToken);
        }
        catch (Exception)
        {
            // Audit logging failures must not break the primary operation.
            // In production, this should be logged to a secondary sink (e.g., structured logging).
        }
    }
}
