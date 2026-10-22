using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using NSG.Mobile.Application.Common;
using NSG.Mobile.Application.Grievances.DTOs;
using NSG.Mobile.Application.Grievances.Mappings;
using NSG.Mobile.Application.Interfaces.Repositories;
using NSG.Mobile.Application.Interfaces.Services;
using NSG.Mobile.Domain.Common;
using NSG.Mobile.Domain.Constants;
using NSG.Mobile.Domain.Entities;
using NSG.Mobile.Domain.Enums;

namespace NSG.Mobile.Application.Grievances.Services;

/// <summary>Grievance Admin work on grievances: review, document access and status changes. Every endpoint is behind the RequireGrievanceAdmin policy.</summary>
public interface IGrievanceAdminService
{
    Task<Result<PaginatedResponse<GrievanceAdminSummaryDto>>> GetPagedAsync(
        int pageNumber, int pageSize, GrievanceStatus? status, int? categoryId, DateOnly? fromDate, DateOnly? toDate,
        string? search, long? assignedTo, CancellationToken cancellationToken);

    Task<Result<GrievanceAdminDetailDto>> GetByIdAsync(long id, CancellationToken cancellationToken);

    Task<Result<GrievanceDocumentContent>> OpenDocumentAsync(long id, int index, CancellationToken cancellationToken);

    /// <summary>Appends an admin comment to the grievance history (status unchanged). Not allowed on a closed grievance.</summary>
    Task<Result<ApiResult<GrievanceAdminDetailDto>>> AddCommentAsync(long id, AddGrievanceCommentRequestDto request, CancellationToken cancellationToken);

    /// <summary>Forwards or reassigns the grievance to another active Grievance Admin and records it in the history. Not allowed on a closed grievance.</summary>
    Task<Result<ApiResult<GrievanceAdminDetailDto>>> ForwardAsync(long id, ForwardGrievanceRequestDto request, CancellationToken cancellationToken);

    /// <summary>Moves a grievance one step forward (1→2, 2→3, 3→4; 4 = resolved). The acting admin and the time come from the server.</summary>
    Task<Result<ApiResult<GrievanceAdminDetailDto>>> UpdateStatusAsync(long id, UpdateGrievanceStatusRequestDto request, CancellationToken cancellationToken);
}

