using System.Text.Json;
using NSG.Mobile.Application.Common;
using NSG.Mobile.Application.Interfaces.Repositories;
using NSG.Mobile.Application.Interfaces.Services;
using NSG.Mobile.Application.Leave.DTOs;
using NSG.Mobile.Domain.Common;
using NSG.Mobile.Domain.Constants;
using NSG.Mobile.Domain.Entities;
using NSG.Mobile.Domain.Enums;

namespace NSG.Mobile.Application.Leave.Services;

/// <summary>Outcome of a manual save: <see cref="Created"/> chooses between HTTP 201 and 200.</summary>
public sealed record LeaveSaveResponse(bool Created, ApiResult<LeaveRecordAdminDto> Body);

/// <summary>
/// Manual leave maintenance (Leave Admin only, enforced by the policy on the controller) and the caller's own read-only record (any mobile user).
/// The year is always the server's current IST calendar year; the acting admin and the person whose record is read come from the token.
/// </summary>
public interface ILeaveService
{
    /// <summary>
    /// Sets the TOTAL leave of a person for the current year. <paramref name="createIfMissing"/> true: creates the record when absent (POST);
    /// false: the record must exist (PUT). Writing the stored value again changes nothing.
    /// </summary>
    Task<Result<LeaveSaveResponse>> SaveAsync(string? nsgNumber, int? leaveTaken, bool createIfMissing, CancellationToken cancellationToken);

    /// <summary>The current-year record of a person (administrative view).</summary>
    Task<Result<LeaveRecordAdminDto>> GetCurrentYearAsync(string? nsgNumber, CancellationToken cancellationToken);

    /// <summary>Every change of a person's leave counts, newest first.</summary>
    Task<Result<PaginatedResponse<LeaveHistoryDto>>> GetHistoryAsync(string? nsgNumber, int pageNumber, int pageSize, CancellationToken cancellationToken);

    /// <summary>The signed-in mobile user's own current-year record. Someone else's data cannot be requested: no identifier is accepted.</summary>
    Task<Result<MyLeaveRecordDto>> GetMyCurrentYearAsync(CancellationToken cancellationToken);
}

public class LeaveService : ILeaveService
{
    private readonly ILeaveRepository _repository;
    private readonly IAuditService _audit;
    private readonly ICurrentUserService _currentUser;
    private readonly TimeProvider _time;

    public LeaveService(ILeaveRepository repository, IAuditService audit, ICurrentUserService currentUser, TimeProvider time)
    {
        _repository = repository;
        _audit = audit;
        _currentUser = currentUser;
        _time = time;
    }

    private DateTime NowIst() => IndianStandardTime.ConvertFromUtc(_time.GetUtcNow().UtcDateTime);

    public async Task<Result<LeaveSaveResponse>> SaveAsync(string? nsgNumber, int? leaveTaken, bool createIfMissing, CancellationToken cancellationToken)
    {
        long? adminId = _currentUser.AdminProfileId;
        if (adminId == null)
            return Result.Failure<LeaveSaveResponse>(ApiErrorCodes.Unauthorized, LeaveMessages.IdentityMissing);

        List<string> problems = new();
        string? nsgProblem = LeaveRules.ValidateNsgNumber(nsgNumber, "NSG number", out string nsg);
        if (nsgProblem != null) problems.Add(nsgProblem);
        string? leaveProblem = LeaveRules.ValidateLeaveNumber(leaveTaken, "Leave taken");
        if (leaveProblem != null) problems.Add(leaveProblem);
        if (problems.Count > 0)
            return Result.Failure<LeaveSaveResponse>(ApiErrorCodes.ValidationFailed, string.Join(" ", problems));

        IReadOnlyDictionary<string, string> personnel = await _repository.GetActivePersonnelNsgNumbersAsync(new[] { nsg }, cancellationToken);
        if (!personnel.TryGetValue(nsg.ToUpperInvariant(), out string? canonical))
            return Result.Failure<LeaveSaveResponse>(ApiErrorCodes.NotFound, LeaveMessages.PersonnelNotFound);

        DateTime now = NowIst();
        int year = LeaveRules.CurrentYear(now);
        LeaveSaveResult saved = await _repository.SaveManualAsync(canonical, year, leaveTaken!.Value, createIfMissing, adminId.Value, now, cancellationToken);

        switch (saved.Outcome)
        {
            case LeaveSaveOutcome.NotFound:
                return Result.Failure<LeaveSaveResponse>(ApiErrorCodes.NotFound, LeaveMessages.RecordNotFound + " Use POST to create it.");
            case LeaveSaveOutcome.ConcurrencyConflict:
                return Result.Failure<LeaveSaveResponse>(ApiErrorCodes.ConcurrencyConflict, LeaveMessages.RecordChangedConcurrently);
        }

        PersonnelLeaveRecord record = saved.Record!;
        if (saved.Outcome != LeaveSaveOutcome.Unchanged)
        {
            await _audit.LogAsync(AuditAction.LeaveRecordChanged, DomainConstants.AuditResults.Success,
                entityType: nameof(PersonnelLeaveRecord), entityId: record.Id.ToString(), correlationId: _currentUser.CorrelationId,
                additionalData: JsonSerializer.Serialize(new
                {
                    actorId = adminId.Value, source = "manual", year, oldLeaveTaken = saved.OldLeaveTaken, newLeaveTaken = record.LeaveTaken
                }),
                cancellationToken: cancellationToken);
        }

        string message = saved.Outcome switch
        {
            LeaveSaveOutcome.Created => "Leave record created successfully.",
            LeaveSaveOutcome.Updated => "Leave record updated successfully.",
            _ => "Leave record already has this value; nothing was changed."
        };
        return Result.Success(new LeaveSaveResponse(saved.Outcome == LeaveSaveOutcome.Created, new ApiResult<LeaveRecordAdminDto>(message, record.ToAdminDto())));
    }

