using Microsoft.Extensions.Logging;
using NSG.Mobile.Application.Common;
using NSG.Mobile.Application.Interfaces.Repositories;
using NSG.Mobile.Application.Interfaces.Services;
using NSG.Mobile.Application.Ranks.DTOs;
using NSG.Mobile.Domain.Common;
using NSG.Mobile.Domain.Constants;
using NSG.Mobile.Domain.Entities;
using NSG.Mobile.Domain.Enums;

namespace NSG.Mobile.Application.Ranks.Services;

/// <summary>Admin management of the NSG rank hierarchy (CRUD + dropdown lookup).</summary>
public interface IRankHierarchyService
{
    Task<Result<PaginatedResponse<RankResponseDto>>> GetPagedAsync(int pageNumber, int pageSize, bool? isActive, string? search, CancellationToken cancellationToken);
    Task<Result<IReadOnlyList<RankDropdownDto>>> GetDropdownAsync(CancellationToken cancellationToken);
    Task<Result<RankResponseDto>> GetByIdAsync(int id, CancellationToken cancellationToken);
    Task<Result<RankResponseDto>> CreateAsync(CreateRankRequestDto request, CancellationToken cancellationToken);
    Task<Result<RankResponseDto>> UpdateAsync(int id, UpdateRankRequestDto request, CancellationToken cancellationToken);
    Task<Result> DeleteAsync(int id, CancellationToken cancellationToken);
}

public class RankHierarchyService : IRankHierarchyService
{
    private const string EntityType = "RankHierarchy";

    private readonly IRankHierarchyRepository _repository;
    private readonly IAuditService _auditService;
    private readonly ICurrentUserService _currentUserService;
    private readonly ILogger<RankHierarchyService> _logger;

    public RankHierarchyService(
        IRankHierarchyRepository repository,
        IAuditService auditService,
        ICurrentUserService currentUserService,
        ILogger<RankHierarchyService> logger)
    {
        _repository = repository;
        _auditService = auditService;
        _currentUserService = currentUserService;
        _logger = logger;
    }

    public async Task<Result<PaginatedResponse<RankResponseDto>>> GetPagedAsync(
        int pageNumber, int pageSize, bool? isActive, string? search, CancellationToken cancellationToken)
    {
        (IReadOnlyList<RankHierarchy> items, int total) =
            await _repository.GetPagedAsync(pageNumber, pageSize, isActive, search, cancellationToken);

        return Result.Success(new PaginatedResponse<RankResponseDto>(
            items.Select(ToDto).ToList(), total, pageNumber, pageSize));
    }

