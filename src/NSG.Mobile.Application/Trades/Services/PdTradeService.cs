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

/// <summary>Admin management of PD trades. "Delete" deactivates; nothing is hard-deleted.</summary>
public interface IPdTradeService
{
    Task<Result<PaginatedResponse<PdTradeResponseDto>>> GetPagedAsync(int pageNumber, int pageSize, bool? isActive, string? search, CancellationToken cancellationToken);
    Task<Result<PdTradeResponseDto>> GetByIdAsync(int id, CancellationToken cancellationToken);
    Task<Result<PdTradeResponseDto>> CreateAsync(CreatePdTradeRequestDto request, CancellationToken cancellationToken);
    Task<Result<PdTradeResponseDto>> UpdateAsync(int id, UpdatePdTradeRequestDto request, CancellationToken cancellationToken);
    Task<Result> DeactivateAsync(int id, CancellationToken cancellationToken);
}

public class PdTradeService : IPdTradeService
{
    private const string EntityType = "PdTrade";
    private const string NotFoundMessage = "PD trade not found.";
    private const string DuplicateMessage = "A PD trade with this name already exists.";
    private const string InUseMessage = "PD trade is used by personnel records and cannot be deactivated.";

    private readonly IPdTradeRepository _repository;
    private readonly IAuditService _auditService;
    private readonly ICurrentUserService _currentUserService;
    private readonly ILogger<PdTradeService> _logger;

    public PdTradeService(
        IPdTradeRepository repository,
        IAuditService auditService,
        ICurrentUserService currentUserService,
        ILogger<PdTradeService> logger)
    {
        _repository = repository;
        _auditService = auditService;
        _currentUserService = currentUserService;
        _logger = logger;
    }

    public async Task<Result<PaginatedResponse<PdTradeResponseDto>>> GetPagedAsync(
        int pageNumber, int pageSize, bool? isActive, string? search, CancellationToken cancellationToken)
    {
        (IReadOnlyList<PdTrade> items, int total) =
            await _repository.GetPagedAsync(pageNumber, pageSize, isActive, search, cancellationToken);

        return Result.Success(new PaginatedResponse<PdTradeResponseDto>(
            items.Select(i => i.ToDto()).ToList(), total, pageNumber, pageSize));
    }

    public async Task<Result<PdTradeResponseDto>> GetByIdAsync(int id, CancellationToken cancellationToken)
    {
        PdTrade? entity = await _repository.GetByIdAsync(id, cancellationToken);
        return entity == null
            ? Result.Failure<PdTradeResponseDto>(ApiErrorCodes.NotFound, NotFoundMessage)
            : Result.Success(entity.ToDto());
    }

    public async Task<Result<PdTradeResponseDto>> CreateAsync(CreatePdTradeRequestDto request, CancellationToken cancellationToken)
    {
        string name = request.PdTradeName.Trim();

        // Pre-check gives a clear message; the repository also maps a unique-index violation (race) to the same outcome.
        if (await _repository.NameExistsAsync(name, null, cancellationToken))
            return Result.Failure<PdTradeResponseDto>(ApiErrorCodes.Conflict, DuplicateMessage);

        PdTrade entity = new() { PdTradeName = name, IsActive = request.IsActive, CreatedAtIst = IndianStandardTime.Now };

        if (!await _repository.AddAsync(entity, cancellationToken))
            return Result.Failure<PdTradeResponseDto>(ApiErrorCodes.Conflict, DuplicateMessage);

        await AuditAsync("PdTradeCreated", entity.Id, cancellationToken);
        return Result.Success(entity.ToDto());
    }

    public async Task<Result<PdTradeResponseDto>> UpdateAsync(int id, UpdatePdTradeRequestDto request, CancellationToken cancellationToken)
    {
        PdTrade? entity = await _repository.GetByIdAsync(id, cancellationToken);
        if (entity == null)
            return Result.Failure<PdTradeResponseDto>(ApiErrorCodes.NotFound, NotFoundMessage);

        string name = request.PdTradeName.Trim();
        bool newActive = request.IsActive ?? entity.IsActive;

        // Same rule as DELETE: a trade used by personnel cannot be deactivated (renaming it is fine).
        if (entity.IsActive && !newActive && await _repository.IsInUseAsync(id, cancellationToken))
            return Result.Failure<PdTradeResponseDto>(ApiErrorCodes.Conflict, InUseMessage);

        if (await _repository.NameExistsAsync(name, id, cancellationToken))
            return Result.Failure<PdTradeResponseDto>(ApiErrorCodes.Conflict, DuplicateMessage);

        entity.PdTradeName = name;
        entity.IsActive = newActive;

        if (!await _repository.UpdateAsync(entity, cancellationToken))
            return Result.Failure<PdTradeResponseDto>(ApiErrorCodes.Conflict, DuplicateMessage);

        await AuditAsync("PdTradeUpdated", id, cancellationToken);
        return Result.Success(entity.ToDto());
    }

    public async Task<Result> DeactivateAsync(int id, CancellationToken cancellationToken)
    {
        PdTrade? entity = await _repository.GetByIdAsync(id, cancellationToken);
        if (entity == null)
            return Result.Failure(ApiErrorCodes.NotFound, NotFoundMessage);

        // Idempotent: an already inactive trade stays inactive.
        if (entity.IsActive)
        {
            if (await _repository.IsInUseAsync(id, cancellationToken))
                return Result.Failure(ApiErrorCodes.Conflict, InUseMessage);

            entity.IsActive = false;
            await _repository.UpdateAsync(entity, cancellationToken);
            await AuditAsync("PdTradeDeactivated", id, cancellationToken);
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

        _logger.LogInformation("{Operation} PD trade {EntityId} by admin {AdminId}", operation, id, _currentUserService.UserId);
    }
}
