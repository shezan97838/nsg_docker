using System.Text.Json;
using NSG.Mobile.Application.Common;
using NSG.Mobile.Application.Grievances.DTOs;
using NSG.Mobile.Application.Grievances.Mappings;
using NSG.Mobile.Application.Interfaces.Repositories;
using NSG.Mobile.Application.Interfaces.Services;
using NSG.Mobile.Domain.Common;
using NSG.Mobile.Domain.Constants;
using NSG.Mobile.Domain.Entities;
using NSG.Mobile.Domain.Enums;

namespace NSG.Mobile.Application.Grievances.Services;

/// <summary>Grievance categories: the dropdown for employees, and create/read/update/soft-delete for Grievance Admins. Rows are never physically deleted.</summary>
public interface IGrievanceCategoryService
{
    /// <summary>Active, non-deleted categories by name, for the submission form.</summary>
    Task<Result<IReadOnlyList<GrievanceCategoryDto>>> GetActiveAsync(CancellationToken cancellationToken);

    Task<Result<PaginatedResponse<GrievanceCategoryAdminDto>>> GetPagedAsync(int pageNumber, int pageSize, bool? isActive, string? search, CancellationToken cancellationToken);
    Task<Result<GrievanceCategoryAdminDto>> GetByIdAsync(int id, CancellationToken cancellationToken);
    Task<Result<ApiResult<GrievanceCategoryAdminDto>>> CreateAsync(SaveGrievanceCategoryRequestDto request, CancellationToken cancellationToken);
    Task<Result<ApiResult<GrievanceCategoryAdminDto>>> UpdateAsync(int id, SaveGrievanceCategoryRequestDto request, CancellationToken cancellationToken);

    /// <summary>Soft delete. Repeating the request answers with a message instead of failing; grievances keep their category.</summary>
    Task<Result<ApiMessageResponse>> DeleteAsync(int id, CancellationToken cancellationToken);
}

public class GrievanceCategoryService : IGrievanceCategoryService
{
    private readonly IGrievanceRepository _repository;
    private readonly IAuditService _audit;
    private readonly ICurrentUserService _currentUser;
    private readonly TimeProvider _time;

    public GrievanceCategoryService(IGrievanceRepository repository, IAuditService audit, ICurrentUserService currentUser, TimeProvider time)
    {
        _repository = repository;
        _audit = audit;
        _currentUser = currentUser;
        _time = time;
    }

