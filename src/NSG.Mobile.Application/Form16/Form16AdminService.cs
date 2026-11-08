using Microsoft.Extensions.Logging;
using NSG.Mobile.Application.Common;
using NSG.Mobile.Application.Interfaces.Infrastructure;
using NSG.Mobile.Application.Interfaces.Repositories;
using NSG.Mobile.Application.Interfaces.Services;
using NSG.Mobile.Domain.Common;
using NSG.Mobile.Domain.Constants;
using NSG.Mobile.Domain.Entities;
using NSG.Mobile.Domain.Enums;

namespace NSG.Mobile.Application.Form16;

/// <summary>The Admin Portal's Form 16 operations. The acting admin always comes from the token.</summary>
public interface IForm16AdminService
{
    /// <summary>Creates the record for the NSG number and assessment year, or updates the supplied parts of the existing one. The NSG number must exist in PIS.</summary>
    Task<Result<Form16SaveOutcome>> UpsertAsync(Form16UploadRequest request, CancellationToken cancellationToken);

    /// <summary>Updates the supplied parts of an existing record. 404 when there is none; never creates a record.</summary>
    Task<Result<Form16SaveOutcome>> UpdateAsync(Form16UploadRequest request, CancellationToken cancellationToken);

    Task<Result<PaginatedResponse<Form16AdminDto>>> GetPagedAsync(string? nsgNumber, string? assessmentYear, int pageNumber, int pageSize, CancellationToken cancellationToken);

    Task<Result<Form16AdminDto>> GetByIdAsync(long id, CancellationToken cancellationToken);
}

public class Form16AdminService : IForm16AdminService
{
    public const int MaxPageSize = 100;
    public const string CreatedMessage = "Form 16 uploaded successfully.";
    public const string UpdatedMessage = "Form 16 updated successfully.";
    public const string NotFoundMessage = "Form 16 record not found.";
    public const string NoDocumentMessage = "Upload at least one PDF: partA, partB or both.";
    public const string PersonnelNotFoundMessage = "No personnel record exists for this NSG number.";
    public const string InvalidNsgNumberMessage = "nsgNumber is required (at most 30 characters, no control characters).";
    public const string InvalidAssessmentYearMessage = "assessmentYear is required in the form yyyy-yy, for example 2026-27.";
    public const string StorageFailureMessage = "The Form 16 file could not be stored. Please try again.";

    // One upload at a time per (NSG number, year) inside this process, so two requests cannot interleave their file moves.
    // Across several instances the unique index still prevents a duplicate record.
    private static readonly SemaphoreSlim[] Locks = Enumerable.Range(0, 64).Select(_ => new SemaphoreSlim(1, 1)).ToArray();

    private readonly IForm16Repository _repository;
    private readonly IUserPisRepository _pis;
    private readonly IForm16StorageService _storage;
    private readonly IAuditService _audit;
    private readonly ICurrentUserService _currentUser;
    private readonly Form16Settings _settings;
    private readonly ILogger<Form16AdminService> _logger;

    public Form16AdminService(IForm16Repository repository, IUserPisRepository pis, IForm16StorageService storage, IAuditService audit,
        ICurrentUserService currentUser, Form16Settings settings, ILogger<Form16AdminService> logger)
    {
        _repository = repository;
        _pis = pis;
        _storage = storage;
        _audit = audit;
        _currentUser = currentUser;
        _settings = settings;
        _logger = logger;
    }

    public Task<Result<Form16SaveOutcome>> UpsertAsync(Form16UploadRequest request, CancellationToken cancellationToken)
        => SaveAsync(request, createIfMissing: true, cancellationToken);

    public Task<Result<Form16SaveOutcome>> UpdateAsync(Form16UploadRequest request, CancellationToken cancellationToken)
        => SaveAsync(request, createIfMissing: false, cancellationToken);

