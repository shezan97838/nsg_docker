using System.Text.Json;
using Microsoft.Extensions.Logging;
using NSG.Mobile.Application.Common;
using NSG.Mobile.Application.Grievances.DTOs;
using NSG.Mobile.Application.Grievances.Mappings;
using NSG.Mobile.Application.Grievances.Validators;
using NSG.Mobile.Application.Interfaces.Infrastructure;
using NSG.Mobile.Application.Interfaces.Repositories;
using NSG.Mobile.Application.Interfaces.Services;
using NSG.Mobile.Domain.Common;
using NSG.Mobile.Domain.Constants;
using NSG.Mobile.Domain.Entities;
using NSG.Mobile.Domain.Enums;

namespace NSG.Mobile.Application.Grievances.Services;

/// <summary>
/// Grievances of the signed-in mobile user (SOW §3.8). Any mobile access token may use it, whatever the user's mobile role; every
/// operation is scoped to the profile id in the token, so a user can never reach another person's grievance.
/// </summary>
public interface IGrievanceService
{
    /// <summary>
    /// The one way to raise a grievance: validates category and description, checks and stores the optional PDF/JPG attachments in private storage,
    /// assigns the ticket number and writes the grievance with its documents and first history row in one step.
    /// Nothing stays in storage if the grievance cannot be recorded.
    /// </summary>
    Task<Result<ApiResult<GrievanceDetailDto>>> CreateAsync(CreateGrievanceRequestDto request, IReadOnlyList<GrievanceUploadFile> files, CancellationToken cancellationToken);

    Task<Result<PaginatedResponse<GrievanceSummaryDto>>> GetMineAsync(int pageNumber, int pageSize, GrievanceStatus? status, CancellationToken cancellationToken);

    /// <summary>Detail with the status timeline. A grievance that is not the caller's is reported as not found.</summary>
    Task<Result<GrievanceDetailDto>> GetMineByIdAsync(long id, CancellationToken cancellationToken);

    Task<Result<GrievanceDocumentContent>> OpenMyDocumentAsync(long id, int index, CancellationToken cancellationToken);

    /// <summary>Opens the resolution file the admin uploaded for the caller's own grievance. Someone else's grievance, or one without a resolution file, is reported as not found.</summary>
    Task<Result<GrievanceDocumentContent>> OpenMyResolutionDocumentAsync(long id, CancellationToken cancellationToken);
}

public class GrievanceService : IGrievanceService
{
    private readonly IGrievanceRepository _repository;
    private readonly IFileStorageService _storage;
    private readonly IStorageAvailability _availability;
    private readonly GrievanceDocumentAccess _documentAccess;
    private readonly IAuditService _audit;
    private readonly ICurrentUserService _currentUser;
    private readonly TimeProvider _time;
    private readonly ILogger<GrievanceService> _logger;

    public GrievanceService(IGrievanceRepository repository, IFileStorageService storage, IStorageAvailability availability, GrievanceDocumentAccess documentAccess,
        IAuditService audit, ICurrentUserService currentUser, TimeProvider time, ILogger<GrievanceService> logger)
    {
        _repository = repository;
        _storage = storage;
        _availability = availability;
        _documentAccess = documentAccess;
        _audit = audit;
        _currentUser = currentUser;
        _time = time;
        _logger = logger;
    }

