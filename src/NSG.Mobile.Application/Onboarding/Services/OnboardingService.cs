using Microsoft.Extensions.Logging;
using NSG.Mobile.Application.Authentication.Mappings;
using NSG.Mobile.Application.Common;
using NSG.Mobile.Application.Interfaces.Repositories;
using NSG.Mobile.Application.Interfaces.Services;
using NSG.Mobile.Application.Onboarding.DTOs;
using NSG.Mobile.Domain.Common;
using NSG.Mobile.Domain.Constants;
using NSG.Mobile.Domain.Enums;

namespace NSG.Mobile.Application.Onboarding.Services;

/// <summary>
/// Onboarding service for IT Cell approval workflow.
/// SOW §3.7 Step 2: "IT Cell verifies identity against HRMS records. Upon approval, the user is notified."
/// </summary>
public class OnboardingService : IOnboardingService
{
    private readonly IUserRepository _userRepository;
    private readonly IAuditService _auditService;
    private readonly ICurrentUserService _currentUserService;
    private readonly ILogger<OnboardingService> _logger;

    public OnboardingService(
        IUserRepository userRepository,
        IAuditService auditService,
        ICurrentUserService currentUserService,
        ILogger<OnboardingService> logger)
    {
        _userRepository = userRepository;
        _auditService = auditService;
        _currentUserService = currentUserService;
        _logger = logger;
    }

    public async Task<Result<PaginatedResponse<PendingRegistrationDto>>> GetPendingRegistrationsAsync(
        int pageNumber, int pageSize, CancellationToken cancellationToken = default)
    {
        var users = await _userRepository.GetByStatusAsync(
            UserStatus.PendingITApproval, pageNumber, pageSize, cancellationToken);
        var totalCount = await _userRepository.CountByStatusAsync(
            UserStatus.PendingITApproval, cancellationToken);

        var dtos = users.Select(u => u.ToPendingDto()).ToList();

        return Result.Success(new PaginatedResponse<PendingRegistrationDto>(
            dtos, totalCount, pageNumber, pageSize));
    }

    public async Task<Result> ApproveRegistrationAsync(
        int userId, string? reason, CancellationToken cancellationToken = default)
    {
        var user = await _userRepository.GetByIdAsync(userId, cancellationToken);
        if (user == null)
            return Result.Failure(ApiErrorCodes.NotFound, "User not found.");

        if (user.Status != UserStatus.PendingITApproval)
            return Result.Failure(ApiErrorCodes.ValidationFailed,
                $"User is in '{user.Status}' state and cannot be approved.");

        user.Status = UserStatus.Active;
        user.IsActive = true;
        user.ApprovedBy = _currentUserService.UserId;
        user.ApprovedAt = IndianStandardTime.Now;
        user.UpdatedAt = IndianStandardTime.Now;

        await _userRepository.UpdateAsync(user, cancellationToken);

        await _auditService.LogAsync(AuditAction.ITApprovalGranted, DomainConstants.AuditResults.Success,
            userId: _currentUserService.UserId, entityType: "User", entityId: userId.ToString(),
            correlationId: _currentUserService.CorrelationId,
            additionalData: reason != null ? $"{{\"reason\":\"{reason}\"}}" : null,
            cancellationToken: cancellationToken);

        _logger.LogInformation("User {UserId} approved by IT admin {AdminId}", userId, _currentUserService.UserId);

        // TODO: Send push notification to user about approval (Phase 2)

        return Result.Success();
    }

    public async Task<Result> RejectRegistrationAsync(
        int userId, string? reason, CancellationToken cancellationToken = default)
    {
        var user = await _userRepository.GetByIdAsync(userId, cancellationToken);
        if (user == null)
            return Result.Failure(ApiErrorCodes.NotFound, "User not found.");

        if (user.Status != UserStatus.PendingITApproval)
            return Result.Failure(ApiErrorCodes.ValidationFailed,
                $"User is in '{user.Status}' state and cannot be rejected.");

        user.Status = UserStatus.Suspended;
        user.IsActive = false;
        user.UpdatedAt = IndianStandardTime.Now;

        await _userRepository.UpdateAsync(user, cancellationToken);

        await _auditService.LogAsync(AuditAction.ITApprovalRejected, DomainConstants.AuditResults.Success,
            userId: _currentUserService.UserId, entityType: "User", entityId: userId.ToString(),
            correlationId: _currentUserService.CorrelationId,
            additionalData: reason != null ? $"{{\"reason\":\"{reason}\"}}" : null,
            cancellationToken: cancellationToken);

        return Result.Success();
    }
}

/// <summary>Onboarding service interface for IT approval workflow.</summary>
public interface IOnboardingService
{
    Task<Result<PaginatedResponse<PendingRegistrationDto>>> GetPendingRegistrationsAsync(
        int pageNumber, int pageSize, CancellationToken cancellationToken = default);
    Task<Result> ApproveRegistrationAsync(int userId, string? reason, CancellationToken cancellationToken = default);
    Task<Result> RejectRegistrationAsync(int userId, string? reason, CancellationToken cancellationToken = default);
}