    public async Task<Result<PaginatedResponse<Form16AdminDto>>> GetPagedAsync(string? nsgNumber, string? assessmentYear, int pageNumber, int pageSize, CancellationToken cancellationToken)
    {
        if (!_currentUser.TryGetAdminActor(out _))
            return Result.Failure<PaginatedResponse<Form16AdminDto>>(ApiErrorCodes.Unauthorized, "The administrator could not be identified.");
        if (pageNumber < 1 || pageSize < 1 || pageSize > MaxPageSize)
            return Result.Failure<PaginatedResponse<Form16AdminDto>>(ApiErrorCodes.ValidationFailed, $"pageNumber must be 1 or greater and pageSize between 1 and {MaxPageSize}.");

        string? year = null;
        if (!string.IsNullOrWhiteSpace(assessmentYear))
        {
            year = Form16Rules.NormalizeAssessmentYear(assessmentYear);
            if (year == null) return Result.Failure<PaginatedResponse<Form16AdminDto>>(ApiErrorCodes.ValidationFailed, InvalidAssessmentYearMessage);
        }

        string? nsg = string.IsNullOrWhiteSpace(nsgNumber) ? null : nsgNumber.Trim();
        (IReadOnlyList<Form16Row> items, int total) = await _repository.GetPagedAsync(nsg, year, pageNumber, pageSize, cancellationToken);
        return Result.Success(new PaginatedResponse<Form16AdminDto>(items.Select(ToDto).ToList(), total, pageNumber, pageSize));
    }

    public async Task<Result<Form16AdminDto>> GetByIdAsync(long id, CancellationToken cancellationToken)
    {
        if (!_currentUser.TryGetAdminActor(out _))
            return Result.Failure<Form16AdminDto>(ApiErrorCodes.Unauthorized, "The administrator could not be identified.");

        Form16Row? row = await _repository.GetRowByIdAsync(id, cancellationToken);
        return row == null
            ? Result.Failure<Form16AdminDto>(ApiErrorCodes.NotFound, NotFoundMessage)
            : Result.Success(ToDto(row));
    }

