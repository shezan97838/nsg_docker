using System.Security.Cryptography;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using NSG.Mobile.Application.Common;
using NSG.Mobile.Application.Interfaces.Infrastructure;
using NSG.Mobile.Application.Interfaces.Repositories;
using NSG.Mobile.Application.Interfaces.Services;
using NSG.Mobile.Application.Pis.Import;
using NSG.Mobile.Domain.Constants;
using NSG.Mobile.Domain.Entities;
using NSG.Mobile.Domain.Enums;

namespace NSG.Mobile.Application.Pis.Services;

/// <summary>Filters of the admin PIS queries. Applied in the database.</summary>
public sealed class PisAdminQuery
{
    public string? Search { get; set; }

    /// <summary>Exact NSG number.</summary>
    public string? NsgNumber { get; set; }
    public string? Unit { get; set; }
    public string? Rank { get; set; }
    public bool? IsActive { get; set; }
    public string? SortBy { get; set; }
    public string? SortOrder { get; set; }
}

/// <summary>
/// Complete personnel record for PIS Admins. The four sensitive identifiers are returned decrypted (owner decision for this admin API);
/// ciphertext and its column names are never exposed, and internal tracking metadata is left out.
/// </summary>
public sealed class PisAdminRecordDto
{
    public long Id { get; set; }
    public string NsgNumber { get; set; } = string.Empty;
    public string? ServiceNumber { get; set; }
    public string? NsgRank { get; set; }
    public string? ParentDepartmentRank { get; set; }
    public string? FullName { get; set; }
    public string? NsgUnit { get; set; }
    public string? Education { get; set; }
    public DateOnly? DateOfInductionInNsg { get; set; }
    public string? SideSteppedDetails { get; set; }
    public DateOnly? DateOfRepatriation { get; set; }
    public DateOnly? DateOfBirth { get; set; }
    public DateOnly? DateOfEnrollmentInGovtService { get; set; }
    public DateOnly? DateOfSuperannuation { get; set; }
    public string? Gender { get; set; }
    public string? Category { get; set; }
    public string? Religion { get; set; }
    public string? FatherName { get; set; }
    public string? MotherName { get; set; }
    public string? SpouseName { get; set; }
    public string? NextOfKinMobileNumber { get; set; }
    public string? NumberOfChildren { get; set; }
    public string MobileNumber { get; set; } = string.Empty;
    public string? BloodGroup { get; set; }
    public string? HeightCm { get; set; }
    public string? WeightKg { get; set; }
    public string? AadhaarNumber { get; set; }
    public string? PanNumber { get; set; }
    public string? PranNumber { get; set; }
    public string? SalaryAccountNumber { get; set; }
    public string? PermanentVillage { get; set; }
    public string? PermanentPostOffice { get; set; }
    public string? PermanentPoliceStation { get; set; }
    public string? PermanentDistrict { get; set; }
    public string? PermanentState { get; set; }
    public string? PermanentPinCode { get; set; }
    public DateOnly? DateOfAme { get; set; }
    public DateOnly? DateOfImmovablePropertyReturn { get; set; }
    public string? EarnedLeaveDue { get; set; }
    public string? HalfPayLeaveDue { get; set; }
    public string? PaternityLeaveAvailed { get; set; }
    public string? MaternityLeaveAvailed { get; set; }
    public string? ChildCareLeaveAvailed { get; set; }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public MobileAppActivationStatus MobileAppActivationStatus { get; set; }

    public bool IsActive { get; set; }
}

/// <summary>Read side of the admin PIS API (complete records, sensitive fields decrypted).</summary>
public interface IPisAdminQueryService
{
    Task<Result<PisPageDto<PisAdminRecordDto>>> GetPagedAsync(int pageNumber, int pageSize, PisAdminQuery query, CancellationToken cancellationToken);
    Task<Result<PisAdminRecordDto>> GetByIdAsync(long id, CancellationToken cancellationToken);
    Task<Result<PisAdminRecordDto>> GetByNsgNumberAsync(string nsgNumber, CancellationToken cancellationToken);

    /// <summary>Every matching record, unpaged. Refused above <see cref="PisAdminQueryService.MaxAllRecords"/> records.</summary>
    Task<Result<IReadOnlyList<PisAdminRecordDto>>> GetAllAsync(PisAdminQuery query, CancellationToken cancellationToken);
}

public class PisAdminQueryService : IPisAdminQueryService
{
    /// <summary>Safeguard of the unpaged endpoint.</summary>
    public const int MaxAllRecords = 25_000;
    public const int MaxPageSize = 100;

    /// <summary>Allowed sort keys (case-insensitive) mapped by the repository to columns.</summary>
    public static readonly IReadOnlyList<string> SortKeys = new[]
    {
        "nsgNumber", "fullName", "nsgRank", "nsgUnit", "serviceNumber", "mobileNumber", "dateOfBirth", "dateOfInductionInNsg", "id"
    };

    private const string NotFoundMessage = PisOwnershipMessages.NotFound;

