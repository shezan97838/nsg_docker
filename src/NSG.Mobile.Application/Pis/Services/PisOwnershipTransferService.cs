using FluentValidation;
using NSG.Mobile.Application.Admin.Services;
using NSG.Mobile.Application.Common;
using NSG.Mobile.Application.Interfaces.Repositories;
using NSG.Mobile.Application.Interfaces.Services;
using NSG.Mobile.Application.MobileAuth.Validators;
using NSG.Mobile.Domain.Common;
using NSG.Mobile.Domain.Constants;
using NSG.Mobile.Domain.Entities;
using NSG.Mobile.Domain.Enums;

namespace NSG.Mobile.Application.Pis.Services;

/// <summary>Request to give a personnel record to a PIS Admin. Both people are named by NSG number; no database id is accepted.</summary>
public class PisOwnershipTransferRequestDto
{
    /// <summary>NSG number of the personnel record to transfer.</summary>
    public string PersonnelNsgNumber { get; set; } = string.Empty;

    /// <summary>NSG number of the PIS Admin who becomes responsible. Must be an active, activated PIS_ADMIN account.</summary>
    public string TargetPisAdminNsgNumber { get; set; } = string.Empty;

    /// <summary>Why the record is transferred (optional, at most 500 characters).</summary>
    public string? Reason { get; set; }
}

/// <summary>Outcome of a transfer request. Names no admin other than the target the caller supplied.</summary>
public class PisOwnershipTransferDto
{
    public string PersonnelNsgNumber { get; set; } = string.Empty;
    public string TargetPisAdminNsgNumber { get; set; } = string.Empty;

    /// <summary>False when the record already belonged to the target (nothing changed, no history written).</summary>
    public bool Transferred { get; set; }

    /// <summary>True when the record had an owner before this transfer; false when it was unassigned.</summary>
    public bool WasAssigned { get; set; }

    /// <summary>IST instant of the transfer (fixed +05:30 offset); null when nothing was transferred.</summary>
    public DateTimeOffset? TransferredAt { get; set; }
}

public class PisOwnershipTransferRequestValidator : AbstractValidator<PisOwnershipTransferRequestDto>
{
    public PisOwnershipTransferRequestValidator()
    {
        RuleFor(x => x.PersonnelNsgNumber).NotEmpty().WithMessage("Personnel NSG number is required.")
            .MaximumLength(30).WithMessage("Personnel NSG number must not exceed 30 characters.");
        RuleFor(x => x.TargetPisAdminNsgNumber).NotEmpty().WithMessage("Target PIS Admin NSG number is required.")
            .MaximumLength(30).WithMessage("Target PIS Admin NSG number must not exceed 30 characters.");
        RuleFor(x => x.Reason).MaximumLength(500).WithMessage("Reason must not exceed 500 characters.");
    }
}

/// <summary>Super Admin transfer of one personnel record to a PIS Admin. Nothing else about the record is read or changed.</summary>
public interface IPisOwnershipTransferService
{
    Task<Result<ApiResult<PisOwnershipTransferDto>>> TransferAsync(PisOwnershipTransferRequestDto request, CancellationToken cancellationToken = default);
}

public class PisOwnershipTransferService : IPisOwnershipTransferService
{
    public const string TransferredMessage = "Personnel ownership transferred successfully.";
    public const string AlreadyAssignedMessage = "Personnel record is already assigned to this PIS Admin. Nothing was changed.";
    public const string PersonnelNotFoundMessage = "Personnel record not found.";
    public const string TargetNotFoundMessage = "Target PIS Admin account not found.";
    public const string TargetInactiveMessage = "The target PIS Admin account is inactive.";
    public const string TargetNotActivatedMessage = "The target PIS Admin account has not completed activation.";
    public const string TargetNotPisAdminMessage = "The target account is not a PIS Admin.";

