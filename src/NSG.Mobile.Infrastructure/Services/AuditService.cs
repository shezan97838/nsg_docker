using Microsoft.Extensions.Logging;
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
    private readonly ILogger<AuditService> _logger;

    public AuditService(IAuditLogRepository auditLogRepository, ILogger<AuditService> logger)
    {
        _auditLogRepository = auditLogRepository;
        _logger = logger;
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
        catch (Exception ex)
        {
            // Audit logging failures must not break the primary operation, but they are never silent: the failure goes to the
            // structured log (the secondary sink) with what is needed to find the event again. No audit payload is logged.
            _logger.LogError(ex, "Audit write failed for action {AuditAction} ({AuditResult}), entity {EntityType}/{EntityId}, CorrelationId {CorrelationId}.",
                action, result, entityType, entityId, correlationId);
        }
    }
}
