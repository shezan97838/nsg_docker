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
    private const string NotFoundMessage = PisOwnershipMessages.NotFound;

    private readonly IUserPisRepository _repository;
    private readonly IEncryptionService _encryption;
    private readonly IAuditService _auditService;
    private readonly ICurrentUserService _currentUser;
    private readonly ILogger<UserPisService> _logger;

    public UserPisService(
        IUserPisRepository repository,
        IEncryptionService encryption,
        IAuditService auditService,
        ICurrentUserService currentUser,
        ILogger<UserPisService> logger)
    {
        _repository = repository;
        _encryption = encryption;
        _auditService = auditService;
        _currentUser = currentUser;
        _logger = logger;
    }

    public async Task<Result<PaginatedResponse<UserPisListItemDto>>> GetPagedAsync(
        int pageNumber, int pageSize, UserPisFilter filter, CancellationToken cancellationToken)
    {
        // Only the signed-in PIS Admin's own records are listed; the ownership predicate runs in the repository query (page, total, search, filters).
        if (!_currentUser.TryGetAdminActor(out AdminActor actor))
            return Result.Failure<PaginatedResponse<UserPisListItemDto>>(ApiErrorCodes.Unauthorized, AdminActorExtensions.AdminNotIdentifiedMessage);

        (IReadOnlyList<UserPis> items, int total) = await _repository.GetPagedAsync(pageNumber, pageSize, filter, actor.Id, cancellationToken);

        return Result.Success(new PaginatedResponse<UserPisListItemDto>(
            items.Select(i => i.ToListItem()).ToList(), total, pageNumber, pageSize));
    }

    /// <summary>
    /// One record for the signed-in PIS Admin. Only a record assigned to the caller is returned: a record assigned to another admin is refused (403),
    /// and a record assigned to nobody is invisible, exactly like a missing one (404).
    /// </summary>
    public async Task<Result<UserPisResponseDto>> GetByIdAsync(long id, CancellationToken cancellationToken)
    {
        if (!_currentUser.TryGetAdminActor(out AdminActor actor))
            return Result.Failure<UserPisResponseDto>(ApiErrorCodes.Unauthorized, AdminActorExtensions.AdminNotIdentifiedMessage);

        UserPis? entity = await _repository.GetDetailAsync(id, cancellationToken);
        if (entity == null || entity.AssignedTo == null)
            return Result.Failure<UserPisResponseDto>(ApiErrorCodes.NotFound, NotFoundMessage);
        if (entity.AssignedTo != actor.Id)
            return Result.Failure<UserPisResponseDto>(ApiErrorCodes.Forbidden, PisOwnershipMessages.NotAssignedToYouToView);

        return Result.Success(entity.ToResponse(MaskStored));
    }

    /// <summary>The response of a record this request has just written (ownership was verified or established by the write itself).</summary>
    private async Task<Result<UserPisResponseDto>> LoadWrittenAsync(long id, CancellationToken cancellationToken)
    {
        UserPis? entity = await _repository.GetDetailAsync(id, cancellationToken);
        return entity == null
            ? Result.Failure<UserPisResponseDto>(ApiErrorCodes.NotFound, NotFoundMessage)
            : Result.Success(entity.ToResponse(MaskStored));
    }

    /// <summary>Null when <paramref name="stored"/> is assigned to <paramref name="adminId"/>; otherwise the refusal. Ownership is never changed here.</summary>
    private static Result<UserPisResponseDto>? OwnershipRefusal(UserPis stored, long adminId)
        => stored.AssignedTo == adminId ? null
            : Result.Failure<UserPisResponseDto>(ApiErrorCodes.Forbidden, stored.AssignedTo == null ? PisOwnershipMessages.Unassigned : PisOwnershipMessages.NotAssignedToYou);

    public async Task<Result<UserPisResponseDto>> CreateAsync(CreateUserPisRequestDto request, CancellationToken cancellationToken)
    {
        // Resolved and validated before anything is read or written: a missing, non-positive or int-overflowing admin id is refused (401).
        if (!_currentUser.TryGetAdminActor(out AdminActor actor))
            return Result.Failure<UserPisResponseDto>(ApiErrorCodes.Unauthorized, AdminActorExtensions.AdminNotIdentifiedMessage);

        UserPis entity = new()
        {
            NsgNumber = PisNormalizer.Identifier(request.NsgNumber),
            IsActive = true,
            MobileAppActivationStatus = MobileAppActivationStatus.Pending,
            CreatedAtIst = IndianStandardTime.Now,
            CreatedBy = actor.Key,   // the creating admin; UpdatedBy/UpdatedAtIst stay empty until the first real update
            AssignedTo = actor.Id    // server-managed: the creating PIS Admin owns the new record; the request has no such field
        };
        request.ApplyTo(entity);
        Result<UserPisResponseDto>? maskRejected = ApplySensitive(request, entity);
        if (maskRejected != null)
            return maskRejected;

        PisSaveOutcome outcome = await _repository.AddAsync(entity, cancellationToken);
        if (outcome != PisSaveOutcome.Saved)
            return Result.Failure<UserPisResponseDto>(ApiErrorCodes.Conflict, ConflictMessage(outcome));

        await AuditAsync("UserPisCreated", entity.Id, actor, cancellationToken);
        return await LoadWrittenAsync(entity.Id, cancellationToken);
    }

    public async Task<Result<UserPisResponseDto>> UpdateAsync(long id, UpdateUserPisRequestDto request, CancellationToken cancellationToken)
    {
        if (!_currentUser.TryGetAdminActor(out AdminActor actor))
            return Result.Failure<UserPisResponseDto>(ApiErrorCodes.Unauthorized, AdminActorExtensions.AdminNotIdentifiedMessage);

        UserPis? entity = await _repository.GetByIdAsync(id, cancellationToken);
        if (entity == null)
            return Result.Failure<UserPisResponseDto>(ApiErrorCodes.NotFound, NotFoundMessage);
        if (OwnershipRefusal(entity, actor.Id) is { } refused)
            return refused;

        // NSG number, activation status, ciphertext (unless re-supplied), creation metadata and ownership are left untouched.
        request.ApplyTo(entity);
        entity.IsActive = request.IsActive;
        Result<UserPisResponseDto>? maskRejected = ApplySensitive(request, entity);
        if (maskRejected != null)
            return maskRejected;

        entity.UpdatedAtIst = IndianStandardTime.Now;
        entity.UpdatedBy = actor.Key;   // the updating admin; CreatedBy is never touched here
        PisSaveOutcome outcome = await _repository.UpdateAsync(entity, actor.Id, cancellationToken);
        if (outcome == PisSaveOutcome.NotOwner)
            return Result.Failure<UserPisResponseDto>(ApiErrorCodes.Forbidden, PisOwnershipMessages.NotAssignedToYou);   // transferred after it was loaded
        if (outcome != PisSaveOutcome.Saved)
            return Result.Failure<UserPisResponseDto>(ApiErrorCodes.Conflict, ConflictMessage(outcome));

        await AuditAsync("UserPisUpdated", id, actor, cancellationToken);
        return await LoadWrittenAsync(id, cancellationToken);
    }

    public async Task<Result> DeactivateAsync(long id, CancellationToken cancellationToken)
    {
        if (!_currentUser.TryGetAdminActor(out AdminActor actor))
            return Result.Failure(ApiErrorCodes.Unauthorized, AdminActorExtensions.AdminNotIdentifiedMessage);

        UserPis? entity = await _repository.GetByIdAsync(id, cancellationToken);
        if (entity == null)
            return Result.Failure(ApiErrorCodes.NotFound, NotFoundMessage);
        if (OwnershipRefusal(entity, actor.Id) is { } refused)
            return Result.Failure(refused.ErrorCode!, refused.ErrorMessage!);

        // Personnel records are never hard-deleted; deactivation is idempotent.
        if (entity.IsActive)
        {
            entity.IsActive = false;
            entity.UpdatedAtIst = IndianStandardTime.Now;
            entity.UpdatedBy = actor.Key;   // the deactivating admin; CreatedBy is never touched here
            if (await _repository.UpdateAsync(entity, actor.Id, cancellationToken) == PisSaveOutcome.NotOwner)
                return Result.Failure(ApiErrorCodes.Forbidden, PisOwnershipMessages.NotAssignedToYou);   // transferred after it was loaded
            await AuditAsync("UserPisDeactivated", id, actor, cancellationToken);
        }

        return Result.Success();
    }

    private enum SensitiveChange { None, Keep, Replace, Reject }

    /// <summary>
    /// Encrypts each sensitive identifier that was supplied; omitted ones keep their stored value. A value
    /// equal to the exact mask of the currently stored value is a round-tripped GET value and leaves the ciphertext
    /// untouched. Any other mask-shaped value is rejected, so a placeholder is never stored. All four
    /// fields are checked before any is applied. Returns a failure, or null when the request may proceed.
    /// </summary>
    private Result<UserPisResponseDto>? ApplySensitive(UserPisWriteDto request, UserPis entity)
    {
        string? aadhaar = request.AadhaarNumber?.Trim();
        string? pran = request.PranNumber?.Trim();
        string? pan = PisNormalizer.Pan(request.PanNumber);
        string? account = PisNormalizer.SalaryAccountNumber(request.SalaryAccountNumber);

        SensitiveChange aadhaarChange = Classify(aadhaar, entity.AadhaarNumberEncrypted);
        SensitiveChange panChange = Classify(pan, entity.PanNumberEncrypted);
        SensitiveChange pranChange = Classify(pran, entity.PranNumberEncrypted);
        SensitiveChange accountChange = Classify(account, entity.SalaryAccountNumberEncrypted);

        List<string> rejected = new();
        if (aadhaarChange == SensitiveChange.Reject) rejected.Add("Aadhaar number");
        if (panChange == SensitiveChange.Reject) rejected.Add("PAN");
        if (pranChange == SensitiveChange.Reject) rejected.Add("PRAN");
        if (accountChange == SensitiveChange.Reject) rejected.Add("Salary account number");
        if (rejected.Count > 0)
            return Result.Failure<UserPisResponseDto>(ApiErrorCodes.ValidationFailed,
                string.Join(", ", rejected) + ": a masked value does not match the stored value; send the full value or omit the field.");

        if (aadhaarChange == SensitiveChange.Replace)
            entity.AadhaarNumberEncrypted = _encryption.Encrypt(request.AadhaarNumber!);

        if (pranChange == SensitiveChange.Replace)
            entity.PranNumberEncrypted = _encryption.Encrypt(request.PranNumber!);

        if (panChange == SensitiveChange.Replace)
        {
            entity.PanNumberEncrypted = _encryption.Encrypt(pan!);
        }

        if (accountChange == SensitiveChange.Replace)
        {
            entity.SalaryAccountNumberEncrypted = _encryption.Encrypt(account!);
        }

        return null;
    }

    /// <summary>
    /// Decides what a submitted (normalised) value means. The stored value is decrypted and masked with the same logic
    /// GET uses; only an exact match counts as unchanged. A mask-shaped value that does not match, or that cannot be
    /// checked because nothing is stored or the stored value is unreadable, is rejected.
    /// </summary>
    private SensitiveChange Classify(string? submitted, string? storedCipher)
    {
        if (submitted == null)
            return SensitiveChange.None;

        if (!PisMasking.LooksMasked(submitted))
            return SensitiveChange.Replace;

        if (storedCipher == null)
            return SensitiveChange.Reject;

        try
        {
            return PisMasking.IsMaskOf(submitted, _encryption.Decrypt(storedCipher)) ? SensitiveChange.Keep : SensitiveChange.Reject;
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException)
        {
            _logger.LogWarning("A stored sensitive personnel value could not be decrypted to check a submitted mask.");
            return SensitiveChange.Reject;
        }
    }

    private static string ConflictMessage(PisSaveOutcome outcome) => outcome switch
    {
        PisSaveOutcome.DuplicateNsgNumber => "A personnel record with this NSG number already exists.",
        PisSaveOutcome.DuplicateMobileNumber => "A personnel record with this mobile number already exists.",
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

    private async Task AuditAsync(string operation, long id, AdminActor actor, CancellationToken cancellationToken)
    {
        // Only the operation and record id are audited or logged — never personnel data. The audit user is the acting Admin Profile ID.
        await _auditService.LogAsync(AuditAction.AdminAction, DomainConstants.AuditResults.Success,
            userId: actor.Key, entityType: EntityType, entityId: id.ToString(),
            correlationId: _currentUser.CorrelationId,
            additionalData: $"{{\"operation\":\"{operation}\",\"adminProfileId\":{actor.Id}}}",
            cancellationToken: cancellationToken);

        _logger.LogInformation("{Operation} personnel record {RecordId} by admin {AdminId}", operation, id, actor.Id);
    }
}