    private readonly IAdminProfileRepository _admins;
    private readonly IPisOwnershipRepository _ownership;
    private readonly IAuditService _audit;
    private readonly ICurrentUserService _currentUser;
    private readonly TimeProvider _time;

    public PisOwnershipTransferService(IAdminProfileRepository admins, IPisOwnershipRepository ownership, IAuditService audit,
        ICurrentUserService currentUser, TimeProvider time)
    {
        _admins = admins;
        _ownership = ownership;
        _audit = audit;
        _currentUser = currentUser;
        _time = time;
    }

    public async Task<Result<ApiResult<PisOwnershipTransferDto>>> TransferAsync(PisOwnershipTransferRequestDto request, CancellationToken cancellationToken = default)
    {
        // The acting Super Admin is the token's admin_profile_id, never a request value. The audit user column is an int, so an id that does not
        // fit is refused here, before anything is read or written (the history column itself is a bigint).
        if (!_currentUser.TryGetAdminActor(out AdminActor actor)) return Fail(ApiErrorCodes.Unauthorized, AdminActorExtensions.AdminNotIdentifiedMessage);
        long actorId = actor.Id;

        string personnelNsg = MobileAuthInput.Nsg(request.PersonnelNsgNumber);
        string targetNsg = MobileAuthInput.Nsg(request.TargetPisAdminNsgNumber);
        string? reason = string.IsNullOrWhiteSpace(request.Reason) ? null : request.Reason.Trim();

        // The target: an existing, active, fully activated Admin Portal account whose role is exactly PIS_ADMIN (Super Admin and every other role are refused).
        AdminProfile? target = await _admins.GetByNsgNumberAsync(targetNsg, cancellationToken);
        if (target == null) return Fail(ApiErrorCodes.NotFound, TargetNotFoundMessage);
        if (target.Role is not { RoleCode: AdminRoleCodes.PisAdmin, IsActive: true }) return Fail(ApiErrorCodes.ValidationFailed, TargetNotPisAdminMessage);
        if (!target.IsActive) return Fail(ApiErrorCodes.Conflict, TargetInactiveMessage);
        if (target.ActivationStatus != AdminActivationStatus.Active) return Fail(ApiErrorCodes.Conflict, TargetNotActivatedMessage);

        DateTime now = IndianStandardTime.ConvertFromUtc(_time.GetUtcNow().UtcDateTime);
        PisOwnershipTransferResult result = await _ownership.TransferAsync(personnelNsg, target.Id, actorId, reason, now, cancellationToken);

        switch (result.Outcome)
        {
            case PisOwnershipTransferOutcome.PersonnelNotFound:
                return Fail(ApiErrorCodes.NotFound, PersonnelNotFoundMessage);

            case PisOwnershipTransferOutcome.TargetNotEligible:
                return Fail(ApiErrorCodes.Conflict, TargetInactiveMessage);

            case PisOwnershipTransferOutcome.AlreadyAssigned:
                return Result.Success(new ApiResult<PisOwnershipTransferDto>(AlreadyAssignedMessage, new PisOwnershipTransferDto
                {
                    PersonnelNsgNumber = result.NsgNumber ?? personnelNsg, TargetPisAdminNsgNumber = target.NsgNumber, Transferred = false, WasAssigned = true
                }));
        }

        await _audit.LogAsync(AuditAction.AdminAction, DomainConstants.AuditResults.Success, userId: actor.Key, entityType: nameof(UserPis), entityId: result.PisRecordId.ToString(),
            correlationId: _currentUser.CorrelationId,
            additionalData: AdminSupport.Audit(("operation", "PisOwnershipTransferred"), ("actorAdminId", actorId),
                ("previousAssignedTo", result.PreviousAssignedTo), ("newAssignedTo", target.Id)),
            cancellationToken: cancellationToken);

        return Result.Success(new ApiResult<PisOwnershipTransferDto>(TransferredMessage, new PisOwnershipTransferDto
        {
            PersonnelNsgNumber = result.NsgNumber ?? personnelNsg,
            TargetPisAdminNsgNumber = target.NsgNumber,
            Transferred = true,
            WasAssigned = result.PreviousAssignedTo != null,
            TransferredAt = IstTime.FromIst(now)
        }));
    }

