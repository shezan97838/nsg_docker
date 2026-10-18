using NSG.Mobile.Application.Common;
using NSG.Mobile.Application.Interfaces.Repositories;
using NSG.Mobile.Application.Interfaces.Services;
using NSG.Mobile.Application.MobileAuth.Validators;
using NSG.Mobile.Domain.Common;
using NSG.Mobile.Domain.Constants;
using NSG.Mobile.Domain.Entities;
using NSG.Mobile.Domain.Enums;

namespace NSG.Mobile.Application.Admin.Services;

/// <summary>
/// Creates the very first Super Admin (ADR-017). It is run on purpose by an operator through the API's
/// <c>bootstrap-super-admin</c> command, never at startup and never over HTTP. It creates a <c>PendingActivation</c> account with no
/// password, for a person who exists in PIS; that person then activates it with an OTP like any other admin. It refuses to run once
/// any Super Admin exists, so it cannot be used later to add one around the normal workflow.
/// </summary>
public interface IAdminBootstrapService
{
    Task<Result<long>> CreateFirstSuperAdminAsync(string nsgNumber, CancellationToken cancellationToken = default);
}

public class AdminBootstrapService : IAdminBootstrapService
{
    private readonly IAdminProfileRepository _profiles;
    private readonly IAdminRoleRepository _roles;
    private readonly IUserPisRepository _pis;
    private readonly IAuditService _audit;
    private readonly TimeProvider _time;

    public AdminBootstrapService(IAdminProfileRepository profiles, IAdminRoleRepository roles, IUserPisRepository pis, IAuditService audit, TimeProvider time)
    {
        _profiles = profiles;
        _roles = roles;
        _pis = pis;
        _audit = audit;
        _time = time;
    }

    public async Task<Result<long>> CreateFirstSuperAdminAsync(string nsgNumber, CancellationToken cancellationToken = default)
    {
        if (await _profiles.AnySuperAdminAsync(cancellationToken))
            return Result.Failure<long>(ApiErrorCodes.Conflict, "A Super Admin already exists. Further accounts are created by a Super Admin through the Admin Portal API.");

        UserPis? person = await _pis.GetByNsgNumberAsync(MobileAuthInput.Nsg(nsgNumber), cancellationToken);
        if (person is not { IsActive: true, MobileNumber: not null, DateOfBirth: not null })
            return Result.Failure<long>(ApiErrorCodes.NotFound, "No active personnel record with a registered mobile number and date of birth exists for this NSG number.");

        AdminRole? role = await _roles.GetByCodeAsync(AdminRoleCodes.SuperAdmin, cancellationToken);
        if (role is not { IsActive: true })
            return Result.Failure<long>(ApiErrorCodes.InternalError, "The SUPER_ADMIN role is missing. Apply the database migrations first.");

        AdminProfile profile = new()
        {
            NsgNumber = person.NsgNumber,
            RoleId = role.Id,
            ActivationStatus = AdminActivationStatus.PendingActivation,
            IsActive = true,
            CreatedAtIst = IndianStandardTime.ConvertFromUtc(_time.GetUtcNow().UtcDateTime),
            CreatedBy = null
        };

        if (await _profiles.AddAsync(profile, cancellationToken) == AdminCreateOutcome.DuplicateNsgNumber)
            return Result.Failure<long>(ApiErrorCodes.Conflict, AdminMessages.AccountExists);

        await _audit.LogAsync(AuditAction.AdminAction, DomainConstants.AuditResults.Success, entityType: nameof(AdminProfile), entityId: profile.Id.ToString(),
            additionalData: AdminSupport.Audit(("operation", "SuperAdminBootstrapped"), ("roleCode", AdminRoleCodes.SuperAdmin)), cancellationToken: cancellationToken);

        return Result.Success(profile.Id);
    }
}
