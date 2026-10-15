using Microsoft.Extensions.Logging;
using NSG.Mobile.Application.Common;
using NSG.Mobile.Application.Interfaces.Repositories;
using NSG.Mobile.Application.Interfaces.Services;
using NSG.Mobile.Application.Trades.DTOs;
using NSG.Mobile.Application.Trades.Mappings;
using NSG.Mobile.Domain.Common;
using NSG.Mobile.Domain.Constants;
using NSG.Mobile.Domain.Entities;
using NSG.Mobile.Domain.Enums;

namespace NSG.Mobile.Application.Trades.Services;

/// <summary>Admin management of NSG trades. "Delete" deactivates; nothing is hard-deleted.</summary>
public interface INsgTradeService
{
    Task<Result<PaginatedResponse<NsgTradeResponseDto>>> GetPagedAsync(int pageNumber, int pageSize, bool? isActive, string? search, CancellationToken cancellationToken);
    Task<Result<NsgTradeResponseDto>> GetByIdAsync(int id, CancellationToken cancellationToken);
    Task<Result<NsgTradeResponseDto>> CreateAsync(CreateNsgTradeRequestDto request, CancellationToken cancellationToken);
    Task<Result<NsgTradeResponseDto>> UpdateAsync(int id, UpdateNsgTradeRequestDto request, CancellationToken cancellationToken);
    Task<Result> DeactivateAsync(int id, CancellationToken cancellationToken);
}

public class NsgTradeService : INsgTradeService
{
    private const string EntityType = "NsgTrade";
    private const string NotFoundMessage = "NSG trade not found.";
    private const string DuplicateMessage = "A NSG trade with this name already exists.";
    private const string InUseMessage = "NSG trade is used by personnel records and cannot be deactivated.";

    private readonly INsgTradeRepository _repository;
    private readonly IAuditService _auditService;
    private readonly ICurrentUserService _currentUserService;
    private readonly ILogger<NsgTradeService> _logger;

    public NsgTradeService(
        INsgTradeRepository repository,
        IAuditService auditService,
        ICurrentUserService currentUserService,
        ILogger<NsgTradeService> logger)
    {
        _repository = repository;
        _auditService = auditService;
        _currentUserService = currentUserService;
        _logger = logger;
    }

    public async Task<Result<PaginatedResponse<NsgTradeResponseDto>>> GetPagedAsync(
        int pageNumber, int pageSize, bool? isActive, string? search, CancellationToken cancellationToken)
    {
        (IReadOnlyList<NsgTrade> items, int total) =
            await _repository.GetPagedAsync(pageNumber, pageSize, isActive, search, cancellationToken);

        return Result.Success(new PaginatedResponse<NsgTradeResponseDto>(
            items.Select(i => i.ToDto()).ToList(), total, pageNumber, pageSize));
    }

    public async Task<Result<NsgTradeResponseDto>> GetByIdAsync(int id, CancellationToken cancellationToken)
    {
        NsgTrade? entity = await _repository.GetByIdAsync(id, cancellationToken);
        return entity == null
            ? Result.Failure<NsgTradeResponseDto>(ApiErrorCodes.NotFound, NotFoundMessage)
            : Result.Success(entity.ToDto());
    }

    public async Task<Result<NsgTradeResponseDto>> CreateAsync(CreateNsgTradeRequestDto request, CancellationToken cancellationToken)
    {
        string name = request.NsgTradeName.Trim();

        // Pre-check gives a clear message; the repository also maps a unique-index violation (race) to the same outcome.
        if (await _repository.NameExistsAsync(name, null, cancellationToken))
            return Result.Failure<NsgTradeResponseDto>(ApiErrorCodes.Conflict, DuplicateMessage);

        NsgTrade entity = new() { NsgTradeName = name, IsActive = request.IsActive, CreatedAtIst = IndianStandardTime.Now };

        if (!await _repository.AddAsync(entity, cancellationToken))
            return Result.Failure<NsgTradeResponseDto>(ApiErrorCodes.Conflict, DuplicateMessage);

        await AuditAsync("NsgTradeCreated", entity.Id, cancellationToken);
        return Result.Success(entity.ToDto());
    }

    public async Task<Result<NsgTradeResponseDto>> UpdateAsync(int id, UpdateNsgTradeRequestDto request, CancellationToken cancellationToken)
    {
        NsgTrade? entity = await _repository.GetByIdAsync(id, cancellationToken);
        if (entity == null)
            return Result.Failure<NsgTradeResponseDto>(ApiErrorCodes.NotFound, NotFoundMessage);

        string name = request.NsgTradeName.Trim();
        bool newActive = request.IsActive ?? entity.IsActive;

        // Same rule as DELETE: a trade used by personnel cannot be deactivated (renaming it is fine).
        if (entity.IsActive && !newActive && await _repository.IsInUseAsync(id, cancellationToken))
            return Result.Failure<NsgTradeResponseDto>(ApiErrorCodes.Conflict, InUseMessage);

        if (await _repository.NameExistsAsync(name, id, cancellationToken))
            return Result.Failure<NsgTradeResponseDto>(ApiErrorCodes.Conflict, DuplicateMessage);

        entity.NsgTradeName = name;
        entity.IsActive = newActive;

        if (!await _repository.UpdateAsync(entity, cancellationToken))
            return Result.Failure<NsgTradeResponseDto>(ApiErrorCodes.Conflict, DuplicateMessage);

        await AuditAsync("NsgTradeUpdated", id, cancellationToken);
        return Result.Success(entity.ToDto());
    }

    public async Task<Result> DeactivateAsync(int id, CancellationToken cancellationToken)
    {
        NsgTrade? entity = await _repository.GetByIdAsync(id, cancellationToken);
        if (entity == null)
            return Result.Failure(ApiErrorCodes.NotFound, NotFoundMessage);

        // Idempotent: an already inactive trade stays inactive.
        if (entity.IsActive)
        {
            if (await _repository.IsInUseAsync(id, cancellationToken))
                return Result.Failure(ApiErrorCodes.Conflict, InUseMessage);

            entity.IsActive = false;
            await _repository.UpdateAsync(entity, cancellationToken);
            await AuditAsync("NsgTradeDeactivated", id, cancellationToken);
        }

        return Result.Success();
    }

    private async Task AuditAsync(string operation, int id, CancellationToken cancellationToken)
    {
        await _auditService.LogAsync(AuditAction.AdminAction, DomainConstants.AuditResults.Success,
            userId: _currentUserService.UserId, entityType: EntityType, entityId: id.ToString(),
            correlationId: _currentUserService.CorrelationId,
            additionalData: $"{{\"operation\":\"{operation}\"}}",
            cancellationToken: cancellationToken);

        _logger.LogInformation("{Operation} NSG trade {EntityId} by admin {AdminId}", operation, id, _currentUserService.UserId);
    }
}