    private static Result<ApiResult<PisOwnershipTransferDto>> Fail(string code, string message)
        => Result.Failure<ApiResult<PisOwnershipTransferDto>>(code, message);
}

// ─── Bulk ownership transfer ───

/// <summary>Request to move every <c>users_pis</c> record of one PIS Admin to another. Both are named by admin_profile_id.</summary>
public class PisBulkOwnershipTransferRequestDto
{
    /// <summary>admin_profiles.id of the PIS Admin who currently owns the records.</summary>
    public long SourceAdminId { get; set; }

    /// <summary>admin_profiles.id of the PIS Admin who will own every transferred record.</summary>
    public long TargetAdminId { get; set; }

    /// <summary>Why the records are transferred (optional, at most 500 characters).</summary>
    public string? Reason { get; set; }
}

/// <summary>Outcome of a bulk transfer request.</summary>
public class PisBulkOwnershipTransferDto
{
    /// <summary>Number of personnel records moved to the target admin.</summary>
    public int TransferredCount { get; set; }

    /// <summary>IST instant of the transfer; null when nothing was transferred.</summary>
    public DateTimeOffset? TransferredAt { get; set; }
}

public class PisBulkOwnershipTransferRequestValidator : AbstractValidator<PisBulkOwnershipTransferRequestDto>
{
    public PisBulkOwnershipTransferRequestValidator()
    {
        RuleFor(x => x.SourceAdminId).GreaterThan(0).WithMessage("Source admin ID must be a positive number.");
        RuleFor(x => x.TargetAdminId).GreaterThan(0).WithMessage("Target admin ID must be a positive number.");
        RuleFor(x => x.Reason).MaximumLength(500).WithMessage("Reason must not exceed 500 characters.");
        RuleFor(x => x).Must(x => x.SourceAdminId != x.TargetAdminId)
            .When(x => x.SourceAdminId > 0 && x.TargetAdminId > 0)
            .WithMessage("Source and target admin must be different.");
    }
}

public interface IPisBulkOwnershipTransferService
{
    Task<Result<ApiResult<PisBulkOwnershipTransferDto>>> BulkTransferAsync(PisBulkOwnershipTransferRequestDto request, CancellationToken cancellationToken = default);
}

public class PisBulkOwnershipTransferService : IPisBulkOwnershipTransferService
{
    public const string BulkTransferredMessage = "Personnel ownership bulk transfer completed successfully.";
    public const string BulkNothingToTransferMessage = "Source admin has no assigned personnel. Nothing was changed.";
    public const string SourceNotFoundMessage = "Source PIS Admin account not found.";
    public const string SourceNotPisAdminMessage = "The source account is not a PIS Admin.";
    public const string SourceAdminIdInvalidMessage = "Source admin ID must be a positive number.";
    public const string TargetAdminIdInvalidMessage = "Target admin ID must be a positive number.";
    public const string SourceEqualsTargetMessage = "Source and target admin must be different.";

    private readonly IAdminProfileRepository _admins;
    private readonly IPisOwnershipRepository _ownership;
    private readonly IAuditService _audit;
    private readonly ICurrentUserService _currentUser;
    private readonly TimeProvider _time;

    public PisBulkOwnershipTransferService(IAdminProfileRepository admins, IPisOwnershipRepository ownership,
        IAuditService audit, ICurrentUserService currentUser, TimeProvider time)
    {
        _admins = admins;
        _ownership = ownership;
        _audit = audit;
        _currentUser = currentUser;
        _time = time;
    }

