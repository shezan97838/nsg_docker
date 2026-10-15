using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
using NSG.Mobile.Application.Common;
using NSG.Mobile.Application.Interfaces.Infrastructure;
using NSG.Mobile.Application.Interfaces.Repositories;
using NSG.Mobile.Application.Interfaces.Services;
using NSG.Mobile.Application.Pis.DTOs;
using NSG.Mobile.Application.Pis.Mappings;
using NSG.Mobile.Domain.Common;
using NSG.Mobile.Domain.Constants;
using NSG.Mobile.Domain.Entities;
using NSG.Mobile.Domain.Enums;

namespace NSG.Mobile.Application.Pis.Services;

/// <summary>Admin management of personnel (PIS) records. There is no hard delete: "delete" deactivates.</summary>
public interface IUserPisService
{
    Task<Result<PaginatedResponse<UserPisListItemDto>>> GetPagedAsync(int pageNumber, int pageSize, UserPisFilter filter, CancellationToken cancellationToken);
    Task<Result<UserPisResponseDto>> GetByIdAsync(long id, CancellationToken cancellationToken);
    Task<Result<UserPisResponseDto>> CreateAsync(CreateUserPisRequestDto request, CancellationToken cancellationToken);
    Task<Result<UserPisResponseDto>> UpdateAsync(long id, UpdateUserPisRequestDto request, CancellationToken cancellationToken);
    Task<Result> DeactivateAsync(long id, CancellationToken cancellationToken);
}

public class UserPisService : IUserPisService
{
    private const string EntityType = "UserPis";
    private const string NotFoundMessage = "Personnel record not found.";

    private readonly IUserPisRepository _repository;
    private readonly IEncryptionService _encryption;
    private readonly IBlindIndexService _blindIndex;
    private readonly IAuditService _auditService;
    private readonly ICurrentUserService _currentUser;
    private readonly ILogger<UserPisService> _logger;

    public UserPisService(
        IUserPisRepository repository,
        IEncryptionService encryption,
        IBlindIndexService blindIndex,
        IAuditService auditService,
        ICurrentUserService currentUser,
        ILogger<UserPisService> logger)
    {
        _repository = repository;
        _encryption = encryption;
        _blindIndex = blindIndex;
        _auditService = auditService;
        _currentUser = currentUser;
        _logger = logger;
    }

    public async Task<Result<PaginatedResponse<UserPisListItemDto>>> GetPagedAsync(
        int pageNumber, int pageSize, UserPisFilter filter, CancellationToken cancellationToken)
    {
        (IReadOnlyList<UserPis> items, int total) = await _repository.GetPagedAsync(pageNumber, pageSize, filter, cancellationToken);

        return Result.Success(new PaginatedResponse<UserPisListItemDto>(
            items.Select(i => i.ToListItem()).ToList(), total, pageNumber, pageSize));
    }

    public async Task<Result<UserPisResponseDto>> GetByIdAsync(long id, CancellationToken cancellationToken)
    {
        UserPis? entity = await _repository.GetDetailAsync(id, cancellationToken);
        return entity == null
            ? Result.Failure<UserPisResponseDto>(ApiErrorCodes.NotFound, NotFoundMessage)
            : Result.Success(entity.ToResponse(MaskStored));
    }

    public async Task<Result<UserPisResponseDto>> CreateAsync(CreateUserPisRequestDto request, CancellationToken cancellationToken)
    {
        UserPis entity = new()
        {
            NsgNumber = PisNormalizer.Identifier(request.NsgNumber),
            IsActive = true,
            MobileAppActivationStatus = MobileAppActivationStatus.Pending,
            CreatedAtIst = IndianStandardTime.Now,
            CreatedBy = _currentUser.UserId
        };
        request.ApplyTo(entity);
        ApplySensitive(request, entity);

        Result<UserPisResponseDto>? invalid = await CheckReferencesAsync(entity, cancellationToken);
        if (invalid != null)
            return invalid;

        PisSaveOutcome outcome = await _repository.AddAsync(entity, cancellationToken);
        if (outcome != PisSaveOutcome.Saved)
            return Result.Failure<UserPisResponseDto>(ApiErrorCodes.Conflict, ConflictMessage(outcome));

        await AuditAsync("UserPisCreated", entity.Id, cancellationToken);
        return await GetByIdAsync(entity.Id, cancellationToken);
    }

