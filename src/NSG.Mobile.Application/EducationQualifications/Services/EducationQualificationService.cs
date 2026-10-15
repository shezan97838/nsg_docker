using Microsoft.Extensions.Logging;
using NSG.Mobile.Application.Common;
using NSG.Mobile.Application.EducationQualifications.DTOs;
using NSG.Mobile.Application.EducationQualifications.Mappings;
using NSG.Mobile.Application.Interfaces.Repositories;
using NSG.Mobile.Application.Interfaces.Services;
using NSG.Mobile.Domain.Common;
using NSG.Mobile.Domain.Constants;
using NSG.Mobile.Domain.Entities;
using NSG.Mobile.Domain.Enums;

namespace NSG.Mobile.Application.EducationQualifications.Services;

/// <summary>Admin management of education qualifications. "Delete" deactivates; nothing is hard-deleted.</summary>
public interface IEducationQualificationService
{
    /// <summary>Data is null (with a message) when nothing matches, so the API never answers with an empty list.</summary>
    Task<Result<ApiResult<PaginatedResponse<EducationQualificationResponseDto>>>> GetPagedAsync(int pageNumber, int pageSize, bool? isActive, string? search, CancellationToken cancellationToken);
    Task<Result<EducationQualificationResponseDto>> GetByIdAsync(int id, CancellationToken cancellationToken);
    Task<Result<ApiResult<EducationQualificationResponseDto>>> CreateAsync(CreateEducationQualificationRequestDto request, CancellationToken cancellationToken);
    Task<Result<ApiResult<EducationQualificationResponseDto>>> UpdateAsync(int id, UpdateEducationQualificationRequestDto request, CancellationToken cancellationToken);
    Task<Result<ApiMessageResponse>> DeactivateAsync(int id, CancellationToken cancellationToken);
}

public class EducationQualificationService : IEducationQualificationService
{
    private const string EntityType = "EducationQualification";
    private const string NotFoundMessage = "Education qualification not found.";
    private const string NoneFoundMessage = "No education qualifications found.";
    private const string DuplicateMessage = "An education qualification with this course name already exists.";
    private const string InUseMessage = "Education qualification is used by personnel records and cannot be deactivated.";
    private const string CreatedMessage = "Education qualification created successfully.";
    private const string UpdatedMessage = "Education qualification updated successfully.";
    private const string DeactivatedMessage = "Education qualification deactivated successfully.";
    private const string AlreadyInactiveMessage = "Education qualification is already inactive.";

    private readonly IEducationQualificationRepository _repository;
    private readonly IAuditService _auditService;
    private readonly ICurrentUserService _currentUserService;
    private readonly ILogger<EducationQualificationService> _logger;

    public EducationQualificationService(
        IEducationQualificationRepository repository,
        IAuditService auditService,
        ICurrentUserService currentUserService,
        ILogger<EducationQualificationService> logger)
    {
        _repository = repository;
        _auditService = auditService;
        _currentUserService = currentUserService;
        _logger = logger;
    }

    public async Task<Result<ApiResult<PaginatedResponse<EducationQualificationResponseDto>>>> GetPagedAsync(
        int pageNumber, int pageSize, bool? isActive, string? search, CancellationToken cancellationToken)
    {
        (IReadOnlyList<EducationQualification> items, int total) =
            await _repository.GetPagedAsync(pageNumber, pageSize, isActive, search, cancellationToken);

        if (items.Count == 0)
            return Result.Success(new ApiResult<PaginatedResponse<EducationQualificationResponseDto>>(NoneFoundMessage, null));

        PaginatedResponse<EducationQualificationResponseDto> page =
            new(items.Select(i => i.ToDto()).ToList(), total, pageNumber, pageSize);
        return Result.Success(new ApiResult<PaginatedResponse<EducationQualificationResponseDto>>(string.Empty, page));
    }

    public async Task<Result<EducationQualificationResponseDto>> GetByIdAsync(int id, CancellationToken cancellationToken)
    {
        EducationQualification? entity = await _repository.GetByIdAsync(id, cancellationToken);
        return entity == null
            ? Result.Failure<EducationQualificationResponseDto>(ApiErrorCodes.NotFound, NotFoundMessage)
            : Result.Success(entity.ToDto());
    }

