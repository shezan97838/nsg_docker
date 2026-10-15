using Microsoft.Extensions.Logging;
using NSG.Mobile.Application.Common;
using NSG.Mobile.Application.Interfaces.Repositories;
using NSG.Mobile.Application.Interfaces.Services;
using NSG.Mobile.Application.Lookups.DTOs;
using NSG.Mobile.Application.Lookups.Mappings;
using NSG.Mobile.Domain.Common;
using NSG.Mobile.Domain.Constants;
using NSG.Mobile.Domain.Entities;
using NSG.Mobile.Domain.Enums;

namespace NSG.Mobile.Application.Lookups.Services;

/// <summary>Admin management of NSG units (CRUD + dropdown lookup).</summary>
public interface INsgUnitService
{
    Task<Result<PaginatedResponse<LookupResponseDto>>> GetPagedAsync(int pageNumber, int pageSize, bool? isActive, string? search, CancellationToken cancellationToken);
    Task<Result<IReadOnlyList<LookupDropdownDto>>> GetDropdownAsync(CancellationToken cancellationToken);
    Task<Result<LookupResponseDto>> GetByIdAsync(int id, CancellationToken cancellationToken);
    Task<Result<LookupResponseDto>> CreateAsync(CreateLookupRequestDto request, CancellationToken cancellationToken);
    Task<Result<LookupResponseDto>> UpdateAsync(int id, UpdateLookupRequestDto request, CancellationToken cancellationToken);
    Task<Result> DeleteAsync(int id, CancellationToken cancellationToken);
}

public class NsgUnitService : INsgUnitService
{
    private const string EntityType = "NsgUnit";
    private const string NotFoundMessage = "NSG unit not found.";
    private const string DuplicateMessage = "A NSG unit with this name already exists.";
    private const string InUseMessage = "NSG unit is referenced by personnel records and cannot be deleted. Deactivate it instead.";

    private readonly INsgUnitRepository _repository;
    private readonly IAuditService _auditService;
    private readonly ICurrentUserService _currentUserService;
    private readonly ILogger<NsgUnitService> _logger;

    public NsgUnitService(
        INsgUnitRepository repository,
        IAuditService auditService,
        ICurrentUserService currentUserService,
        ILogger<NsgUnitService> logger)
    {
        _repository = repository;
        _auditService = auditService;
        _currentUserService = currentUserService;
        _logger = logger;
    }

    public async Task<Result<PaginatedResponse<LookupResponseDto>>> GetPagedAsync(
        int pageNumber, int pageSize, bool? isActive, string? search, CancellationToken cancellationToken)
    {
        (IReadOnlyList<NsgUnit> items, int total) =
            await _repository.GetPagedAsync(pageNumber, pageSize, isActive, search, cancellationToken);

        return Result.Success(new PaginatedResponse<LookupResponseDto>(
            items.Select(i => i.ToDto()).ToList(), total, pageNumber, pageSize));
    }

    public async Task<Result<IReadOnlyList<LookupDropdownDto>>> GetDropdownAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<NsgUnit> items = await _repository.GetActiveAsync(cancellationToken);
        IReadOnlyList<LookupDropdownDto> dropdown = items.Select(i => i.ToDropdownDto()).ToList();
        return Result.Success(dropdown);
    }

    public async Task<Result<LookupResponseDto>> GetByIdAsync(int id, CancellationToken cancellationToken)
    {
        NsgUnit? entity = await _repository.GetByIdAsync(id, cancellationToken);
        return entity == null
            ? Result.Failure<LookupResponseDto>(ApiErrorCodes.NotFound, NotFoundMessage)
            : Result.Success(entity.ToDto());
    }

    public async Task<Result<LookupResponseDto>> CreateAsync(CreateLookupRequestDto request, CancellationToken cancellationToken)
    {
        NsgUnit entity = request.ToEntity<NsgUnit>(IndianStandardTime.Now);

        // Pre-check gives a clear message; the repository also maps a unique-index violation (race) to the same outcome.
        if (await _repository.NameExistsAsync(entity.Name, null, cancellationToken)
            || !await _repository.AddAsync(entity, cancellationToken))
            return Result.Failure<LookupResponseDto>(ApiErrorCodes.Conflict, DuplicateMessage);

        await AuditAsync("NsgUnitCreated", entity.Id, cancellationToken);
        return Result.Success(entity.ToDto());
    }

    public async Task<Result<LookupResponseDto>> UpdateAsync(int id, UpdateLookupRequestDto request, CancellationToken cancellationToken)
    {
        NsgUnit? entity = await _repository.GetByIdAsync(id, cancellationToken);
        if (entity == null)
            return Result.Failure<LookupResponseDto>(ApiErrorCodes.NotFound, NotFoundMessage);

        if (await _repository.NameExistsAsync(request.Name.Trim(), id, cancellationToken))
            return Result.Failure<LookupResponseDto>(ApiErrorCodes.Conflict, DuplicateMessage);

        request.ApplyTo(entity, IndianStandardTime.Now);
        if (!await _repository.UpdateAsync(entity, cancellationToken))
            return Result.Failure<LookupResponseDto>(ApiErrorCodes.Conflict, DuplicateMessage);

        await AuditAsync("NsgUnitUpdated", id, cancellationToken);
        return Result.Success(entity.ToDto());
    }

    public async Task<Result> DeleteAsync(int id, CancellationToken cancellationToken)
    {
        NsgUnit? entity = await _repository.GetByIdAsync(id, cancellationToken);
        if (entity == null)
            return Result.Failure(ApiErrorCodes.NotFound, NotFoundMessage);

        // Pre-check gives a clear message; the repository also maps an FK violation (race) to the same outcome.
        if (await _repository.IsInUseAsync(id, cancellationToken)
            || !await _repository.DeleteAsync(entity, cancellationToken))
            return Result.Failure(ApiErrorCodes.Conflict, InUseMessage);

        await AuditAsync("NsgUnitDeleted", id, cancellationToken);
        return Result.Success();
    }

    private async Task AuditAsync(string operation, int id, CancellationToken cancellationToken)
    {
        await _auditService.LogAsync(AuditAction.AdminAction, DomainConstants.AuditResults.Success,
            userId: _currentUserService.UserId, entityType: EntityType, entityId: id.ToString(),
            correlationId: _currentUserService.CorrelationId,
            additionalData: $"{{\"operation\":\"{operation}\"}}",
            cancellationToken: cancellationToken);

        _logger.LogInformation("{Operation} NSG unit {EntityId} by admin {AdminId}", operation, id, _currentUserService.UserId);
    }
}
