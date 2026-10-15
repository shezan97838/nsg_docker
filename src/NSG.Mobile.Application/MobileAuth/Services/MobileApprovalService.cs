using System.Text.Json;
using NSG.Mobile.Application.Common;
using NSG.Mobile.Application.Interfaces.Repositories;
using NSG.Mobile.Application.Interfaces.Services;
using NSG.Mobile.Application.MobileAuth.DTOs;
using NSG.Mobile.Application.Pis.Mappings;
using NSG.Mobile.Domain.Common;
using NSG.Mobile.Domain.Constants;
using NSG.Mobile.Domain.Entities;
using NSG.Mobile.Domain.Enums;

namespace NSG.Mobile.Application.MobileAuth.Services;

/// <summary>IT Admin review of mobile activation requests.</summary>
public interface IMobileApprovalService
{
    /// <summary>One page of requests, newest first, optionally filtered by status and a search on NSG number or name.</summary>
    Task<Result<PaginatedResponse<MobileApprovalRequestDto>>> GetPagedAsync(
        int pageNumber, int pageSize, MobileAppActivationStatus? status, string? search, CancellationToken cancellationToken = default);

    /// <summary>Approves or rejects a Pending request. The reviewer and the timestamps come from the server.</summary>
    Task<Result<ApiResult<MobileApprovalRequestDto>>> DecideAsync(long id, MobileApprovalDecisionRequestDto request, CancellationToken cancellationToken = default);
}

public class MobileApprovalService : IMobileApprovalService
{
    /// <summary>Message returned (instead of an empty list) when a search or filter matches nothing.</summary>
    public const string NoneFoundMessage = "No mobile approval requests found.";

    private readonly IApproveMobileUserRepository _requests;
    private readonly IAuditService _audit;
    private readonly ICurrentUserService _currentUser;
    private readonly IReviewerIdentityResolver _reviewer;
    private readonly TimeProvider _time;

    public MobileApprovalService(IApproveMobileUserRepository requests, IAuditService audit, ICurrentUserService currentUser,
        IReviewerIdentityResolver reviewer, TimeProvider time)
    {
        _requests = requests;
        _audit = audit;
        _currentUser = currentUser;
        _reviewer = reviewer;
        _time = time;
    }

    public async Task<Result<PaginatedResponse<MobileApprovalRequestDto>>> GetPagedAsync(
        int pageNumber, int pageSize, MobileAppActivationStatus? status, string? search, CancellationToken cancellationToken = default)
    {
        string? term = string.IsNullOrWhiteSpace(search) ? null : search.Trim();
        (IReadOnlyList<ApprovalRequestRow> items, int total) = await _requests.GetPagedAsync(pageNumber, pageSize, status, term, cancellationToken);

        return Result.Success(new PaginatedResponse<MobileApprovalRequestDto>(
            items.Select(r => ToDto(r.Request, r.FullName)).ToList(), total, pageNumber, pageSize));
    }

    public async Task<Result<ApiResult<MobileApprovalRequestDto>>> DecideAsync(long id, MobileApprovalDecisionRequestDto request, CancellationToken cancellationToken = default)
    {
        // The reviewer is always the authenticated admin; a request without a resolvable admin identity is refused.
        ReviewerIdentity? reviewer = _reviewer.Resolve();
        int? approverId = reviewer?.UserId;
        if (approverId == null)
        {
            return Result.Failure<ApiResult<MobileApprovalRequestDto>>(ApiErrorCodes.Unauthorized, "The reviewer could not be identified. Please sign in again.");
        }

        string? remarks = string.IsNullOrWhiteSpace(request.Remarks) ? null : request.Remarks.Trim();
        DateTime now = IndianStandardTime.ConvertFromUtc(_time.GetUtcNow().UtcDateTime);

        ApprovalDecisionResult result = await _requests.DecideAsync(id, request.Decision, approverId.Value, remarks, now, cancellationToken);

        switch (result.Outcome)
        {
            case ApprovalDecisionOutcome.NotFound:
                return Result.Failure<ApiResult<MobileApprovalRequestDto>>(ApiErrorCodes.NotFound, "Mobile approval request not found.");

            case ApprovalDecisionOutcome.AlreadyDecided:
                return Result.Failure<ApiResult<MobileApprovalRequestDto>>(ApiErrorCodes.Conflict,
                    $"This request has already been {result.CurrentStatus.ToString()!.ToLowerInvariant()} and cannot be decided again.");

            case ApprovalDecisionOutcome.PersonnelRecordMissing:
                return Result.Failure<ApiResult<MobileApprovalRequestDto>>(ApiErrorCodes.Conflict,
                    "The personnel record for this request no longer exists, so no decision was recorded.");
        }

        bool approved = request.Decision == MobileAppActivationStatus.Approved;
        await _audit.LogAsync(approved ? AuditAction.ITApprovalGranted : AuditAction.ITApprovalRejected, DomainConstants.AuditResults.Success,
            userId: approverId, entityType: nameof(ApproveMobileUser), entityId: id.ToString(), correlationId: _currentUser.CorrelationId,
            additionalData: reviewer!.DisplayName == null ? null : JsonSerializer.Serialize(new { reviewer = reviewer.DisplayName }),
            cancellationToken: cancellationToken);

        string message = approved ? "Mobile approval request approved successfully." : "Mobile approval request rejected successfully.";
        return Result.Success(new ApiResult<MobileApprovalRequestDto>(message, ToDto(result.Request!, null)));
    }

    private static MobileApprovalRequestDto ToDto(ApproveMobileUser r, string? fullName) => new()
    {
        Id = r.Id,
        NsgNumber = r.NsgNumber,
        FullName = fullName,
        MaskedMobileNumber = PisMasking.Mask(r.MobileNumber),
        Status = r.ApprovalStatus.ToString(),
        RequestedAt = IstTime.FromIst(r.RequestedAtIst),
        ReviewedBy = r.ApprovedBy,
        ReviewedAt = r.ApprovedAtIst.HasValue ? IstTime.FromIst(r.ApprovedAtIst.Value) : null,
        Remarks = r.Remarks
    };
}
