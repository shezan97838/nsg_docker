using Microsoft.Extensions.Logging;
using NSG.Mobile.Application.Common;
using NSG.Mobile.Application.Geography.DTOs;
using NSG.Mobile.Application.Geography.Mappings;
using NSG.Mobile.Application.Interfaces.Repositories;
using NSG.Mobile.Application.Interfaces.Services;
using NSG.Mobile.Domain.Common;
using NSG.Mobile.Domain.Constants;
using NSG.Mobile.Domain.Entities;
using NSG.Mobile.Domain.Enums;

namespace NSG.Mobile.Application.Geography.Services;

/// <summary>Management of States and Union Territories.</summary>
public interface IStateService
{
    Task<Result<PaginatedResponse<StateResponseDto>>> GetPagedAsync(StateListQuery query, CancellationToken cancellationToken);
    Task<Result<StateResponseDto>> GetByIdAsync(int id, CancellationToken cancellationToken);
    Task<Result<StateResponseDto>> CreateAsync(CreateStateRequestDto request, CancellationToken cancellationToken);
    Task<Result<StateResponseDto>> UpdateAsync(int id, UpdateStateRequestDto request, CancellationToken cancellationToken);
    Task<Result> DeleteAsync(int id, CancellationToken cancellationToken);
}

internal static class GeographyPaging
{
    /// <summary>Clamps paging inputs using the shared pagination rules (max page size 100).</summary>
    public static (int PageNumber, int PageSize) Normalize(int pageNumber, int pageSize)
    {
        PaginationRequest p = new() { PageNumber = pageNumber, PageSize = pageSize };
        return (p.PageNumber, p.PageSize);
    }
}

public class StateService : IStateService
{
    private const string EntityType = "State";

    private readonly IStateRepository _states;
    private readonly IAuditService _audit;
    private readonly ICurrentUserService _currentUser;
    private readonly ILogger<StateService> _logger;

    public StateService(IStateRepository states, IAuditService audit, ICurrentUserService currentUser, ILogger<StateService> logger)
    {
        _states = states;
        _audit = audit;
        _currentUser = currentUser;
        _logger = logger;
    }

    public async Task<Result<PaginatedResponse<StateResponseDto>>> GetPagedAsync(StateListQuery query, CancellationToken cancellationToken)
    {
        (query.PageNumber, query.PageSize) = GeographyPaging.Normalize(query.PageNumber, query.PageSize);
        (IReadOnlyList<State> items, int total) = await _states.GetPagedAsync(query, cancellationToken);
        return Result.Success(new PaginatedResponse<StateResponseDto>(
            items.Select(s => s.ToDto()).ToList(), total, query.PageNumber, query.PageSize));
    }

    public async Task<Result<StateResponseDto>> GetByIdAsync(int id, CancellationToken cancellationToken)
    {
        State? state = await _states.GetByIdAsync(id, cancellationToken);
        return state == null
            ? Result.Failure<StateResponseDto>(ApiErrorCodes.NotFound, "State not found.")
            : Result.Success(state.ToDto());
    }

    public async Task<Result<StateResponseDto>> CreateAsync(CreateStateRequestDto request, CancellationToken cancellationToken)
    {
        if (_currentUser.HasUnusableAdminId())
            return Result.Failure<StateResponseDto>(ApiErrorCodes.Unauthorized, AdminActorExtensions.AdminNotIdentifiedMessage);

        State state = request.ToEntity(IndianStandardTime.Now);

        Result? conflict = await CheckConflictsAsync(state.Name, state.Code, null, cancellationToken);
        if (conflict != null)
            return Result.Failure<StateResponseDto>(conflict.ErrorCode!, conflict.ErrorMessage!);

        await _states.AddAsync(state, cancellationToken);
        await AuditAsync("StateCreated", state.Id, cancellationToken);
        return Result.Success(state.ToDto());
    }

    public async Task<Result<StateResponseDto>> UpdateAsync(int id, UpdateStateRequestDto request, CancellationToken cancellationToken)
    {
        if (_currentUser.HasUnusableAdminId())
            return Result.Failure<StateResponseDto>(ApiErrorCodes.Unauthorized, AdminActorExtensions.AdminNotIdentifiedMessage);

        State? state = await _states.GetByIdAsync(id, cancellationToken);
        if (state == null)
            return Result.Failure<StateResponseDto>(ApiErrorCodes.NotFound, "State not found.");

        request.ApplyTo(state, IndianStandardTime.Now);

        Result? conflict = await CheckConflictsAsync(state.Name, state.Code, id, cancellationToken);
        if (conflict != null)
            return Result.Failure<StateResponseDto>(conflict.ErrorCode!, conflict.ErrorMessage!);

        await _states.UpdateAsync(state, cancellationToken);
        await AuditAsync("StateUpdated", id, cancellationToken);
        return Result.Success(state.ToDto());
    }

    public async Task<Result> DeleteAsync(int id, CancellationToken cancellationToken)
    {
        if (_currentUser.HasUnusableAdminId())
            return Result.Failure(ApiErrorCodes.Unauthorized, AdminActorExtensions.AdminNotIdentifiedMessage);

        State? state = await _states.GetByIdAsync(id, cancellationToken);
        if (state == null)
            return Result.Failure(ApiErrorCodes.NotFound, "State not found.");

        await _states.DeleteAsync(state, cancellationToken);
        await AuditAsync("StateDeleted", id, cancellationToken);
        return Result.Success();
    }

    private async Task<Result?> CheckConflictsAsync(string name, string? code, int? excludeId, CancellationToken cancellationToken)
    {
        if (code != null && await _states.CodeExistsAsync(code, excludeId, cancellationToken))
            return Result.Failure(ApiErrorCodes.Conflict, $"A state/UT with code '{code}' already exists.");

        if (await _states.NameExistsAsync(name, excludeId, cancellationToken))
            return Result.Failure(ApiErrorCodes.Conflict, "A state/UT with this name already exists.");

        return null;
    }

    private async Task AuditAsync(string operation, int id, CancellationToken cancellationToken)
    {
        await _audit.LogAsync(AuditAction.AdminAction, DomainConstants.AuditResults.Success,
            userId: _currentUser.AuditUserId(), entityType: EntityType, entityId: id.ToString(),
            correlationId: _currentUser.CorrelationId,
            additionalData: $"{{\"operation\":\"{operation}\"}}",
            cancellationToken: cancellationToken);
        _logger.LogInformation("{Operation} state {StateId} by admin {AdminId}", operation, id, _currentUser.AuditUserId());
    }
}