    private readonly IPisAdminQueryRepository _repository;
    private readonly IEncryptionService _encryption;
    private readonly IAuditService _audit;
    private readonly ICurrentUserService _currentUser;
    private readonly ILogger<PisAdminQueryService> _logger;

    public PisAdminQueryService(IPisAdminQueryRepository repository, IEncryptionService encryption, IAuditService audit,
        ICurrentUserService currentUser, ILogger<PisAdminQueryService> logger)
    {
        _repository = repository;
        _encryption = encryption;
        _audit = audit;
        _currentUser = currentUser;
        _logger = logger;
    }

    public async Task<Result<PisPageDto<PisAdminRecordDto>>> GetPagedAsync(int pageNumber, int pageSize, PisAdminQuery query, CancellationToken cancellationToken)
    {
        if (!_currentUser.TryGetAdminActor(out AdminActor actor))
            return Result.Failure<PisPageDto<PisAdminRecordDto>>(ApiErrorCodes.Unauthorized, AdminActorExtensions.AdminNotIdentifiedMessage);
        if (pageNumber < 1)
            return Result.Failure<PisPageDto<PisAdminRecordDto>>(ApiErrorCodes.ValidationFailed, "pageNumber must be 1 or greater.");
        if (pageSize < 1 || pageSize > MaxPageSize)
            return Result.Failure<PisPageDto<PisAdminRecordDto>>(ApiErrorCodes.ValidationFailed, $"pageSize must be between 1 and {MaxPageSize}.");
        string? sortProblem = CheckSort(query);
        if (sortProblem != null)
            return Result.Failure<PisPageDto<PisAdminRecordDto>>(ApiErrorCodes.ValidationFailed, sortProblem);

        (IReadOnlyList<UserPis> items, int total) = await _repository.GetPagedAsync(pageNumber, pageSize, query, actor.Id, cancellationToken);
        List<PisAdminRecordDto> dtos = items.Select(ToDto).ToList();
        await AuditReadAsync("UserPisListRead", dtos.Count, actor, cancellationToken);

        return Result.Success(new PisPageDto<PisAdminRecordDto> { Items = dtos, Pagination = PisPaginationDto.Of(pageNumber, pageSize, total) });
    }

    public async Task<Result<PisAdminRecordDto>> GetByIdAsync(long id, CancellationToken cancellationToken)
    {
        if (!_currentUser.TryGetAdminActor(out AdminActor actor))
            return Result.Failure<PisAdminRecordDto>(ApiErrorCodes.Unauthorized, AdminActorExtensions.AdminNotIdentifiedMessage);

        UserPis? entity = await _repository.GetByIdAsync(id, cancellationToken);
        return await SingleAsync(entity, actor, cancellationToken);
    }

    public async Task<Result<PisAdminRecordDto>> GetByNsgNumberAsync(string nsgNumber, CancellationToken cancellationToken)
    {
        if (!_currentUser.TryGetAdminActor(out AdminActor actor))
            return Result.Failure<PisAdminRecordDto>(ApiErrorCodes.Unauthorized, AdminActorExtensions.AdminNotIdentifiedMessage);
        if (string.IsNullOrWhiteSpace(nsgNumber))
            return Result.Failure<PisAdminRecordDto>(ApiErrorCodes.ValidationFailed, "NSG number is required.");

        UserPis? entity = await _repository.GetByNsgNumberAsync(nsgNumber.Trim(), cancellationToken);
        return await SingleAsync(entity, actor, cancellationToken);
    }

    public async Task<Result<IReadOnlyList<PisAdminRecordDto>>> GetAllAsync(PisAdminQuery query, CancellationToken cancellationToken)
    {
        if (!_currentUser.TryGetAdminActor(out AdminActor actor))
            return Result.Failure<IReadOnlyList<PisAdminRecordDto>>(ApiErrorCodes.Unauthorized, AdminActorExtensions.AdminNotIdentifiedMessage);
        string? sortProblem = CheckSort(query);
        if (sortProblem != null)
            return Result.Failure<IReadOnlyList<PisAdminRecordDto>>(ApiErrorCodes.ValidationFailed, sortProblem);

        int count = await _repository.CountAsync(query, actor.Id, cancellationToken);
        if (count > MaxAllRecords)
            return Result.Failure<IReadOnlyList<PisAdminRecordDto>>(ApiErrorCodes.ValidationFailed,
                $"{count} records match, which is more than the {MaxAllRecords} this endpoint returns at once. Narrow the filters or use the paginated endpoint.");

        IReadOnlyList<UserPis> items = await _repository.GetAllAsync(query, actor.Id, cancellationToken);
        List<PisAdminRecordDto> dtos = items.Select(ToDto).ToList();
        await AuditReadAsync("UserPisFullListRead", dtos.Count, actor, cancellationToken);
        return Result.Success<IReadOnlyList<PisAdminRecordDto>>(dtos);
    }

