using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using NSG.Mobile.Application.Common;
using NSG.Mobile.Application.Interfaces.Repositories;
using NSG.Mobile.Application.Interfaces.Services;
using NSG.Mobile.Application.MobileAuth;
using NSG.Mobile.Application.Pis.Mappings;
using NSG.Mobile.Domain.Common;
using NSG.Mobile.Domain.Constants;
using NSG.Mobile.Domain.Entities;
using NSG.Mobile.Domain.Enums;

namespace NSG.Mobile.Application.MobileAccess;

/// <summary>Current mobile access state of a person, as seen by the PIS Admin. Only what the block decision needs; no hashes, date of birth or sensitive PIS identifiers.</summary>
public sealed class MobileAccessStatusDto
{
    public string NsgNumber { get; set; } = string.Empty;
    public string? FullName { get; set; }
    public string? Rank { get; set; }
    public string? Unit { get; set; }

    /// <summary>Mobile number with only the last four digits visible.</summary>
    public string? MaskedMobileNumber { get; set; }

    public bool PisRecordExists { get; set; }
    public bool MobileProfileExists { get; set; }

    /// <summary>One of <see cref="MobileAccessStatuses"/>.</summary>
    public string Status { get; set; } = MobileAccessStatuses.NoMobileProfile;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public bool? IsActive { get; set; }

    /// <summary>Last successful login, in IST (+05:30).</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public DateTimeOffset? LastLoginAt { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public bool? IsLockedOut { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public DateTimeOffset? LockedUntil { get; set; }

    /// <summary>True when at least one refresh token of the person is neither revoked nor expired.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public bool? HasActiveSession { get; set; }

    /// <summary>When the PIS Admin block was made, in IST (+05:30). Present only while blocked.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public DateTimeOffset? AccessBlockedAt { get; set; }

    /// <summary>Admin Portal profile id of the PIS Admin who blocked access. Present only while blocked.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public int? AccessBlockedBy { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? AccessBlockReason { get; set; }
}

/// <summary>Values of <see cref="MobileAccessStatusDto.Status"/>.</summary>
public static class MobileAccessStatuses
{
    /// <summary>The person is in PIS (or unknown) but has no mobile profile yet.</summary>
    public const string NoMobileProfile = "NO_MOBILE_PROFILE";
    public const string Active = "ACTIVE";

    /// <summary>Blocked through this API. Restorable.</summary>
    public const string AdminBlocked = "ADMIN_BLOCKED";

    /// <summary>Inactive for a reason other than a PIS Admin block. Not restorable here.</summary>
    public const string Inactive = "INACTIVE";
}

/// <summary>Body of the block request. The body itself is optional.</summary>
public sealed class MobileAccessBlockRequestDto
{
    /// <summary>Optional reason, at most 500 characters. Stored on the profile while blocked and in the audit trail.</summary>
    public string? Reason { get; set; }
}

/// <summary>Body of the restore request. The body itself is optional.</summary>
public sealed class MobileAccessRestoreRequestDto
{
    /// <summary>Optional reason, at most 500 characters. Kept in the audit trail.</summary>
    public string? Reason { get; set; }
}

/// <summary>Client-safe messages of the mobile access API.</summary>
public static class MobileAccessMessages
{
    public const string NsgRequired = "NSG number is required.";
    public const string NsgTooLong = "NSG number must not exceed 30 characters.";
    public const string ReasonTooLong = "Reason must not exceed 500 characters.";
    public const string AdminNotIdentified = "The administrator could not be identified. Please sign in again.";
    public const string NothingFound = "No personnel record or mobile profile was found for this NSG number.";
    public const string NoMobileProfile = "A personnel record exists for this NSG number, but the person has not registered in the mobile app, so there is no mobile access to change.";
    public const string Retrieved = "Mobile access status retrieved successfully.";
    public const string RetrievedNoProfile = "Personnel record found, but no mobile profile has been created for this NSG number.";
    public const string Blocked = "Mobile access blocked successfully. All active sessions have been terminated.";
    public const string AlreadyBlocked = "Mobile access is already blocked.";
    public const string Restored = "Mobile access restored successfully. The user must log in again.";
    public const string AlreadyActive = "Mobile access is already active.";
    public const string BlockRefused = "Mobile access is already disabled for another reason, so it cannot be blocked here.";
    public const string RestoreRefused = "Mobile access was not blocked by a PIS Admin, so it cannot be restored here.";
}