    public async Task<Result<ApiResult<GrievanceDetailDto>>> CreateAsync(
        CreateGrievanceRequestDto request, IReadOnlyList<GrievanceUploadFile> files, CancellationToken cancellationToken)
    {
        long? profileId = _currentUser.ProfileId;
        if (profileId == null)
            return Result.Failure<ApiResult<GrievanceDetailDto>>(ApiErrorCodes.Unauthorized, GrievanceMessages.IdentityMissing);

        FluentValidation.Results.ValidationResult validation = new CreateGrievanceRequestValidator().Validate(request);
        if (!validation.IsValid)
            return Result.Failure<ApiResult<GrievanceDetailDto>>(ApiErrorCodes.ValidationFailed,
                string.Join(" ", validation.Errors.Select(e => e.ErrorMessage).Distinct()));

        GrievanceCategory? category = await _repository.GetCategoryByIdAsync(request.CategoryId, cancellationToken);
        if (category == null || category.IsDeleted || !category.IsActive)
            return Result.Failure<ApiResult<GrievanceDetailDto>>(ApiErrorCodes.ValidationFailed, "The selected grievance category is not available.");

        if (files.Count > GrievanceLimits.MaxFiles)
            return Result.Failure<ApiResult<GrievanceDetailDto>>(ApiErrorCodes.ValidationFailed,
                $"A grievance can have at most {GrievanceLimits.MaxFiles} documents.");

        // Outside Development only private R2 may receive documents; a host without it must never write to the local disk.
        if (files.Count > 0 && !_availability.IsAvailable)
            return Result.Failure<ApiResult<GrievanceDetailDto>>(GrievanceErrorCodes.StorageUnavailable, GrievanceMessages.StorageUnavailable);

        List<PreparedDocument> prepared = new();
        List<string> storedKeys = new();
        try
        {
            // Phase 1: everything is checked BEFORE anything is sent to storage.
            foreach (GrievanceUploadFile file in files)
            {
                Result<PreparedDocument> checkedFile = await GrievanceUploadPreparer.PrepareAsync(file, GrievanceLimits.Upload, cancellationToken);
                if (!checkedFile.IsSuccess)
                    return Result.Failure<ApiResult<GrievanceDetailDto>>(checkedFile.ErrorCode!, checkedFile.ErrorMessage!);
                prepared.Add(checkedFile.Data!);
            }

            // Phase 2: store. The grievance row is written only after every object exists.
            List<GrievanceDocumentRecord> records = new();
            foreach (PreparedDocument document in prepared)
            {
                string key;
                try
                {
                    key = await _storage.StoreAsync(document.Content, document.Validated.SafeName, document.Validated.ContentType, GrievanceLimits.StorageCategory, cancellationToken);
                }
                catch (FileStorageUnavailableException)
                {
                    await RemoveOrphansAsync(storedKeys);
                    return Result.Failure<ApiResult<GrievanceDetailDto>>(GrievanceErrorCodes.StorageUnavailable, GrievanceMessages.StorageUnavailable);
                }
                catch
                {
                    await RemoveOrphansAsync(storedKeys);
                    throw;
                }

                storedKeys.Add(key);
                records.Add(new GrievanceDocumentRecord(key, document.Validated.SafeName, document.Validated.ContentType, document.Size, NowIst()));
            }

            // Phase 3: record. From here on the objects exist; if recording fails for any reason they are removed again.
            Grievance grievance = new()
            {
                CategoryId = category.Id,
                Subject = request.Subject.Trim(),
                GrievanceNote = request.Note.Trim(),
                DocumentsJson = records.Count == 0 ? null : GrievanceDocumentRules.SerializeDocuments(records),
                CurrentStatus = GrievanceStatus.Submitted,
                CreatedBy = profileId.Value,
                CreatedAtIst = NowIst(),
                AssignedTo = null
            };

            GrievanceCreateResult created;
            try
            {
                created = await _repository.CreateAsync(grievance, profileId.Value, GrievanceLimits.MaxTicketAttempts, cancellationToken);
            }
            catch
            {
                await RemoveOrphansAsync(storedKeys);
                throw;
            }

            if (created.Outcome != GrievanceCreateOutcome.Created || created.Grievance == null)
            {
                await RemoveOrphansAsync(storedKeys);
                _logger.LogError("Could not allocate a grievance ticket number after {Attempts} attempts", GrievanceLimits.MaxTicketAttempts);
                return Result.Failure<ApiResult<GrievanceDetailDto>>(GrievanceErrorCodes.TicketUnavailable,
                    "Your grievance could not be registered right now. Please try again.");
            }

            // Outside the try on purpose: once the grievance is saved the objects must stay, whatever happens to the audit writes.
            Grievance saved = created.Grievance;
            await _audit.LogAsync(AuditAction.GrievanceCreated, DomainConstants.AuditResults.Success,
                entityType: nameof(Grievance), entityId: saved.Id.ToString(), correlationId: _currentUser.CorrelationId,
                additionalData: JsonSerializer.Serialize(new { actorType = nameof(GrievanceActorType.Employee), actorId = profileId.Value, categoryId = category.Id, documents = records.Count }),
                cancellationToken: cancellationToken);
            for (int index = 0; index < records.Count; index++)
                await _audit.LogAsync(AuditAction.GrievanceDocumentUploaded, DomainConstants.AuditResults.Success,
                    entityType: nameof(Grievance), entityId: saved.Id.ToString(), correlationId: _currentUser.CorrelationId,
                    additionalData: JsonSerializer.Serialize(new { actorType = nameof(GrievanceActorType.Employee), actorId = profileId.Value, documentIndex = index, sizeBytes = records[index].SizeBytes }),
                    cancellationToken: cancellationToken);

            GrievanceStatusLog first = new()
            {
                FromStatus = null, ToStatus = GrievanceStatus.Submitted, ChangedBy = profileId.Value,
                ChangedByType = GrievanceActorType.Employee, ChangedAtIst = saved.CreatedAtIst
            };
            return Result.Success(new ApiResult<GrievanceDetailDto>(
                $"Grievance submitted successfully. Your ticket number is {saved.TicketNumber}.",
                saved.ToDetailDto(category.GrievanceName, new[] { first })));
        }
        finally
        {
            foreach (PreparedDocument document in prepared) document.Buffer?.Dispose();
        }
    }