public class GrievanceAdminService : IGrievanceAdminService
{
    private static readonly Regex TicketSearchPattern = new("^[A-Za-z0-9-]+$", RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private readonly IGrievanceRepository _repository;
    private readonly GrievanceDocumentAccess _documentAccess;
    private readonly IAuditService _audit;
    private readonly ICurrentUserService _currentUser;
    private readonly TimeProvider _time;
    private readonly ILogger<GrievanceAdminService> _logger;

    public GrievanceAdminService(IGrievanceRepository repository, GrievanceDocumentAccess documentAccess, IAuditService audit,
        ICurrentUserService currentUser, TimeProvider time, ILogger<GrievanceAdminService> logger)
    {
        _repository = repository;
        _documentAccess = documentAccess;
        _audit = audit;
        _currentUser = currentUser;
        _time = time;
        _logger = logger;
    }

    public async Task<Result<PaginatedResponse<GrievanceAdminSummaryDto>>> GetPagedAsync(
        int pageNumber, int pageSize, GrievanceStatus? status, int? categoryId, DateOnly? fromDate, DateOnly? toDate,
        string? search, long? assignedTo, CancellationToken cancellationToken)
    {
        if (fromDate.HasValue && toDate.HasValue && fromDate.Value > toDate.Value)
            return Result.Failure<PaginatedResponse<GrievanceAdminSummaryDto>>(ApiErrorCodes.ValidationFailed, "fromDate must not be after toDate.");

        string? term = string.IsNullOrWhiteSpace(search) ? null : search.Trim();
        if (term != null && (term.Length > GrievanceLimits.TicketSearchMaxLength || !TicketSearchPattern.IsMatch(term)))
            return Result.Failure<PaginatedResponse<GrievanceAdminSummaryDto>>(ApiErrorCodes.ValidationFailed,
                "Search accepts a ticket number such as GRV-2026-000001 (letters, digits and hyphens, at most 30 characters).");

        // Dates are IST calendar days: from the start of fromDate up to, not including, the day after toDate.
        GrievanceListFilter filter = new(
            Status: status,
            CategoryId: categoryId,
            FromIst: fromDate?.ToDateTime(TimeOnly.MinValue),
            ToIstExclusive: toDate?.AddDays(1).ToDateTime(TimeOnly.MinValue),
            TicketSearch: term,
            AssignedTo: assignedTo);

        (IReadOnlyList<Grievance> items, int total) = await _repository.GetPagedAsync(filter, pageNumber, pageSize, cancellationToken);
        IReadOnlyDictionary<long, GrievanceEmployeeInfo> employees =
            await _repository.GetEmployeeInfoAsync(items.Select(g => g.CreatedBy).Distinct().ToList(), cancellationToken);
        return Result.Success(new PaginatedResponse<GrievanceAdminSummaryDto>(
            items.Select(g => g.ToAdminSummaryDto(g.Category?.GrievanceName ?? string.Empty, employees.GetValueOrDefault(g.CreatedBy))).ToList(), total, pageNumber, pageSize));
    }

    public async Task<Result<GrievanceAdminDetailDto>> GetByIdAsync(long id, CancellationToken cancellationToken)
    {
        long? adminId = _currentUser.AdminProfileId;
        if (adminId == null)
            return Result.Failure<GrievanceAdminDetailDto>(ApiErrorCodes.Unauthorized, GrievanceMessages.IdentityMissing);

        Result<GrievanceAdminDetailDto> detail = await LoadDetailAsync(id, cancellationToken);
        if (detail.IsSuccess)
        {
            // An admin opening a grievance reads an employee's personal details, so every view is audited (ids only).
            await _audit.LogAsync(AuditAction.GrievanceViewed, DomainConstants.AuditResults.Success,
                entityType: nameof(Grievance), entityId: id.ToString(), correlationId: _currentUser.CorrelationId,
                additionalData: JsonSerializer.Serialize(new { actorType = nameof(GrievanceActorType.Admin), actorId = adminId.Value }),
                cancellationToken: cancellationToken);
        }

        return detail;
    }

    /// <summary>Loads the full admin view without auditing; used by <see cref="GetByIdAsync"/> and to answer a status change.</summary>
    private async Task<Result<GrievanceAdminDetailDto>> LoadDetailAsync(long id, CancellationToken cancellationToken)
    {
        Grievance? grievance = await _repository.GetByIdAsync(id, null, cancellationToken);
        if (grievance == null)
            return Result.Failure<GrievanceAdminDetailDto>(ApiErrorCodes.NotFound, GrievanceMessages.GrievanceNotFound);

        IReadOnlyList<GrievanceStatusLog> timeline = await _repository.GetTimelineAsync(grievance.Id, cancellationToken);
        IReadOnlyDictionary<long, GrievanceEmployeeInfo> employees = await _repository.GetEmployeeInfoAsync(new[] { grievance.CreatedBy }, cancellationToken);
        return Result.Success(grievance.ToAdminDetailDto(
            grievance.Category?.GrievanceName ?? string.Empty, timeline, employees.GetValueOrDefault(grievance.CreatedBy)));
    }

    public async Task<Result<GrievanceDocumentContent>> OpenDocumentAsync(long id, int index, CancellationToken cancellationToken)
    {
        long? adminId = _currentUser.AdminProfileId;
        if (adminId == null)
            return Result.Failure<GrievanceDocumentContent>(ApiErrorCodes.Unauthorized, GrievanceMessages.IdentityMissing);

        Grievance? grievance = await _repository.GetByIdAsync(id, null, cancellationToken);
        if (grievance == null)
            return Result.Failure<GrievanceDocumentContent>(ApiErrorCodes.NotFound, GrievanceMessages.GrievanceNotFound);

        return await _documentAccess.OpenAsync(grievance, index, GrievanceActorType.Admin, adminId.Value, cancellationToken);
    }

    public async Task<Result<ApiResult<GrievanceAdminDetailDto>>> UpdateStatusAsync(long id, UpdateGrievanceStatusRequestDto request, CancellationToken cancellationToken)
    {
        long? adminId = _currentUser.AdminProfileId;
        if (adminId == null)
            return Result.Failure<ApiResult<GrievanceAdminDetailDto>>(ApiErrorCodes.Unauthorized, GrievanceMessages.IdentityMissing);

        if (request.ToStatus is not int rawStatus || !Enum.IsDefined(typeof(GrievanceStatus), rawStatus))
            return Result.Failure<ApiResult<GrievanceAdminDetailDto>>(ApiErrorCodes.ValidationFailed, "toStatus must be 2 (Under Review), 3 (Action Taken) or 4 (Closed).");
        GrievanceStatus to = (GrievanceStatus)rawStatus;

        string? remarks = string.IsNullOrWhiteSpace(request.Remarks) ? null : request.Remarks.Trim();
        if (remarks is { Length: > GrievanceLimits.RemarksMaxLength })
            return Result.Failure<ApiResult<GrievanceAdminDetailDto>>(ApiErrorCodes.ValidationFailed, $"Remarks must not exceed {GrievanceLimits.RemarksMaxLength} characters.");
        if (to == GrievanceStatus.ActionTaken && remarks == null)
            return Result.Failure<ApiResult<GrievanceAdminDetailDto>>(ApiErrorCodes.ValidationFailed, "Remarks are required when action is taken.");
        if (to == GrievanceStatus.Closed && remarks == null)
            return Result.Failure<ApiResult<GrievanceAdminDetailDto>>(ApiErrorCodes.ValidationFailed, "Remarks are required when resolving a grievance.");

        Grievance? grievance = await _repository.GetByIdAsync(id, null, cancellationToken);
        if (grievance == null)
            return Result.Failure<ApiResult<GrievanceAdminDetailDto>>(ApiErrorCodes.NotFound, GrievanceMessages.GrievanceNotFound);

        GrievanceStatus from = grievance.CurrentStatus;
        if (!IsAllowedTransition(from, to))
            return Result.Failure<ApiResult<GrievanceAdminDetailDto>>(ApiErrorCodes.ValidationFailed,
                $"A grievance cannot move from {from.DisplayName()} to {to.DisplayName()}. Allowed: Submitted → Under Review → Action Taken → Closed, one step at a time.");

        DateTime now = IndianStandardTime.ConvertFromUtc(_time.GetUtcNow().UtcDateTime);
        GrievanceStatusChangeResult change = await _repository.ChangeStatusAsync(id, from, to, adminId.Value, remarks, now, cancellationToken);

        switch (change.Outcome)
        {
            case GrievanceStatusChangeOutcome.NotFound:
                return Result.Failure<ApiResult<GrievanceAdminDetailDto>>(ApiErrorCodes.NotFound, GrievanceMessages.GrievanceNotFound);

            case GrievanceStatusChangeOutcome.StatusChanged:
                return Result.Failure<ApiResult<GrievanceAdminDetailDto>>(ApiErrorCodes.Conflict,
                    $"The grievance was changed by someone else and is now {change.CurrentStatus?.DisplayName()}. Reload it and try again.");
        }

        _logger.LogInformation("Grievance {GrievanceId} moved {From} -> {To} by admin {AdminId}", id, from, to, adminId.Value);
        await _audit.LogAsync(AuditAction.GrievanceStatusChanged, DomainConstants.AuditResults.Success,
            entityType: nameof(Grievance), entityId: id.ToString(), correlationId: _currentUser.CorrelationId,
            additionalData: JsonSerializer.Serialize(new { actorType = nameof(GrievanceActorType.Admin), actorId = adminId.Value, from = (int)from, to = (int)to }),
            cancellationToken: cancellationToken);

        Result<GrievanceAdminDetailDto> updated = await LoadDetailAsync(id, cancellationToken);
        return updated.IsSuccess
            ? Result.Success(new ApiResult<GrievanceAdminDetailDto>($"Grievance status updated to {to.DisplayName()} successfully.", updated.Data))
            : Result.Failure<ApiResult<GrievanceAdminDetailDto>>(updated.ErrorCode!, updated.ErrorMessage!);
    }

    public async Task<Result<ApiResult<GrievanceAdminDetailDto>>> AddCommentAsync(long id, AddGrievanceCommentRequestDto request, CancellationToken cancellationToken)
    {
        long? adminId = _currentUser.AdminProfileId;
        if (adminId == null)
            return Result.Failure<ApiResult<GrievanceAdminDetailDto>>(ApiErrorCodes.Unauthorized, GrievanceMessages.IdentityMissing);

        string comment = (request.Comment ?? string.Empty).Trim();
        if (comment.Length == 0)
            return Result.Failure<ApiResult<GrievanceAdminDetailDto>>(ApiErrorCodes.ValidationFailed, "Comment is required.");
        if (comment.Length > GrievanceLimits.RemarksMaxLength)
            return Result.Failure<ApiResult<GrievanceAdminDetailDto>>(ApiErrorCodes.ValidationFailed, $"Comment must not exceed {GrievanceLimits.RemarksMaxLength} characters.");

        switch (await _repository.AddCommentAsync(id, adminId.Value, comment, NowIst(), cancellationToken))
        {
            case GrievanceCommentOutcome.NotFound:
                return Result.Failure<ApiResult<GrievanceAdminDetailDto>>(ApiErrorCodes.NotFound, GrievanceMessages.GrievanceNotFound);
            case GrievanceCommentOutcome.Closed:
                return Result.Failure<ApiResult<GrievanceAdminDetailDto>>(ApiErrorCodes.Conflict, GrievanceMessages.GrievanceClosed);
        }

        await _audit.LogAsync(AuditAction.GrievanceCommentAdded, DomainConstants.AuditResults.Success,
            entityType: nameof(Grievance), entityId: id.ToString(), correlationId: _currentUser.CorrelationId,
            additionalData: JsonSerializer.Serialize(new { actorType = nameof(GrievanceActorType.Admin), actorId = adminId.Value }),
            cancellationToken: cancellationToken);

        return await AnswerAsync("Comment added successfully.", id, cancellationToken);
    }

    public async Task<Result<ApiResult<GrievanceAdminDetailDto>>> ForwardAsync(long id, ForwardGrievanceRequestDto request, CancellationToken cancellationToken)
    {
        long? adminId = _currentUser.AdminProfileId;
        if (adminId == null)
            return Result.Failure<ApiResult<GrievanceAdminDetailDto>>(ApiErrorCodes.Unauthorized, GrievanceMessages.IdentityMissing);

        if (request.ToAdminId is not > 0)
            return Result.Failure<ApiResult<GrievanceAdminDetailDto>>(ApiErrorCodes.ValidationFailed, "toAdminId must be the id of a Grievance Admin account.");
        string? remarks = string.IsNullOrWhiteSpace(request.Remarks) ? null : request.Remarks.Trim();
        if (remarks is { Length: > GrievanceLimits.RemarksMaxLength })
            return Result.Failure<ApiResult<GrievanceAdminDetailDto>>(ApiErrorCodes.ValidationFailed, $"Remarks must not exceed {GrievanceLimits.RemarksMaxLength} characters.");

        long target = request.ToAdminId.Value;
        if (!await _repository.IsActiveGrievanceAdminAsync(target, cancellationToken))
            return Result.Failure<ApiResult<GrievanceAdminDetailDto>>(ApiErrorCodes.ValidationFailed, "toAdminId must be the id of an active Grievance Admin account.");

        switch (await _repository.ForwardAsync(id, adminId.Value, target, remarks, NowIst(), cancellationToken))
        {
            case GrievanceForwardOutcome.NotFound:
                return Result.Failure<ApiResult<GrievanceAdminDetailDto>>(ApiErrorCodes.NotFound, GrievanceMessages.GrievanceNotFound);
            case GrievanceForwardOutcome.Closed:
                return Result.Failure<ApiResult<GrievanceAdminDetailDto>>(ApiErrorCodes.Conflict, GrievanceMessages.GrievanceClosed);
            case GrievanceForwardOutcome.AlreadyAssigned:
                return Result.Failure<ApiResult<GrievanceAdminDetailDto>>(ApiErrorCodes.Conflict, "The grievance is already assigned to this Grievance Admin.");
        }

        await _audit.LogAsync(AuditAction.GrievanceForwarded, DomainConstants.AuditResults.Success,
            entityType: nameof(Grievance), entityId: id.ToString(), correlationId: _currentUser.CorrelationId,
            additionalData: JsonSerializer.Serialize(new { actorType = nameof(GrievanceActorType.Admin), actorId = adminId.Value, forwardedTo = target }),
            cancellationToken: cancellationToken);

        return await AnswerAsync("Grievance forwarded successfully.", id, cancellationToken);
    }

    /// <summary>The refreshed detail with a message, as the answer to a change.</summary>
    private async Task<Result<ApiResult<GrievanceAdminDetailDto>>> AnswerAsync(string message, long id, CancellationToken cancellationToken)
    {
        Result<GrievanceAdminDetailDto> updated = await LoadDetailAsync(id, cancellationToken);
        return updated.IsSuccess
            ? Result.Success(new ApiResult<GrievanceAdminDetailDto>(message, updated.Data))
            : Result.Failure<ApiResult<GrievanceAdminDetailDto>>(updated.ErrorCode!, updated.ErrorMessage!);
    }

    private DateTime NowIst() => IndianStandardTime.ConvertFromUtc(_time.GetUtcNow().UtcDateTime);

    /// <summary>The only moves: 1→2, 2→3, 3→4.</summary>
    internal static bool IsAllowedTransition(GrievanceStatus from, GrievanceStatus to)
        => (from, to) is (GrievanceStatus.Submitted, GrievanceStatus.UnderReview)
            or (GrievanceStatus.UnderReview, GrievanceStatus.ActionTaken)
            or (GrievanceStatus.ActionTaken, GrievanceStatus.Closed);
}