    public async Task<Result<IReadOnlyList<RankDropdownDto>>> GetDropdownAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<RankHierarchy> ranks = await _repository.GetActiveAsync(cancellationToken);
        IReadOnlyList<RankDropdownDto> dropdown = ranks
            .Select(r => new RankDropdownDto { Id = r.Id, SrNo = r.SrNo, RankName = r.RankName, ShortCode = r.ShortCode })
            .ToList();
        return Result.Success(dropdown);
    }

    public async Task<Result<RankResponseDto>> GetByIdAsync(int id, CancellationToken cancellationToken)
    {
        RankHierarchy? rank = await _repository.GetByIdAsync(id, cancellationToken);
        return rank == null
            ? Result.Failure<RankResponseDto>(ApiErrorCodes.NotFound, "Rank not found.")
            : Result.Success(ToDto(rank));
    }

    public async Task<Result<RankResponseDto>> CreateAsync(CreateRankRequestDto request, CancellationToken cancellationToken)
    {
        string rankName = request.RankName.Trim();

        Result? conflict = await CheckConflictsAsync(request.SrNo, rankName, null, cancellationToken);
        if (conflict != null)
            return Result.Failure<RankResponseDto>(conflict.ErrorCode!, conflict.ErrorMessage!);

        DateTime now = IndianStandardTime.Now;
        RankHierarchy rank = new()
        {
            SrNo = request.SrNo,
            RankName = rankName,
            ShortCode = NormalizeShortCode(request.ShortCode),
            IsActive = request.IsActive,
            CreatedAt = now,
            UpdatedAt = now
        };

        await _repository.AddAsync(rank, cancellationToken);
        await AuditAsync("RankCreated", rank.Id, cancellationToken);

        return Result.Success(ToDto(rank));
    }

    public async Task<Result<RankResponseDto>> UpdateAsync(int id, UpdateRankRequestDto request, CancellationToken cancellationToken)
    {
        RankHierarchy? rank = await _repository.GetByIdAsync(id, cancellationToken);
        if (rank == null)
            return Result.Failure<RankResponseDto>(ApiErrorCodes.NotFound, "Rank not found.");

        string rankName = request.RankName.Trim();

        Result? conflict = await CheckConflictsAsync(request.SrNo, rankName, id, cancellationToken);
        if (conflict != null)
            return Result.Failure<RankResponseDto>(conflict.ErrorCode!, conflict.ErrorMessage!);

        rank.SrNo = request.SrNo;
        rank.RankName = rankName;
        rank.ShortCode = NormalizeShortCode(request.ShortCode);
        rank.IsActive = request.IsActive;
        rank.UpdatedAt = IndianStandardTime.Now;

        await _repository.UpdateAsync(rank, cancellationToken);
        await AuditAsync("RankUpdated", rank.Id, cancellationToken);

        return Result.Success(ToDto(rank));
    }

    public async Task<Result> DeleteAsync(int id, CancellationToken cancellationToken)
    {
        RankHierarchy? rank = await _repository.GetByIdAsync(id, cancellationToken);
        if (rank == null)
            return Result.Failure(ApiErrorCodes.NotFound, "Rank not found.");

        // Ranks are never hard-deleted: "delete" means deactivate, and only while no personnel record holds the rank.
        if (await _repository.IsInUseAsync(id, cancellationToken))
            return Result.Failure(ApiErrorCodes.Conflict, "Rank is assigned to personnel records and cannot be deleted.");

        if (rank.IsActive)
        {
            rank.IsActive = false;
            rank.UpdatedAt = IndianStandardTime.Now;
            await _repository.UpdateAsync(rank, cancellationToken);
            await AuditAsync("RankDeactivated", id, cancellationToken);
        }

        return Result.Success();
    }

    private async Task<Result?> CheckConflictsAsync(int srNo, string rankName, int? excludeId, CancellationToken cancellationToken)
    {
        if (await _repository.SrNoExistsAsync(srNo, excludeId, cancellationToken))
            return Result.Failure(ApiErrorCodes.ValidationFailed, $"A rank with SrNo {srNo} already exists.");

        if (await _repository.RankNameExistsAsync(rankName, excludeId, cancellationToken))
            return Result.Failure(ApiErrorCodes.ValidationFailed, "A rank with this name already exists.");

        return null;
    }

    private async Task AuditAsync(string operation, int rankId, CancellationToken cancellationToken)
    {
        await _auditService.LogAsync(AuditAction.AdminAction, DomainConstants.AuditResults.Success,
            userId: _currentUserService.UserId, entityType: EntityType, entityId: rankId.ToString(),
            correlationId: _currentUserService.CorrelationId,
            additionalData: $"{{\"operation\":\"{operation}\"}}",
            cancellationToken: cancellationToken);

        _logger.LogInformation("{Operation} rank {RankId} by admin {AdminId}", operation, rankId, _currentUserService.UserId);
    }

    private static string? NormalizeShortCode(string? shortCode)
        => string.IsNullOrWhiteSpace(shortCode) ? null : shortCode.Trim().ToUpperInvariant();

    private static RankResponseDto ToDto(RankHierarchy r) => new()
    {
        Id = r.Id,
        SrNo = r.SrNo,
        RankName = r.RankName,
        ShortCode = r.ShortCode,
        IsActive = r.IsActive,
        CreatedAt = r.CreatedAt,
        UpdatedAt = r.UpdatedAt
    };
}