    public async Task<Result<ApiResult<EducationQualificationResponseDto>>> CreateAsync(
        CreateEducationQualificationRequestDto request, CancellationToken cancellationToken)
    {
        string name = request.CourseName.Trim();

        // Pre-check gives a clear message; the repository also maps a unique-index violation (race) to the same outcome.
        if (await _repository.NameExistsAsync(name, null, cancellationToken))
            return Result.Failure<ApiResult<EducationQualificationResponseDto>>(ApiErrorCodes.Conflict, DuplicateMessage);

        EducationQualification entity = new() { CourseName = name, IsActive = request.IsActive, CreatedAtIst = IndianStandardTime.Now };

        if (!await _repository.AddAsync(entity, cancellationToken))
            return Result.Failure<ApiResult<EducationQualificationResponseDto>>(ApiErrorCodes.Conflict, DuplicateMessage);

        await AuditAsync("EducationQualificationCreated", entity.Id, cancellationToken);
        return Result.Success(new ApiResult<EducationQualificationResponseDto>(CreatedMessage, entity.ToDto()));
    }

    public async Task<Result<ApiResult<EducationQualificationResponseDto>>> UpdateAsync(
        int id, UpdateEducationQualificationRequestDto request, CancellationToken cancellationToken)
    {
        EducationQualification? entity = await _repository.GetByIdAsync(id, cancellationToken);
        if (entity == null)
            return Result.Failure<ApiResult<EducationQualificationResponseDto>>(ApiErrorCodes.NotFound, NotFoundMessage);

        string name = request.CourseName.Trim();
        bool newActive = request.IsActive ?? entity.IsActive;

        // Same rule as DELETE: a qualification used by personnel cannot be deactivated (renaming it is fine).
        if (entity.IsActive && !newActive && await _repository.IsInUseAsync(id, cancellationToken))
            return Result.Failure<ApiResult<EducationQualificationResponseDto>>(ApiErrorCodes.Conflict, InUseMessage);

        if (await _repository.NameExistsAsync(name, id, cancellationToken))
            return Result.Failure<ApiResult<EducationQualificationResponseDto>>(ApiErrorCodes.Conflict, DuplicateMessage);

        entity.CourseName = name;
        entity.IsActive = newActive;

        if (!await _repository.UpdateAsync(entity, cancellationToken))
            return Result.Failure<ApiResult<EducationQualificationResponseDto>>(ApiErrorCodes.Conflict, DuplicateMessage);

        await AuditAsync("EducationQualificationUpdated", id, cancellationToken);
        return Result.Success(new ApiResult<EducationQualificationResponseDto>(UpdatedMessage, entity.ToDto()));
    }

    public async Task<Result<ApiMessageResponse>> DeactivateAsync(int id, CancellationToken cancellationToken)
    {
        EducationQualification? entity = await _repository.GetByIdAsync(id, cancellationToken);
        if (entity == null)
            return Result.Failure<ApiMessageResponse>(ApiErrorCodes.NotFound, NotFoundMessage);

        // Idempotent: an already inactive qualification stays inactive.
        if (!entity.IsActive)
            return Result.Success(new ApiMessageResponse(AlreadyInactiveMessage));

        if (await _repository.IsInUseAsync(id, cancellationToken))
            return Result.Failure<ApiMessageResponse>(ApiErrorCodes.Conflict, InUseMessage);

        entity.IsActive = false;
        await _repository.UpdateAsync(entity, cancellationToken);
        await AuditAsync("EducationQualificationDeactivated", id, cancellationToken);
        return Result.Success(new ApiMessageResponse(DeactivatedMessage));
    }

    private async Task AuditAsync(string operation, int id, CancellationToken cancellationToken)
    {
        await _auditService.LogAsync(AuditAction.AdminAction, DomainConstants.AuditResults.Success,
            userId: _currentUserService.UserId, entityType: EntityType, entityId: id.ToString(),
            correlationId: _currentUserService.CorrelationId,
            additionalData: $"{{\"operation\":\"{operation}\"}}",
            cancellationToken: cancellationToken);

        _logger.LogInformation("{Operation} education qualification {EntityId} by admin {AdminId}", operation, id, _currentUserService.UserId);
    }
}
