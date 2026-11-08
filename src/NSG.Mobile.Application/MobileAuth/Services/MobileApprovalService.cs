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

/// <summary>
/// PIS Admin review of mobile activation requests. A PIS Admin sees and decides only the requests of personnel CURRENTLY assigned to them
/// (<c>users_pis.assigned_to</c>); the acting admin is the Admin Portal token's <c>admin_profile_id</c>, never a request value.
/// </summary>
public interface IMobileApprovalService
{
    /// <summary>One page of the caller's own requests, newest first, optionally filtered by status and a search on NSG number or name.</summary>
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
    private readonly TimeProvider _time;

    public MobileApprovalService(IApproveMobileUserRepository requests, IAuditService audit, ICurrentUserService currentUser,
        TimeProvider time)
    {
        _requests = requests;
        _audit = audit;
        _currentUser = currentUser;
        _time = time;
    }

    /// <summary>Shown when the caller is not the PIS Admin the request's personnel record is assigned to (or it is assigned to nobody).</summary>
    public const string NotYourRequestMessage = "This registration request is not assigned to you.";

    private const string ReviewerNotIdentifiedMessage = "The reviewer could not be identified. Please sign in again.";

    /// <summary>
    /// The acting PIS Admin's <c>admin_profiles.id</c>, from the validated Admin Portal token only. Null for any other identity (a staff token
    /// has none), so such a caller can neither list nor decide anything.
    /// </summary>
    private long? ActingAdminId => _currentUser.AdminProfileId is > 0 and var id ? id : null;

    public async Task<Result<PaginatedResponse<MobileApprovalRequestDto>>> GetPagedAsync(
        int pageNumber, int pageSize, MobileAppActivationStatus? status, string? search, CancellationToken cancellationToken = default)
    {
        if (ActingAdminId is not long adminId)
            return Result.Failure<PaginatedResponse<MobileApprovalRequestDto>>(ApiErrorCodes.Unauthorized, ReviewerNotIdentifiedMessage);

        string? term = string.IsNullOrWhiteSpace(search) ? null : search.Trim();
        (IReadOnlyList<ApprovalRequestRow> items, int total) = await _requests.GetPagedAsync(adminId, pageNumber, pageSize, status, term, cancellationToken);

        return Result.Success(new PaginatedResponse<MobileApprovalRequestDto>(
            items.Select(r => ToDto(r.Request, r.FullName)).ToList(), total, pageNumber, pageSize));
    }

    public async Task<Result<ApiResult<MobileApprovalRequestDto>>> DecideAsync(long id, MobileApprovalDecisionRequestDto request, CancellationToken cancellationToken = default)
    {
        // The reviewer is always the authenticated PIS Admin. approved_by is an int column: an id that does not fit is refused, never truncated
        // (a profile id above int.MaxValue would otherwise be recorded as a different admin).
        if (ActingAdminId is not long adminId || adminId > int.MaxValue)
            return Result.Failure<ApiResult<MobileApprovalRequestDto>>(ApiErrorCodes.Unauthorized, ReviewerNotIdentifiedMessage);

        string? remarks = string.IsNullOrWhiteSpace(request.Remarks) ? null : request.Remarks.Trim();
        DateTime now = IndianStandardTime.ConvertFromUtc(_time.GetUtcNow().UtcDateTime);

        // Ownership (users_pis.assigned_to = this admin) is verified and locked inside the repository's transaction, not here.
        ApprovalDecisionResult result = await _requests.DecideAsync(id, request.Decision, adminId, remarks, now, cancellationToken);

        switch (result.Outcome)
        {
            case ApprovalDecisionOutcome.NotFound:
                return Result.Failure<ApiResult<MobileApprovalRequestDto>>(ApiErrorCodes.NotFound, "Mobile approval request not found.");

            case ApprovalDecisionOutcome.NotOwner:
                return Result.Failure<ApiResult<MobileApprovalRequestDto>>(ApiErrorCodes.Forbidden, NotYourRequestMessage);

            case ApprovalDecisionOutcome.AlreadyDecided:
                return Result.Failure<ApiResult<MobileApprovalRequestDto>>(ApiErrorCodes.Conflict,
                    $"This request has already been {result.CurrentStatus.ToString()!.ToLowerInvariant()} and cannot be decided again.");

            case ApprovalDecisionOutcome.PersonnelRecordMissing:
                return Result.Failure<ApiResult<MobileApprovalRequestDto>>(ApiErrorCodes.Conflict,
                    "The personnel record for this request no longer exists, so no decision was recorded.");
        }

        bool approved = request.Decision == MobileAppActivationStatus.Approved;
        await _audit.LogAsync(approved ? AuditAction.ITApprovalGranted : AuditAction.ITApprovalRejected, DomainConstants.AuditResults.Success,
            userId: (int)adminId, entityType: nameof(ApproveMobileUser), entityId: id.ToString(), correlationId: _currentUser.CorrelationId,
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