    public async Task<Result<LeaveRecordAdminDto>> GetCurrentYearAsync(string? nsgNumber, CancellationToken cancellationToken)
    {
        long? adminId = _currentUser.AdminProfileId;
        if (adminId == null)
            return Result.Failure<LeaveRecordAdminDto>(ApiErrorCodes.Unauthorized, LeaveMessages.IdentityMissing);

        string? problem = LeaveRules.ValidateNsgNumber(nsgNumber, "NSG number", out string nsg);
        if (problem != null) return Result.Failure<LeaveRecordAdminDto>(ApiErrorCodes.ValidationFailed, problem);

        IReadOnlyDictionary<string, string> personnel = await _repository.GetActivePersonnelNsgNumbersAsync(new[] { nsg }, cancellationToken);
        if (!personnel.TryGetValue(nsg.ToUpperInvariant(), out string? canonical))
            return Result.Failure<LeaveRecordAdminDto>(ApiErrorCodes.NotFound, LeaveMessages.PersonnelNotFound);

        PersonnelLeaveRecord? record = await _repository.GetRecordAsync(canonical, LeaveRules.CurrentYear(NowIst()), cancellationToken);
        if (record == null)
            return Result.Failure<LeaveRecordAdminDto>(ApiErrorCodes.NotFound, LeaveMessages.RecordNotFound);

        return Result.Success(record.ToAdminDto());
    }

    public async Task<Result<PaginatedResponse<LeaveHistoryDto>>> GetHistoryAsync(string? nsgNumber, int pageNumber, int pageSize, CancellationToken cancellationToken)
    {
        string? problem = LeaveRules.ValidateNsgNumber(nsgNumber, "NSG number", out string nsg);
        if (problem != null) return Result.Failure<PaginatedResponse<LeaveHistoryDto>>(ApiErrorCodes.ValidationFailed, problem);

        IReadOnlyDictionary<string, string> personnel = await _repository.GetActivePersonnelNsgNumbersAsync(new[] { nsg }, cancellationToken);
        if (!personnel.TryGetValue(nsg.ToUpperInvariant(), out string? canonical))
            return Result.Failure<PaginatedResponse<LeaveHistoryDto>>(ApiErrorCodes.NotFound, LeaveMessages.PersonnelNotFound);

        (IReadOnlyList<PersonnelLeaveHistory> items, int total) = await _repository.GetHistoryPagedAsync(canonical, pageNumber, pageSize, cancellationToken);
        return Result.Success(new PaginatedResponse<LeaveHistoryDto>(items.Select(h => h.ToDto()).ToList(), total, pageNumber, pageSize));
    }

    public async Task<Result<MyLeaveRecordDto>> GetMyCurrentYearAsync(CancellationToken cancellationToken)
    {
        long? profileId = _currentUser.ProfileId;
        if (profileId == null)
            return Result.Failure<MyLeaveRecordDto>(ApiErrorCodes.Unauthorized, LeaveMessages.IdentityMissing);

        // The person is whoever the token's profile is; the NSG number is read from that profile, never from the request.
        string? nsg = await _repository.GetNsgNumberOfMobileProfileAsync(profileId.Value, cancellationToken);
        if (nsg == null)
            return Result.Failure<MyLeaveRecordDto>(ApiErrorCodes.Unauthorized, LeaveMessages.IdentityMissing);

        int year = LeaveRules.CurrentYear(NowIst());
        PersonnelLeaveRecord? record = await _repository.GetRecordAsync(nsg, year, cancellationToken);
        if (record == null)
            return Result.Failure<MyLeaveRecordDto>(ApiErrorCodes.NotFound, string.Format(LeaveMessages.NoLeaveRecordForYear, year));

        return Result.Success(record.ToMyDto());
    }
}