/// <summary>PIS Admin lookup, block and restore of a person's mobile access, addressed by NSG number.</summary>
public interface IMobileAccessAdminService
{
    Task<Result<ApiResult<MobileAccessStatusDto>>> GetAsync(string nsgNumber, CancellationToken cancellationToken = default);
    Task<Result<ApiResult<MobileAccessStatusDto>>> BlockAsync(string nsgNumber, string? reason, string? ipAddress, CancellationToken cancellationToken = default);
    Task<Result<ApiResult<MobileAccessStatusDto>>> RestoreAsync(string nsgNumber, string? reason, string? ipAddress, CancellationToken cancellationToken = default);
}

public class MobileAccessAdminService : IMobileAccessAdminService
{
    private const int MaxNsgLength = 30;
    private const int MaxReasonLength = 500;
    private const string ActorSource = "AdminPortal";

    private readonly IMobileUserProfileRepository _profiles;
    private readonly IUserPisRepository _pis;
    private readonly IMobileAccessRepository _access;
    private readonly IAuditService _audit;
    private readonly ICurrentUserService _currentUser;
    private readonly TimeProvider _time;
    private readonly ILogger<MobileAccessAdminService> _logger;

    public MobileAccessAdminService(IMobileUserProfileRepository profiles, IUserPisRepository pis, IMobileAccessRepository access,
        IAuditService audit, ICurrentUserService currentUser, TimeProvider time, ILogger<MobileAccessAdminService> logger)
    {
        _profiles = profiles;
        _pis = pis;
        _access = access;
        _audit = audit;
        _currentUser = currentUser;
        _time = time;
        _logger = logger;
    }

    public async Task<Result<ApiResult<MobileAccessStatusDto>>> GetAsync(string nsgNumber, CancellationToken cancellationToken = default)
    {
        Result<string> nsg = ValidateNsg(nsgNumber);
        if (!nsg.IsSuccess) return Result.Failure<ApiResult<MobileAccessStatusDto>>(nsg.ErrorCode!, nsg.ErrorMessage!);

        MobileUserProfile? profile = await _profiles.GetByNsgNumberAsync(nsg.Data!, cancellationToken);
        UserPis? pis = await _pis.GetByNsgNumberAsync(nsg.Data!, cancellationToken);
        if (profile == null && pis == null)
            return Result.Failure<ApiResult<MobileAccessStatusDto>>(ApiErrorCodes.NotFound, MobileAccessMessages.NothingFound);

        MobileAccessStatusDto dto = await BuildAsync(nsg.Data!, pis, profile, cancellationToken);
        return Result.Success(new ApiResult<MobileAccessStatusDto>(profile == null ? MobileAccessMessages.RetrievedNoProfile : MobileAccessMessages.Retrieved, dto));
    }

    public async Task<Result<ApiResult<MobileAccessStatusDto>>> BlockAsync(string nsgNumber, string? reason, string? ipAddress, CancellationToken cancellationToken = default)
    {
        Result<(string Nsg, string? Reason, int AdminId)> input = Prepare(nsgNumber, reason);
        if (!input.IsSuccess) return Result.Failure<ApiResult<MobileAccessStatusDto>>(input.ErrorCode!, input.ErrorMessage!);
        (string nsg, string? cleanReason, int adminId) = input.Data;

        (MobileUserProfile? profile, UserPis? pis) = await FindAsync(nsg, cancellationToken);
        if (profile == null) return NoProfile(pis);

        MobileAccessChangeOutcome outcome = await _access.BlockAsync(profile.Id, adminId, cleanReason, Now(), cancellationToken);
        switch (outcome)
        {
            case MobileAccessChangeOutcome.NotFound:
                return NoProfile(pis);

            case MobileAccessChangeOutcome.Refused:
                _logger.LogWarning("Block refused: profile {ProfileId} is inactive for another reason. Admin {AdminId}.", profile.Id, adminId);
                return Result.Failure<ApiResult<MobileAccessStatusDto>>(ApiErrorCodes.Conflict, MobileAccessMessages.BlockRefused);

            case MobileAccessChangeOutcome.NoChange:
                return Result.Success(new ApiResult<MobileAccessStatusDto>(MobileAccessMessages.AlreadyBlocked, await ReloadAsync(profile.Id, nsg, pis, cancellationToken)));
        }

        _logger.LogInformation("Mobile access blocked: profile {ProfileId} by admin {AdminId}.", profile.Id, adminId);
        await AuditAsync(AuditAction.MobileAccessBlocked, "Block", nsg, profile.Id, adminId, cleanReason, ipAddress);
        return Result.Success(new ApiResult<MobileAccessStatusDto>(MobileAccessMessages.Blocked, await ReloadAsync(profile.Id, nsg, pis, cancellationToken)));
    }

