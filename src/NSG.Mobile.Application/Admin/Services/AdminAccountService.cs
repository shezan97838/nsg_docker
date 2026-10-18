using NSG.Mobile.Application.Admin.DTOs;
using NSG.Mobile.Application.Admin.Mappings;
using NSG.Mobile.Application.Authentication.Interfaces;
using NSG.Mobile.Application.Common;
using NSG.Mobile.Application.Interfaces.Repositories;
using NSG.Mobile.Application.Interfaces.Services;
using NSG.Mobile.Application.MobileAuth.Validators;
using NSG.Mobile.Domain.Common;
using NSG.Mobile.Domain.Constants;
using NSG.Mobile.Domain.Entities;
using NSG.Mobile.Domain.Enums;

namespace NSG.Mobile.Application.Admin.Services;

/// <summary>A message plus the affected account, for changes that answer with both.</summary>
public sealed record AdminChangeResult(string Message, AdminAccountDto? Account);

/// <summary>Super Admin management of Admin Portal accounts. The caller's identity is always passed in from the token, never from a request body.</summary>
public interface IAdminAccountService
{
    Task<Result<AdminChangeResult>> CreateAsync(CreateAdminAccountRequestDto request, long actorAdminId, string? ipAddress, CancellationToken cancellationToken = default);
    Task<Result<PaginatedResponse<AdminAccountDto>>> GetPagedAsync(int pageNumber, int pageSize, AdminAccountFilter filter, CancellationToken cancellationToken = default);
    Task<Result<AdminAccountDto>> GetByIdAsync(long id, CancellationToken cancellationToken = default);
    Task<Result<AdminChangeResult>> DeactivateAsync(long id, long actorAdminId, string? ipAddress, CancellationToken cancellationToken = default);
    Task<Result<AdminChangeResult>> ReactivateAsync(long id, long actorAdminId, string? ipAddress, CancellationToken cancellationToken = default);
    Task<Result<IReadOnlyList<AdminRoleDto>>> GetRolesAsync(CancellationToken cancellationToken = default);
}

public class AdminAccountService : IAdminAccountService
{
    private readonly IAdminProfileRepository _profiles;
    private readonly IAdminRoleRepository _roles;
    private readonly IAdminRefreshTokenRepository _sessions;
    private readonly IUserPisRepository _pis;
    private readonly IAuditService _audit;
    private readonly ICurrentUserService _currentUser;
    private readonly TimeProvider _time;

    public AdminAccountService(
        IAdminProfileRepository profiles, IAdminRoleRepository roles, IAdminRefreshTokenRepository sessions, IUserPisRepository pis,
        IAuditService audit, ICurrentUserService currentUser, TimeProvider time)
    {
        _profiles = profiles;
        _roles = roles;
        _sessions = sessions;
        _pis = pis;
        _audit = audit;
        _currentUser = currentUser;
        _time = time;
    }

    private DateTime Now() => IndianStandardTime.ConvertFromUtc(_time.GetUtcNow().UtcDateTime);

    public async Task<Result<AdminChangeResult>> CreateAsync(CreateAdminAccountRequestDto request, long actorAdminId, string? ipAddress, CancellationToken cancellationToken = default)
    {
        string nsg = MobileAuthInput.Nsg(request.NsgNumber);

        // PIS decides who this person is. The supplied mobile number and date of birth are only checks against it.
        UserPis? person = await AdminSupport.FindMatchingPersonnelAsync(_pis, nsg, MobileAuthInput.Mobile(request.MobileNumber), request.DateOfBirth, cancellationToken);
        if (person == null)
            return Result.Failure<AdminChangeResult>(ApiErrorCodes.NotFound, AdminMessages.PersonnelNotFound);

        AdminRole? role = await _roles.GetByCodeAsync(request.RoleCode.Trim().ToUpperInvariant(), cancellationToken);
        if (role == null || !role.IsActive)
            return Result.Failure<AdminChangeResult>(ApiErrorCodes.ValidationFailed, AdminMessages.RoleInvalid);

        if (await _profiles.GetByNsgNumberAsync(person.NsgNumber, cancellationToken) != null)
            return Result.Failure<AdminChangeResult>(ApiErrorCodes.Conflict, AdminMessages.AccountExists);

        // Pending, enabled, and above all without a password: the person chooses their own during activation.
        AdminProfile profile = new()
        {
            NsgNumber = person.NsgNumber,
            RoleId = role.Id,
            ActivationStatus = AdminActivationStatus.PendingActivation,
            IsActive = true,
            CreatedAtIst = Now(),
            CreatedBy = actorAdminId
        };

        if (await _profiles.AddAsync(profile, cancellationToken) == AdminCreateOutcome.DuplicateNsgNumber)
            return Result.Failure<AdminChangeResult>(ApiErrorCodes.Conflict, AdminMessages.AccountExists);

        await _audit.LogAsync(AuditAction.AdminAction, DomainConstants.AuditResults.Success, entityType: nameof(AdminProfile), entityId: profile.Id.ToString(),
            ipAddress: ipAddress, correlationId: _currentUser.CorrelationId,
            additionalData: AdminSupport.Audit(("operation", "AdminAccountCreated"), ("actorAdminId", actorAdminId), ("roleCode", role.RoleCode)), cancellationToken: cancellationToken);

        AdminAccountRow? row = await _profiles.GetRowByIdAsync(profile.Id, cancellationToken);
        return Result.Success(new AdminChangeResult(AdminMessages.AccountCreated, row?.ToDto()));
    }