    private async Task<Result<Form16SaveOutcome>> SaveAsync(Form16UploadRequest request, bool createIfMissing, CancellationToken cancellationToken)
    {
        if (!_currentUser.TryGetAdminActor(out AdminActor actor))
            return Failure(ApiErrorCodes.Unauthorized, "The administrator could not be identified.");

        string nsgNumber = request.NsgNumber?.Trim() ?? string.Empty;
        if (!Form16Rules.IsValidNsgNumber(nsgNumber))
            return Failure(ApiErrorCodes.ValidationFailed, InvalidNsgNumberMessage);

        string? year = Form16Rules.NormalizeAssessmentYear(request.AssessmentYear);
        if (year == null)
            return Failure(ApiErrorCodes.ValidationFailed, InvalidAssessmentYearMessage);

        if ((request.PartA == null || request.PartA.Length == 0) && (request.PartB == null || request.PartB.Length == 0))
            return Failure(ApiErrorCodes.ValidationFailed, NoDocumentMessage);

        // The PIS record also supplies the canonical spelling of the NSG number, so the folder name never depends on how the admin typed it.
        if (createIfMissing)
        {
            UserPis? person = await _pis.GetByNsgNumberAsync(nsgNumber, cancellationToken);
            if (person == null)
                return Failure(ApiErrorCodes.NotFound, PersonnelNotFoundMessage);
            nsgNumber = person.NsgNumber;
        }

        SemaphoreSlim gate = Locks[(uint)StringComparer.OrdinalIgnoreCase.GetHashCode($"{nsgNumber}|{year}") % (uint)Locks.Length];
        await gate.WaitAsync(cancellationToken);
        try
        {
            return await SaveLockedAsync(request, createIfMissing, nsgNumber, year, actor, cancellationToken);
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<Result<Form16SaveOutcome>> SaveLockedAsync(Form16UploadRequest request, bool createIfMissing, string nsgNumber, string year, AdminActor actor, CancellationToken cancellationToken)
    {
        PersonnelForm16? record = await _repository.GetByKeyForUpdateAsync(nsgNumber, year, cancellationToken);
        if (record == null && !createIfMissing)
            return Failure(ApiErrorCodes.NotFound, NotFoundMessage);
        if (record != null) nsgNumber = record.NsgNumber;   // canonical spelling of an existing record (PUT)

        List<IForm16StagedFile> staged = new();
        try
        {
            IForm16StagedFile? partA = null, partB = null;

            if (request.PartA is { Length: > 0 })
            {
                Result<IForm16StagedFile> result = await _storage.StageAsync(year, nsgNumber, Form16Part.PartA, request.PartA.Content, _settings.MaxPdfSizeBytes, cancellationToken);
                if (!result.IsSuccess) return Failure(result.ErrorCode!, $"Part A: {result.ErrorMessage}");
                staged.Add(partA = result.Data!);
            }

            if (request.PartB is { Length: > 0 })
            {
                Result<IForm16StagedFile> result = await _storage.StageAsync(year, nsgNumber, Form16Part.PartB, request.PartB.Content, _settings.MaxPdfSizeBytes, cancellationToken);
                if (!result.IsSuccess) return Failure(result.ErrorCode!, $"Part B: {result.ErrorMessage}");
                staged.Add(partB = result.Data!);
            }

            try
            {
                foreach (IForm16StagedFile file in staged) file.Commit();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                _logger.LogError(ex, "Form 16 files for year {AssessmentYear} could not be moved into place", year);
                return Failure(ApiErrorCodes.InternalError, StorageFailureMessage);
            }

            // The files are in place; the row is saved next. Any failure from here on disposes the staged files, which restores the previous ones.
            DateTime now = IndianStandardTime.Now;
            bool created = record == null;
            record ??= new PersonnelForm16 { NsgNumber = nsgNumber, AssessmentYear = year, CreatedAtIst = now };
            if (partA != null)
            {
                record.PartAFilePath = partA.RelativePath;
                record.PartAUploadedAtIst = now;
            }

            if (partB != null)
            {
                record.PartBFilePath = partB.RelativePath;
                record.PartBUploadedAtIst = now;
            }

            record.UploadedBy = actor.Id;
            record.UpdatedAtIst = now;

            if (created)
            {
                if (await _repository.AddAsync(record, cancellationToken) == Form16InsertOutcome.Duplicate)
                    return Failure(ApiErrorCodes.Conflict, "Another upload for this NSG number and assessment year is in progress. Please try again.");
            }
            else
            {
                await _repository.UpdateAsync(record, cancellationToken);
            }

            foreach (IForm16StagedFile file in staged) file.Complete();

            await AuditAsync(record, created, partA != null, partB != null, actor);

            Form16Row? row = await _repository.GetRowByIdAsync(record.Id, CancellationToken.None);
            Form16AdminDto dto = row != null ? ToDto(row) : ToDto(new Form16Row(record, null));
            return Result.Success(new Form16SaveOutcome(created, created ? CreatedMessage : UpdatedMessage, dto));
        }
        finally
        {
            foreach (IForm16StagedFile file in staged) await file.DisposeAsync();
        }
    }

    private async Task AuditAsync(PersonnelForm16 record, bool created, bool partA, bool partB, AdminActor actor)
    {
        try
        {
            await _audit.LogAsync(AuditAction.AdminAction, DomainConstants.AuditResults.Success,
                userId: actor.Key, entityType: "PersonnelForm16", entityId: record.Id.ToString(), correlationId: _currentUser.CorrelationId,
                additionalData: $"{{\"operation\":\"{(created ? "Form16Create" : "Form16Update")}\",\"adminProfileId\":{actor.Id},\"assessmentYear\":\"{record.AssessmentYear}\",\"partA\":{(partA ? "true" : "false")},\"partB\":{(partB ? "true" : "false")}}}",
                cancellationToken: CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Audit entry for Form 16 record {Form16Id} could not be written", record.Id);
        }
    }

    private static Result<Form16SaveOutcome> Failure(string code, string message) => Result.Failure<Form16SaveOutcome>(code, message);

    private static Form16AdminDto ToDto(Form16Row row)
    {
        PersonnelForm16 f = row.Form16;
        return new Form16AdminDto
        {
            Id = f.Id,
            NsgNumber = f.NsgNumber,
            EmployeeName = row.EmployeeName,
            AssessmentYear = f.AssessmentYear,
            PartAAvailable = !string.IsNullOrEmpty(f.PartAFilePath),
            PartAUploadedAt = f.PartAUploadedAtIst == null ? null : IstTime.FromIst(f.PartAUploadedAtIst.Value),
            PartBAvailable = !string.IsNullOrEmpty(f.PartBFilePath),
            PartBUploadedAt = f.PartBUploadedAtIst == null ? null : IstTime.FromIst(f.PartBUploadedAtIst.Value),
            CreatedAt = IstTime.FromIst(f.CreatedAtIst),
            UpdatedAt = IstTime.FromIst(f.UpdatedAtIst)
        };
    }
}