    public async Task<Result<UserPisResponseDto>> UpdateAsync(long id, UpdateUserPisRequestDto request, CancellationToken cancellationToken)
    {
        UserPis? entity = await _repository.GetByIdAsync(id, cancellationToken);
        if (entity == null)
            return Result.Failure<UserPisResponseDto>(ApiErrorCodes.NotFound, NotFoundMessage);

        // NSG number, activation status, hashes/ciphertext (unless re-supplied) and creation metadata are left untouched.
        request.ApplyTo(entity);
        entity.IsActive = request.IsActive;
        ApplySensitive(request, entity);
        entity.UpdatedAtIst = IndianStandardTime.Now;
        entity.UpdatedBy = _currentUser.UserId;

        Result<UserPisResponseDto>? invalid = await CheckReferencesAsync(entity, cancellationToken);
        if (invalid != null)
            return invalid;

        PisSaveOutcome outcome = await _repository.UpdateAsync(entity, cancellationToken);
        if (outcome != PisSaveOutcome.Saved)
            return Result.Failure<UserPisResponseDto>(ApiErrorCodes.Conflict, ConflictMessage(outcome));

        await AuditAsync("UserPisUpdated", id, cancellationToken);
        return await GetByIdAsync(id, cancellationToken);
    }

    public async Task<Result> DeactivateAsync(long id, CancellationToken cancellationToken)
    {
        UserPis? entity = await _repository.GetByIdAsync(id, cancellationToken);
        if (entity == null)
            return Result.Failure(ApiErrorCodes.NotFound, NotFoundMessage);

        // Personnel records are never hard-deleted; deactivation is idempotent.
        if (entity.IsActive)
        {
            entity.IsActive = false;
            entity.UpdatedAtIst = IndianStandardTime.Now;
            entity.UpdatedBy = _currentUser.UserId;
            await _repository.UpdateAsync(entity, cancellationToken);
            await AuditAsync("UserPisDeactivated", id, cancellationToken);
        }

        return Result.Success();
    }

    /// <summary>Encrypts and hashes each sensitive identifier that was supplied; omitted ones keep their stored value.</summary>
    private void ApplySensitive(UserPisWriteDto request, UserPis entity)
    {
        if (request.AadhaarNumber != null)
            entity.AadhaarNumberEncrypted = _encryption.Encrypt(request.AadhaarNumber);

        if (request.PranNumber != null)
            entity.PranNumberEncrypted = _encryption.Encrypt(request.PranNumber);

        string? pan = PisNormalizer.Pan(request.PanNumber);
        if (pan != null)
        {
            entity.PanNumberEncrypted = _encryption.Encrypt(pan);
            entity.PanNumberHash = _blindIndex.ForPan(pan);
        }

        string? account = PisNormalizer.SalaryAccountNumber(request.SalaryAccountNumber);
        if (account != null)
        {
            entity.SalaryAccountNumberEncrypted = _encryption.Encrypt(account);
            entity.SalaryAccountNumberHash = _blindIndex.ForSalaryAccountNumber(account);
        }
    }

        private async Task<Result<UserPisResponseDto>?> CheckReferencesAsync(UserPis entity, CancellationToken cancellationToken)
    {
        IReadOnlyList<string> missing = await _repository.GetMissingReferencesAsync(entity, cancellationToken);
        return missing.Count == 0
            ? null
            : Result.Failure<UserPisResponseDto>(ApiErrorCodes.ValidationFailed, "Referenced record not found for: " + string.Join(", ", missing) + ".");
    }

    private static string ConflictMessage(PisSaveOutcome outcome) => outcome switch
    {
        PisSaveOutcome.DuplicateNsgNumber => "A personnel record with this NSG number already exists.",
        PisSaveOutcome.DuplicateMobileNumber => "A personnel record with this mobile number already exists.",
        PisSaveOutcome.DuplicatePanNumber => "A personnel record with this PAN already exists.",
        PisSaveOutcome.DuplicateSalaryAccountNumber => "A personnel record with this salary account number already exists.",
        _ => "A personnel record with the same unique identifier already exists."
    };

    /// <summary>Decrypts only to mask; an unreadable value (e.g. rotated key) yields null and never fails the request.</summary>
    private string? MaskStored(string? cipherText)
    {
        if (cipherText == null)
            return null;

        try
        {
            return PisMasking.Mask(_encryption.Decrypt(cipherText));
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException)
        {
            _logger.LogWarning("A stored sensitive personnel value could not be decrypted for display.");
            return null;
        }
    }

    private async Task AuditAsync(string operation, long id, CancellationToken cancellationToken)
    {
        // Only the operation and record id are audited or logged — never personnel data.
        await _auditService.LogAsync(AuditAction.AdminAction, DomainConstants.AuditResults.Success,
            userId: _currentUser.UserId, entityType: EntityType, entityId: id.ToString(),
            correlationId: _currentUser.CorrelationId,
            additionalData: $"{{\"operation\":\"{operation}\"}}",
            cancellationToken: cancellationToken);

        _logger.LogInformation("{Operation} personnel record {RecordId} by admin {AdminId}", operation, id, _currentUser.UserId);
    }
}