    public async Task<Result<ApiResult<MobileAccessStatusDto>>> RestoreAsync(string nsgNumber, string? reason, string? ipAddress, CancellationToken cancellationToken = default)
    {
        Result<(string Nsg, string? Reason, int AdminId)> input = Prepare(nsgNumber, reason);
        if (!input.IsSuccess) return Result.Failure<ApiResult<MobileAccessStatusDto>>(input.ErrorCode!, input.ErrorMessage!);
        (string nsg, string? cleanReason, int adminId) = input.Data;

        (MobileUserProfile? profile, UserPis? pis) = await FindAsync(nsg, cancellationToken);
        if (profile == null) return NoProfile(pis);

        MobileAccessChangeOutcome outcome = await _access.RestoreAsync(profile.Id, adminId, Now(), cancellationToken);
        switch (outcome)
        {
            case MobileAccessChangeOutcome.NotFound:
                return NoProfile(pis);

            case MobileAccessChangeOutcome.Refused:
                _logger.LogWarning("Restore refused: profile {ProfileId} is inactive but was not blocked by a PIS Admin. Admin {AdminId}.", profile.Id, adminId);
                return Result.Failure<ApiResult<MobileAccessStatusDto>>(ApiErrorCodes.Conflict, MobileAccessMessages.RestoreRefused);

            case MobileAccessChangeOutcome.NoChange:
                return Result.Success(new ApiResult<MobileAccessStatusDto>(MobileAccessMessages.AlreadyActive, await ReloadAsync(profile.Id, nsg, pis, cancellationToken)));
        }

        _logger.LogInformation("Mobile access restored: profile {ProfileId} by admin {AdminId}.", profile.Id, adminId);
        await AuditAsync(AuditAction.MobileAccessRestored, "Restore", nsg, profile.Id, adminId, cleanReason, ipAddress);
        return Result.Success(new ApiResult<MobileAccessStatusDto>(MobileAccessMessages.Restored, await ReloadAsync(profile.Id, nsg, pis, cancellationToken)));
    }

    // ───────────────────────── helpers ─────────────────────────

    private DateTime Now() => IndianStandardTime.ConvertFromUtc(_time.GetUtcNow().UtcDateTime);

    private static Result<string> ValidateNsg(string? nsgNumber)
    {
        string nsg = (nsgNumber ?? string.Empty).Trim();
        if (nsg.Length == 0) return Result.Failure<string>(ApiErrorCodes.ValidationFailed, MobileAccessMessages.NsgRequired);
        if (nsg.Length > MaxNsgLength) return Result.Failure<string>(ApiErrorCodes.ValidationFailed, MobileAccessMessages.NsgTooLong);
        return Result.Success(nsg);
    }

    /// <summary>NSG number, trimmed reason (null when blank) and the acting admin, all validated. The admin always comes from the token.</summary>
    private Result<(string Nsg, string? Reason, int AdminId)> Prepare(string nsgNumber, string? reason)
    {
        Result<string> nsg = ValidateNsg(nsgNumber);
        if (!nsg.IsSuccess) return Result.Failure<(string, string?, int)>(nsg.ErrorCode!, nsg.ErrorMessage!);

        string? clean = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        if (clean is { Length: > MaxReasonLength })
            return Result.Failure<(string, string?, int)>(ApiErrorCodes.ValidationFailed, MobileAccessMessages.ReasonTooLong);

        long? adminProfileId = _currentUser.AdminProfileId;
        if (adminProfileId is not > 0 or > int.MaxValue)
            return Result.Failure<(string, string?, int)>(ApiErrorCodes.Unauthorized, MobileAccessMessages.AdminNotIdentified);

        return Result.Success((nsg.Data!, clean, (int)adminProfileId.Value));
    }