    public async Task<Result<PaginatedResponse<AdminAccountDto>>> GetPagedAsync(int pageNumber, int pageSize, AdminAccountFilter filter, CancellationToken cancellationToken = default)
    {
        (IReadOnlyList<AdminAccountRow> items, int total) = await _profiles.GetPagedAsync(pageNumber, pageSize, filter, cancellationToken);
        return Result.Success(new PaginatedResponse<AdminAccountDto>(items.Select(r => r.ToDto()).ToList(), total, pageNumber, pageSize));
    }

    public async Task<Result<AdminAccountDto>> GetByIdAsync(long id, CancellationToken cancellationToken = default)
    {
        AdminAccountRow? row = await _profiles.GetRowByIdAsync(id, cancellationToken);
        return row == null
            ? Result.Failure<AdminAccountDto>(ApiErrorCodes.NotFound, AdminMessages.AccountNotFound)
            : Result.Success(row.ToDto());
    }

    public async Task<Result<AdminChangeResult>> DeactivateAsync(long id, long actorAdminId, string? ipAddress, CancellationToken cancellationToken = default)
    {
        if (id == actorAdminId)
            return Result.Failure<AdminChangeResult>(ApiErrorCodes.Conflict, AdminMessages.CannotDeactivateSelf);

        AdminAccountRow? row = await _profiles.GetRowByIdAsync(id, cancellationToken);
        if (row == null)
            return Result.Failure<AdminChangeResult>(ApiErrorCodes.NotFound, AdminMessages.AccountNotFound);
        if (!row.Profile.IsActive)
            return Result.Success(new AdminChangeResult(AdminMessages.AccountAlreadyInactive, row.ToDto()));

        // There must always be someone who can manage accounts.
        bool isLiveSuperAdmin = row.Profile.Role?.RoleCode == AdminRoleCodes.SuperAdmin && row.Profile.ActivationStatus == AdminActivationStatus.Active;
        if (isLiveSuperAdmin && await _profiles.CountActiveSuperAdminsAsync(cancellationToken) <= 1)
            return Result.Failure<AdminChangeResult>(ApiErrorCodes.Conflict, AdminMessages.LastSuperAdmin);

        DateTime now = Now();
        AdminStateChangeOutcome outcome = await _profiles.SetEnabledAsync(id, false, actorAdminId, now, cancellationToken);
        if (outcome == AdminStateChangeOutcome.NotFound)
            return Result.Failure<AdminChangeResult>(ApiErrorCodes.NotFound, AdminMessages.AccountNotFound);

        // Sessions end now; the access token stops working at its next request, the refresh token at its next use.
        await _sessions.RevokeAllForProfileAsync(id, AdminSessionRevokeReasons.AccountDeactivated, now, cancellationToken);

        await _audit.LogAsync(AuditAction.AdminAction, DomainConstants.AuditResults.Success, entityType: nameof(AdminProfile), entityId: id.ToString(),
            ipAddress: ipAddress, correlationId: _currentUser.CorrelationId,
            additionalData: AdminSupport.Audit(("operation", "AdminAccountDeactivated"), ("actorAdminId", actorAdminId)), cancellationToken: cancellationToken);

        return Result.Success(new AdminChangeResult(AdminMessages.AccountDeactivated, (await _profiles.GetRowByIdAsync(id, cancellationToken))?.ToDto()));
    }

    public async Task<Result<AdminChangeResult>> ReactivateAsync(long id, long actorAdminId, string? ipAddress, CancellationToken cancellationToken = default)
    {
        AdminAccountRow? row = await _profiles.GetRowByIdAsync(id, cancellationToken);
        if (row == null)
            return Result.Failure<AdminChangeResult>(ApiErrorCodes.NotFound, AdminMessages.AccountNotFound);
        if (row.Profile.IsActive)
            return Result.Success(new AdminChangeResult(AdminMessages.AccountAlreadyActive, row.ToDto()));

        // Reactivation re-enables the account as it was: an account that never activated still has to activate.
        await _profiles.SetEnabledAsync(id, true, actorAdminId, Now(), cancellationToken);

        await _audit.LogAsync(AuditAction.AdminAction, DomainConstants.AuditResults.Success, entityType: nameof(AdminProfile), entityId: id.ToString(),
            ipAddress: ipAddress, correlationId: _currentUser.CorrelationId,
            additionalData: AdminSupport.Audit(("operation", "AdminAccountReactivated"), ("actorAdminId", actorAdminId)), cancellationToken: cancellationToken);

        return Result.Success(new AdminChangeResult(AdminMessages.AccountReactivated, (await _profiles.GetRowByIdAsync(id, cancellationToken))?.ToDto()));
    }

    public async Task<Result<IReadOnlyList<AdminRoleDto>>> GetRolesAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyList<AdminRole> roles = await _roles.GetAllAsync(cancellationToken);
        return Result.Success<IReadOnlyList<AdminRoleDto>>(roles.Select(r => r.ToDto()).ToList());
    }
}
