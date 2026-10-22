using System.Text.Json;
using Microsoft.Extensions.Logging;
using NSG.Mobile.Application.Common;
using NSG.Mobile.Application.Grievances.DTOs;
using NSG.Mobile.Application.Interfaces.Infrastructure;
using NSG.Mobile.Application.Interfaces.Services;
using NSG.Mobile.Domain.Constants;
using NSG.Mobile.Domain.Entities;
using NSG.Mobile.Domain.Enums;

namespace NSG.Mobile.Application.Grievances.Services;

/// <summary>
/// Opens one stored document for download. The caller has already proven access to the grievance (ownership for an employee, the
/// Grievance Admin policy for an admin). Every download is logged and audited with who downloaded which grievance document.
/// </summary>
public sealed class GrievanceDocumentAccess
{
    private static readonly HashSet<string> ServedContentTypes = new(StringComparer.Ordinal) { "application/pdf", "image/jpeg" };

    private readonly IFileStorageService _storage;
    private readonly IStorageAvailability _availability;
    private readonly IAuditService _audit;
    private readonly ICurrentUserService _currentUser;
    private readonly ILogger<GrievanceDocumentAccess> _logger;

    public GrievanceDocumentAccess(IFileStorageService storage, IStorageAvailability availability, IAuditService audit, ICurrentUserService currentUser, ILogger<GrievanceDocumentAccess> logger)
    {
        _storage = storage;
        _availability = availability;
        _audit = audit;
        _currentUser = currentUser;
        _logger = logger;
    }

    public async Task<Result<GrievanceDocumentContent>> OpenAsync(
        Grievance grievance, int index, GrievanceActorType actor, long actorId, CancellationToken cancellationToken)
    {
        List<GrievanceDocumentRecord> documents = GrievanceDocumentRules.ParseDocuments(grievance.DocumentsJson);
        if (index < 0 || index >= documents.Count)
            return Result.Failure<GrievanceDocumentContent>(ApiErrorCodes.NotFound, GrievanceMessages.DocumentNotFound);

        GrievanceDocumentRecord document = documents[index];

        // Outside Development only private R2 may serve documents; a host without it must never read the local disk.
        if (!_availability.IsAvailable)
            return Result.Failure<GrievanceDocumentContent>(GrievanceErrorCodes.StorageUnavailable, GrievanceMessages.StorageUnavailable);

        FileStorageResult? file;
        try
        {
            file = await _storage.GetAsync(document.Key, cancellationToken);
        }
        catch (FileStorageUnavailableException)
        {
            return Result.Failure<GrievanceDocumentContent>(GrievanceErrorCodes.StorageUnavailable, GrievanceMessages.StorageUnavailable);
        }

        if (file == null)
        {
            _logger.LogWarning("Grievance {GrievanceId} document {Index} is recorded but missing in storage", grievance.Id, index);
            return Result.Failure<GrievanceDocumentContent>(ApiErrorCodes.NotFound, GrievanceMessages.DocumentFileMissing);
        }

        _logger.LogInformation("Grievance document downloaded: grievance {GrievanceId}, document {Index}, by {ActorType} {ActorId}", grievance.Id, index, actor, actorId);
        await _audit.LogAsync(AuditAction.DocumentDownloaded, DomainConstants.AuditResults.Success,
            entityType: nameof(Grievance), entityId: grievance.Id.ToString(), correlationId: _currentUser.CorrelationId,
            additionalData: JsonSerializer.Serialize(new { actorType = actor.ToString(), actorId, documentIndex = index }),
            cancellationToken: cancellationToken);

        // The stored, already validated content type is served; anything unexpected is sent as an opaque download.
        string contentType = ServedContentTypes.Contains(document.ContentType) ? document.ContentType : "application/octet-stream";
        return Result.Success(new GrievanceDocumentContent(file.Stream, contentType, document.Name));
    }
}