    /// <summary>
    /// A record assigned to nobody is invisible to PIS Admins, exactly like a missing one (404); a record assigned to another admin is refused (403).
    /// Only the caller's own record is decrypted, returned and audited.
    /// </summary>
    private async Task<Result<PisAdminRecordDto>> SingleAsync(UserPis? entity, AdminActor actor, CancellationToken cancellationToken)
    {
        if (entity == null || entity.AssignedTo == null)
            return Result.Failure<PisAdminRecordDto>(ApiErrorCodes.NotFound, NotFoundMessage);
        if (entity.AssignedTo != actor.Id)
            return Result.Failure<PisAdminRecordDto>(ApiErrorCodes.Forbidden, PisOwnershipMessages.NotAssignedToYouToView);

        await AuditReadAsync("UserPisRead", 1, actor, cancellationToken);
        return Result.Success(ToDto(entity));
    }

    private static string? CheckSort(PisAdminQuery query)
    {
        if (!string.IsNullOrWhiteSpace(query.SortBy) && !SortKeys.Contains(query.SortBy.Trim(), StringComparer.OrdinalIgnoreCase))
            return "sortBy must be one of: " + string.Join(", ", SortKeys) + ".";
        if (!string.IsNullOrWhiteSpace(query.SortOrder) && !query.SortOrder.Trim().Equals("asc", StringComparison.OrdinalIgnoreCase)
            && !query.SortOrder.Trim().Equals("desc", StringComparison.OrdinalIgnoreCase))
            return "sortOrder must be asc or desc.";
        return null;
    }

    private PisAdminRecordDto ToDto(UserPis p) => new()
    {
        Id = p.Id,
        NsgNumber = p.NsgNumber,
        ServiceNumber = p.ServiceNumber,
        NsgRank = p.NsgRank,
        ParentDepartmentRank = p.ParentDepartmentRank,
        FullName = p.FullName,
        NsgUnit = p.NsgUnit,
        Education = p.Education,
        DateOfInductionInNsg = p.DateOfInductionInNsg,
        SideSteppedDetails = p.SideSteppedDetails,
        DateOfRepatriation = p.DateOfRepatriation,
        DateOfBirth = p.DateOfBirth,
        DateOfEnrollmentInGovtService = p.DateOfEnrollmentInGovtService,
        DateOfSuperannuation = p.DateOfSuperannuation,
        Gender = p.Gender,
        Category = p.Category,
        Religion = p.Religion,
        FatherName = p.FatherName,
        MotherName = p.MotherName,
        SpouseName = p.SpouseName,
        NextOfKinMobileNumber = p.NextOfKinMobileNumber,
        NumberOfChildren = p.NumberOfChildren,
        MobileNumber = p.MobileNumber,
        BloodGroup = p.BloodGroup,
        HeightCm = p.HeightCm,
        WeightKg = p.WeightKg,
        AadhaarNumber = Decrypt(p.AadhaarNumberEncrypted),
        PanNumber = Decrypt(p.PanNumberEncrypted),
        PranNumber = Decrypt(p.PranNumberEncrypted),
        SalaryAccountNumber = Decrypt(p.SalaryAccountNumberEncrypted),
        PermanentVillage = p.PermanentVillage,
        PermanentPostOffice = p.PermanentPostOffice,
        PermanentPoliceStation = p.PermanentPoliceStation,
        PermanentDistrict = p.PermanentDistrict,
        PermanentState = p.PermanentState,
        PermanentPinCode = p.PermanentPinCode,
        DateOfAme = p.DateOfAme,
        DateOfImmovablePropertyReturn = p.DateOfImmovablePropertyReturn,
        EarnedLeaveDue = p.EarnedLeaveDue,
        HalfPayLeaveDue = p.HalfPayLeaveDue,
        PaternityLeaveAvailed = p.PaternityLeaveAvailed,
        MaternityLeaveAvailed = p.MaternityLeaveAvailed,
        ChildCareLeaveAvailed = p.ChildCareLeaveAvailed,
        MobileAppActivationStatus = p.MobileAppActivationStatus,
        IsActive = p.IsActive
    };

    /// <summary>An unreadable value (for example after a key rotation) yields null and never fails the request; nothing is logged but the fact.</summary>
    private string? Decrypt(string? cipherText)
    {
        if (cipherText == null) return null;

        try
        {
            return _encryption.Decrypt(cipherText);
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException)
        {
            _logger.LogWarning("A stored sensitive personnel value could not be decrypted for display.");
            return null;
        }
    }

    private async Task AuditReadAsync(string operation, int count, AdminActor actor, CancellationToken cancellationToken)
    {
        // Reads of decrypted identifiers are audited once per request: operation and count only, attributed to the reading admin.
        await _audit.LogAsync(AuditAction.AdminAction, DomainConstants.AuditResults.Success,
            userId: actor.Key, entityType: "UserPis", correlationId: _currentUser.CorrelationId,
            additionalData: $"{{\"operation\":\"{operation}\",\"adminProfileId\":{actor.Id},\"records\":{count}}}",
            cancellationToken: cancellationToken);
    }
}