    public async Task<Result<ApiResult<PisBulkOwnershipTransferDto>>> BulkTransferAsync(PisBulkOwnershipTransferRequestDto request, CancellationToken cancellationToken = default)
    {
        if (!_currentUser.TryGetAdminActor(out AdminActor actor)) return Fail(ApiErrorCodes.Unauthorized, AdminActorExtensions.AdminNotIdentifiedMessage);
        long actorId = actor.Id;

        if (request.SourceAdminId <= 0) return Fail(ApiErrorCodes.ValidationFailed, SourceAdminIdInvalidMessage);
        if (request.TargetAdminId <= 0) return Fail(ApiErrorCodes.ValidationFailed, TargetAdminIdInvalidMessage);
        if (request.SourceAdminId == request.TargetAdminId) return Fail(ApiErrorCodes.ValidationFailed, SourceEqualsTargetMessage);

        // Source: must exist and must be a PIS_ADMIN. Active/inactive status does not block the transfer (supports admin retirement).
        AdminProfile? source = await _admins.GetByIdAsync(request.SourceAdminId, cancellationToken);
        if (source == null) return Fail(ApiErrorCodes.NotFound, SourceNotFoundMessage);
        if (source.Role is not { RoleCode: AdminRoleCodes.PisAdmin, IsActive: true }) return Fail(ApiErrorCodes.ValidationFailed, SourceNotPisAdminMessage);

        // Target: must exist, active, activated, and PIS_ADMIN.
        AdminProfile? target = await _admins.GetByIdAsync(request.TargetAdminId, cancellationToken);
        if (target == null) return Fail(ApiErrorCodes.NotFound, PisOwnershipTransferService.TargetNotFoundMessage);
        if (target.Role is not { RoleCode: AdminRoleCodes.PisAdmin, IsActive: true }) return Fail(ApiErrorCodes.ValidationFailed, PisOwnershipTransferService.TargetNotPisAdminMessage);
        if (!target.IsActive) return Fail(ApiErrorCodes.Conflict, PisOwnershipTransferService.TargetInactiveMessage);
        if (target.ActivationStatus != AdminActivationStatus.Active) return Fail(ApiErrorCodes.Conflict, PisOwnershipTransferService.TargetNotActivatedMessage);

        string? reason = string.IsNullOrWhiteSpace(request.Reason) ? null : request.Reason.Trim();
        DateTime now = IndianStandardTime.ConvertFromUtc(_time.GetUtcNow().UtcDateTime);

        PisBulkOwnershipTransferResult result = await _ownership.BulkTransferAsync(
            request.SourceAdminId, request.TargetAdminId, actorId, reason, now, cancellationToken);

        if (result.Outcome == PisBulkOwnershipTransferOutcome.TargetNotEligible)
            return Fail(ApiErrorCodes.Conflict, PisOwnershipTransferService.TargetInactiveMessage);

        if (result.TransferredCount == 0)
            return Result.Success(new ApiResult<PisBulkOwnershipTransferDto>(BulkNothingToTransferMessage, new PisBulkOwnershipTransferDto { TransferredCount = 0 }));

        await _audit.LogAsync(AuditAction.AdminAction, DomainConstants.AuditResults.Success, userId: actor.Key,
            entityType: nameof(UserPis), entityId: request.SourceAdminId.ToString(),
            correlationId: _currentUser.CorrelationId,
            additionalData: AdminSupport.Audit(
                ("operation", "BulkPisOwnershipTransferred"),
                ("actorAdminId", actorId),
                ("sourceAdminId", request.SourceAdminId),
                ("targetAdminId", request.TargetAdminId),
                ("transferredCount", result.TransferredCount)),
            cancellationToken: cancellationToken);

        return Result.Success(new ApiResult<PisBulkOwnershipTransferDto>(BulkTransferredMessage, new PisBulkOwnershipTransferDto
        {
            TransferredCount = result.TransferredCount,
            TransferredAt = IstTime.FromIst(now)
        }));
    }

    private static Result<ApiResult<PisBulkOwnershipTransferDto>> Fail(string code, string message)
        => Result.Failure<ApiResult<PisBulkOwnershipTransferDto>>(code, message);
}