    public async Task<Result<IReadOnlyList<GrievanceCategoryDto>>> GetActiveAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<GrievanceCategory> categories = await _repository.GetActiveCategoriesAsync(cancellationToken);
        return Result.Success<IReadOnlyList<GrievanceCategoryDto>>(categories.Select(c => c.ToDto()).ToList());
    }

    public async Task<Result<PaginatedResponse<GrievanceCategoryAdminDto>>> GetPagedAsync(
        int pageNumber, int pageSize, bool? isActive, string? search, CancellationToken cancellationToken)
    {
        string? term = string.IsNullOrWhiteSpace(search) ? null : search.Trim();
        (IReadOnlyList<GrievanceCategory> items, int total) = await _repository.GetCategoriesPagedAsync(pageNumber, pageSize, isActive, term, cancellationToken);
        return Result.Success(new PaginatedResponse<GrievanceCategoryAdminDto>(items.Select(c => c.ToAdminDto()).ToList(), total, pageNumber, pageSize));
    }

    public async Task<Result<GrievanceCategoryAdminDto>> GetByIdAsync(int id, CancellationToken cancellationToken)
    {
        GrievanceCategory? category = await _repository.GetCategoryByIdAsync(id, cancellationToken);
        return category == null || category.IsDeleted
            ? Result.Failure<GrievanceCategoryAdminDto>(ApiErrorCodes.NotFound, GrievanceMessages.CategoryNotFound)
            : Result.Success(category.ToAdminDto());
    }

    public async Task<Result<ApiResult<GrievanceCategoryAdminDto>>> CreateAsync(SaveGrievanceCategoryRequestDto request, CancellationToken cancellationToken)
    {
        Result<string> name = ValidName(request);
        if (!name.IsSuccess)
            return Result.Failure<ApiResult<GrievanceCategoryAdminDto>>(name.ErrorCode!, name.ErrorMessage!);

        GrievanceCategory category = new()
        {
            GrievanceName = name.Data!,
            DepartmentId = request.DepartmentId,
            IsActive = request.IsActive ?? true,
            IsDeleted = false,
            CreatedAtIst = NowIst(),
            CreatedBy = _currentUser.AdminProfileId
        };

        if (await _repository.AddCategoryAsync(category, cancellationToken) != CategoryWriteOutcome.Saved)
            return Result.Failure<ApiResult<GrievanceCategoryAdminDto>>(ApiErrorCodes.Conflict, GrievanceMessages.DuplicateCategory);

        await AuditAsync(category.Id, "created", cancellationToken);
        return Result.Success(new ApiResult<GrievanceCategoryAdminDto>("Grievance category created successfully.", category.ToAdminDto()));
    }

    public async Task<Result<ApiResult<GrievanceCategoryAdminDto>>> UpdateAsync(int id, SaveGrievanceCategoryRequestDto request, CancellationToken cancellationToken)
    {
        Result<string> name = ValidName(request);
        if (!name.IsSuccess)
            return Result.Failure<ApiResult<GrievanceCategoryAdminDto>>(name.ErrorCode!, name.ErrorMessage!);
        if (request.IsActive == null)
            return Result.Failure<ApiResult<GrievanceCategoryAdminDto>>(ApiErrorCodes.ValidationFailed, "isActive is required.");

        CategoryWriteOutcome outcome = await _repository.UpdateCategoryAsync(
            id, name.Data!, request.DepartmentId, request.IsActive.Value, _currentUser.AdminProfileId, NowIst(), cancellationToken);

        switch (outcome)
        {
            case CategoryWriteOutcome.NotFound:
                return Result.Failure<ApiResult<GrievanceCategoryAdminDto>>(ApiErrorCodes.NotFound, GrievanceMessages.CategoryNotFound);
            case CategoryWriteOutcome.DuplicateName:
                return Result.Failure<ApiResult<GrievanceCategoryAdminDto>>(ApiErrorCodes.Conflict, GrievanceMessages.DuplicateCategory);
        }

        await AuditAsync(id, "updated", cancellationToken);
        GrievanceCategory? saved = await _repository.GetCategoryByIdAsync(id, cancellationToken);
        return saved == null
            ? Result.Failure<ApiResult<GrievanceCategoryAdminDto>>(ApiErrorCodes.NotFound, GrievanceMessages.CategoryNotFound)
            : Result.Success(new ApiResult<GrievanceCategoryAdminDto>("Grievance category updated successfully.", saved.ToAdminDto()));
    }

    public async Task<Result<ApiMessageResponse>> DeleteAsync(int id, CancellationToken cancellationToken)
    {
        CategoryDeleteOutcome outcome = await _repository.SoftDeleteCategoryAsync(id, _currentUser.AdminProfileId, NowIst(), cancellationToken);

        switch (outcome)
        {
            case CategoryDeleteOutcome.NotFound:
                return Result.Failure<ApiMessageResponse>(ApiErrorCodes.NotFound, GrievanceMessages.CategoryNotFound);
            case CategoryDeleteOutcome.AlreadyDeleted:
                return Result.Success(new ApiMessageResponse("Grievance category is already deleted."));
        }

        await AuditAsync(id, "deleted", cancellationToken);
        return Result.Success(new ApiMessageResponse("Grievance category deleted successfully."));
    }

    private static Result<string> ValidName(SaveGrievanceCategoryRequestDto request)
    {
        string name = (request.Name ?? string.Empty).Trim();
        return name.Length == 0 || name.Length > GrievanceLimits.CategoryNameMaxLength
            ? Result.Failure<string>(ApiErrorCodes.ValidationFailed, $"Category name is required and must not exceed {GrievanceLimits.CategoryNameMaxLength} characters.")
            : Result.Success(name);
    }

    private async Task AuditAsync(int categoryId, string change, CancellationToken cancellationToken)
        => await _audit.LogAsync(AuditAction.GrievanceCategoryChanged, DomainConstants.AuditResults.Success,
            entityType: nameof(GrievanceCategory), entityId: categoryId.ToString(), correlationId: _currentUser.CorrelationId,
            additionalData: JsonSerializer.Serialize(new { actorType = nameof(GrievanceActorType.Admin), actorId = _currentUser.AdminProfileId, change }),
            cancellationToken: cancellationToken);

    private DateTime NowIst() => IndianStandardTime.ConvertFromUtc(_time.GetUtcNow().UtcDateTime);
}