    public async Task<Result<PaginatedResponse<GrievanceSummaryDto>>> GetMineAsync(
        int pageNumber, int pageSize, GrievanceStatus? status, CancellationToken cancellationToken)
    {
        long? profileId = _currentUser.ProfileId;
        if (profileId == null)
            return Result.Failure<PaginatedResponse<GrievanceSummaryDto>>(ApiErrorCodes.Unauthorized, GrievanceMessages.IdentityMissing);

        GrievanceListFilter filter = new(Status: status, CreatedBy: profileId.Value);
        (IReadOnlyList<Grievance> items, int total) = await _repository.GetPagedAsync(filter, pageNumber, pageSize, cancellationToken);

        return Result.Success(new PaginatedResponse<GrievanceSummaryDto>(
            items.Select(g => g.ToSummaryDto(g.Category?.GrievanceName ?? string.Empty)).ToList(), total, pageNumber, pageSize));
    }

    public async Task<Result<GrievanceDetailDto>> GetMineByIdAsync(long id, CancellationToken cancellationToken)
    {
        long? profileId = _currentUser.ProfileId;
        if (profileId == null)
            return Result.Failure<GrievanceDetailDto>(ApiErrorCodes.Unauthorized, GrievanceMessages.IdentityMissing);

        Grievance? grievance = await _repository.GetByIdAsync(id, profileId.Value, cancellationToken);
        if (grievance == null)
            return Result.Failure<GrievanceDetailDto>(ApiErrorCodes.NotFound, GrievanceMessages.GrievanceNotFound);

        IReadOnlyList<GrievanceStatusLog> timeline = await _repository.GetTimelineAsync(grievance.Id, cancellationToken);
        return Result.Success(grievance.ToDetailDto(grievance.Category?.GrievanceName ?? string.Empty, timeline));
    }

    public async Task<Result<GrievanceDocumentContent>> OpenMyDocumentAsync(long id, int index, CancellationToken cancellationToken)
    {
        long? profileId = _currentUser.ProfileId;
        if (profileId == null)
            return Result.Failure<GrievanceDocumentContent>(ApiErrorCodes.Unauthorized, GrievanceMessages.IdentityMissing);

        Grievance? grievance = await _repository.GetByIdAsync(id, profileId.Value, cancellationToken);
        if (grievance == null)
            return Result.Failure<GrievanceDocumentContent>(ApiErrorCodes.NotFound, GrievanceMessages.GrievanceNotFound);

        return await _documentAccess.OpenAsync(grievance, index, GrievanceActorType.Employee, profileId.Value, cancellationToken);
    }

    public async Task<Result<GrievanceDocumentContent>> OpenMyResolutionDocumentAsync(long id, CancellationToken cancellationToken)
    {
        long? profileId = _currentUser.ProfileId;
        if (profileId == null)
            return Result.Failure<GrievanceDocumentContent>(ApiErrorCodes.Unauthorized, GrievanceMessages.IdentityMissing);

        Grievance? grievance = await _repository.GetByIdAsync(id, profileId.Value, cancellationToken);
        if (grievance == null)
            return Result.Failure<GrievanceDocumentContent>(ApiErrorCodes.NotFound, GrievanceMessages.GrievanceNotFound);

        return await _documentAccess.OpenResolutionAsync(grievance, GrievanceActorType.Employee, profileId.Value, cancellationToken);
    }

    private async Task RemoveOrphansAsync(IReadOnlyCollection<string> keys)
    {
        foreach (string key in keys) await RemoveOrphanAsync(key);
    }

    private async Task RemoveOrphanAsync(string key)
    {
        // Not tied to the request token: the cleanup must still run when the request was cancelled.
        bool deleted = false;
        try { deleted = await _storage.DeleteAsync(key, CancellationToken.None); }
        catch (Exception ex) { _logger.LogError(ex, "Compensating delete of {StorageKey} threw", key); }
        if (!deleted) _logger.LogError("Orphaned grievance document {StorageKey} could not be removed and needs manual clean-up", key);
    }


    private DateTime NowIst() => IndianStandardTime.ConvertFromUtc(_time.GetUtcNow().UtcDateTime);
}