    private async Task<(MobileUserProfile? Profile, UserPis? Pis)> FindAsync(string nsg, CancellationToken cancellationToken)
        => (await _profiles.GetByNsgNumberAsync(nsg, cancellationToken), await _pis.GetByNsgNumberAsync(nsg, cancellationToken));

    private static Result<ApiResult<MobileAccessStatusDto>> NoProfile(UserPis? pis)
        => Result.Failure<ApiResult<MobileAccessStatusDto>>(ApiErrorCodes.NotFound, pis == null ? MobileAccessMessages.NothingFound : MobileAccessMessages.NoMobileProfile);

    private async Task<MobileAccessStatusDto> ReloadAsync(long profileId, string nsg, UserPis? pis, CancellationToken cancellationToken)
        => await BuildAsync(nsg, pis, await _profiles.GetByIdAsync(profileId, cancellationToken), cancellationToken);

    private async Task<MobileAccessStatusDto> BuildAsync(string nsg, UserPis? pis, MobileUserProfile? profile, CancellationToken cancellationToken)
    {
        string? mobile = profile?.MobileNumber ?? pis?.MobileNumber;
        MobileAccessStatusDto dto = new()
        {
            NsgNumber = profile?.NsgNumber ?? pis?.NsgNumber ?? nsg,
            FullName = pis?.FullName,
            Rank = pis?.NsgRank,
            Unit = pis?.NsgUnit,
            MaskedMobileNumber = string.IsNullOrEmpty(mobile) ? null : PisMasking.Mask(mobile),
            PisRecordExists = pis != null,
            MobileProfileExists = profile != null
        };
        if (profile == null) return dto;

        DateTime now = Now();
        dto.IsActive = profile.IsActive;
        dto.Status = profile.IsAdminBlocked ? MobileAccessStatuses.AdminBlocked : profile.IsActive ? MobileAccessStatuses.Active : MobileAccessStatuses.Inactive;
        dto.LastLoginAt = profile.LastLoginAtIst.HasValue ? IstTime.FromIst(profile.LastLoginAtIst.Value) : null;
        dto.IsLockedOut = profile.LockedUntilIst.HasValue && profile.LockedUntilIst.Value > now;
        dto.LockedUntil = dto.IsLockedOut == true ? IstTime.FromIst(profile.LockedUntilIst!.Value) : null;
        dto.HasActiveSession = await _access.HasLiveSessionAsync(profile.Id, now, cancellationToken);
        if (profile.IsAdminBlocked)
        {
            dto.AccessBlockedAt = IstTime.FromIst(profile.AccessBlockedAtIst!.Value);
            dto.AccessBlockedBy = profile.AccessBlockedBy;
            dto.AccessBlockReason = profile.AccessBlockReason;
        }

        return dto;
    }

    /// <summary>
    /// Records the action after the change has committed. A failure here never undoes the block or restore: it is logged with the
    /// details needed to reconstruct the entry, and the caller still gets the success answer. Runs without the request's
    /// cancellation token so a client disconnect cannot drop the audit entry of a change that already happened.
    /// </summary>
    private async Task AuditAsync(AuditAction action, string actionName, string nsg, long profileId, int adminId, string? reason, string? ipAddress)
    {
        try
        {
            string data = JsonSerializer.Serialize(new
            {
                action = actionName,
                result = DomainConstants.AuditResults.Success,
                nsgNumber = nsg,
                mobileProfileId = profileId,
                adminProfileId = adminId,
                actorSource = ActorSource,
                reason
            });
            await _audit.LogAsync(action, DomainConstants.AuditResults.Success, userId: adminId, entityType: nameof(MobileUserProfile),
                entityId: profileId.ToString(), ipAddress: ipAddress, correlationId: _currentUser.CorrelationId, additionalData: data,
                cancellationToken: CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Audit of {AuditAction} failed after the change was committed: profile {ProfileId}, admin {AdminId}, actor source {ActorSource}, CorrelationId {CorrelationId}.",
                action, profileId, adminId, ActorSource, _currentUser.CorrelationId);
        }
    }
}
