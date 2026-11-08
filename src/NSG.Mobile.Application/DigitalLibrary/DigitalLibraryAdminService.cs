using Microsoft.Extensions.Logging;
using NSG.Mobile.Application.Common;
using NSG.Mobile.Application.Interfaces.Infrastructure;
using NSG.Mobile.Application.Interfaces.Repositories;
using NSG.Mobile.Application.Interfaces.Services;
using NSG.Mobile.Domain.Common;
using NSG.Mobile.Domain.Constants;
using NSG.Mobile.Domain.Entities;
using NSG.Mobile.Domain.Enums;

namespace NSG.Mobile.Application.DigitalLibrary;

/// <summary>The Admin Portal's Digital Library operations. The acting admin always comes from the token.</summary>
public interface IDigitalLibraryAdminService
{
    Task<Result<PaginatedResponse<DigitalLibraryAdminDto>>> GetPagedAsync(DigitalLibraryAdminFilter filter, int pageNumber, int pageSize, CancellationToken cancellationToken);

    Task<Result<DigitalLibraryAdminDto>> GetByIdAsync(long id, CancellationToken cancellationToken);

    /// <summary>Validates and stores the file, then inserts an active record.</summary>
    Task<Result<DigitalLibraryAdminDto>> CreateAsync(DigitalLibraryCreateRequest request, CancellationToken cancellationToken);

    /// <summary>Updates description and importance, and replaces the file when one is supplied. Never changes <c>is_active</c>.</summary>
    Task<Result<DigitalLibraryAdminDto>> UpdateAsync(long id, DigitalLibraryUpdateRequest request, CancellationToken cancellationToken);

    /// <summary>Soft delete: sets <c>is_active</c> to false. The row and the file are kept. Repeating it answers "already inactive" (a success).</summary>
    Task<Result<ApiResult<DigitalLibraryAdminDto>>> DeactivateAsync(long id, CancellationToken cancellationToken);
}

public class DigitalLibraryAdminService : IDigitalLibraryAdminService
{
    private const string UnauthorizedMessage = "The administrator could not be identified.";

    private readonly IDigitalLibraryRepository _repository;
    private readonly IDigitalLibraryStorageService _storage;
    private readonly IAuditService _audit;
    private readonly ICurrentUserService _currentUser;
    private readonly DigitalLibrarySettings _settings;
    private readonly ILogger<DigitalLibraryAdminService> _logger;

    public DigitalLibraryAdminService(IDigitalLibraryRepository repository, IDigitalLibraryStorageService storage, IAuditService audit,
        ICurrentUserService currentUser, DigitalLibrarySettings settings, ILogger<DigitalLibraryAdminService> logger)
    {
        _repository = repository;
        _storage = storage;
        _audit = audit;
        _currentUser = currentUser;
        _settings = settings;
        _logger = logger;
    }

    public async Task<Result<PaginatedResponse<DigitalLibraryAdminDto>>> GetPagedAsync(DigitalLibraryAdminFilter filter, int pageNumber, int pageSize, CancellationToken cancellationToken)
    {
        if (!_currentUser.TryGetAdminActor(out _))
            return Result.Failure<PaginatedResponse<DigitalLibraryAdminDto>>(ApiErrorCodes.Unauthorized, UnauthorizedMessage);
        if (pageNumber < 1 || pageSize < 1 || pageSize > DigitalLibraryRules.MaxPageSize)
            return Result.Failure<PaginatedResponse<DigitalLibraryAdminDto>>(ApiErrorCodes.ValidationFailed,
                $"pageNumber must be 1 or greater and pageSize between 1 and {DigitalLibraryRules.MaxPageSize}.");

        string? typeLabel = null;
        if (!string.IsNullOrWhiteSpace(filter.FileType))
        {
            DigitalLibraryFileType? type = DigitalLibraryRules.ParseLabel(filter.FileType);
            if (type == null)
                return Result.Failure<PaginatedResponse<DigitalLibraryAdminDto>>(ApiErrorCodes.ValidationFailed, "fileType must be PDF, JPG, JPEG or PNG.");
            typeLabel = DigitalLibraryRules.Label(type.Value);
        }

        string? search = string.IsNullOrWhiteSpace(filter.Search) ? null : filter.Search.Trim();
        (IReadOnlyList<DigitalLibraryItem> items, int total) = await _repository.GetAdminPagedAsync(filter with { Search = search }, typeLabel, pageNumber, pageSize, cancellationToken);
        return Result.Success(new PaginatedResponse<DigitalLibraryAdminDto>(items.Select(i => i.ToAdminDto()).ToList(), total, pageNumber, pageSize));
    }

    public async Task<Result<DigitalLibraryAdminDto>> GetByIdAsync(long id, CancellationToken cancellationToken)
    {
        if (!_currentUser.TryGetAdminActor(out _))
            return Result.Failure<DigitalLibraryAdminDto>(ApiErrorCodes.Unauthorized, UnauthorizedMessage);
        if (id < 1)
            return Result.Failure<DigitalLibraryAdminDto>(ApiErrorCodes.ValidationFailed, "id must be 1 or greater.");

        DigitalLibraryItem? item = await _repository.GetByIdAsync(id, cancellationToken);
        return item == null
            ? Result.Failure<DigitalLibraryAdminDto>(ApiErrorCodes.NotFound, DigitalLibraryRules.NotFoundMessage)
            : Result.Success(item.ToAdminDto());
    }

    public async Task<Result<DigitalLibraryAdminDto>> CreateAsync(DigitalLibraryCreateRequest request, CancellationToken cancellationToken)
    {
        if (!_currentUser.TryGetAdminActor(out AdminActor actor)) return Result.Failure<DigitalLibraryAdminDto>(ApiErrorCodes.Unauthorized, UnauthorizedMessage);

        string? invalid = ValidateDescription(request.Description);
        if (invalid != null) return Result.Failure<DigitalLibraryAdminDto>(ApiErrorCodes.ValidationFailed, invalid);
        if (request.File == null || request.File.Length == 0)
            return Result.Failure<DigitalLibraryAdminDto>(ApiErrorCodes.ValidationFailed, "file is required.");

        DigitalLibraryFileType? type = DigitalLibraryRules.TypeFromFileName(request.File.FileName);
        if (type == null) return Result.Failure<DigitalLibraryAdminDto>(ApiErrorCodes.ValidationFailed, DigitalLibraryRules.UnsupportedTypeMessage);

        Result<IDigitalLibraryStagedFile> staged = await StageSafelyAsync(type.Value, request.File, cancellationToken);
        if (!staged.IsSuccess) return Result.Failure<DigitalLibraryAdminDto>(staged.ErrorCode!, staged.ErrorMessage!);

        await using IDigitalLibraryStagedFile file = staged.Data!;
        if (!TryCommit(file)) return Result.Failure<DigitalLibraryAdminDto>(ApiErrorCodes.InternalError, DigitalLibraryRules.StorageFailureMessage);

        // The file is in place; if the insert fails, disposing the staged file (not completed) removes it again.
        DigitalLibraryItem item = new()
        {
            Description = request.Description!.Trim(),
            FilePath = file.RelativePath,
            FileType = DigitalLibraryRules.Label(type.Value),
            IsImportant = request.IsImportant ?? false,
            IsActive = true,
            CreatedAtIst = IndianStandardTime.Now,
            CreatedBy = actor.Id
        };
        await _repository.AddAsync(item, cancellationToken);
        file.Complete();

        await AuditAsync("DigitalLibraryCreate", item.Id, actor);
        return Result.Success(item.ToAdminDto());
    }

    public async Task<Result<DigitalLibraryAdminDto>> UpdateAsync(long id, DigitalLibraryUpdateRequest request, CancellationToken cancellationToken)
    {
        if (!_currentUser.TryGetAdminActor(out AdminActor actor)) return Result.Failure<DigitalLibraryAdminDto>(ApiErrorCodes.Unauthorized, UnauthorizedMessage);
        if (id < 1) return Result.Failure<DigitalLibraryAdminDto>(ApiErrorCodes.ValidationFailed, "id must be 1 or greater.");

        string? invalid = ValidateDescription(request.Description);
        if (invalid != null) return Result.Failure<DigitalLibraryAdminDto>(ApiErrorCodes.ValidationFailed, invalid);
        if (request.IsImportant == null) return Result.Failure<DigitalLibraryAdminDto>(ApiErrorCodes.ValidationFailed, "isImportant is required.");

        DigitalLibraryItem? item = await _repository.GetForUpdateAsync(id, cancellationToken);
        if (item == null) return Result.Failure<DigitalLibraryAdminDto>(ApiErrorCodes.NotFound, DigitalLibraryRules.NotFoundMessage);

        DigitalLibraryFileType? type = null;
        if (request.File is { Length: > 0 })
        {
            type = DigitalLibraryRules.TypeFromFileName(request.File.FileName);
            if (type == null) return Result.Failure<DigitalLibraryAdminDto>(ApiErrorCodes.ValidationFailed, DigitalLibraryRules.UnsupportedTypeMessage);
        }
        else if (request.File != null)
        {
            return Result.Failure<DigitalLibraryAdminDto>(ApiErrorCodes.ValidationFailed, "The file is empty.");
        }

        IDigitalLibraryStagedFile? file = null;
        try
        {
            string? previousPath = null;
            if (type != null)
            {
                Result<IDigitalLibraryStagedFile> staged = await StageSafelyAsync(type.Value, request.File!, cancellationToken);
                if (!staged.IsSuccess) return Result.Failure<DigitalLibraryAdminDto>(staged.ErrorCode!, staged.ErrorMessage!);
                file = staged.Data!;
                if (!TryCommit(file)) return Result.Failure<DigitalLibraryAdminDto>(ApiErrorCodes.InternalError, DigitalLibraryRules.StorageFailureMessage);

                previousPath = item.FilePath;
                item.FilePath = file.RelativePath;
                item.FileType = DigitalLibraryRules.Label(type.Value);
            }

            item.Description = request.Description!.Trim();
            item.IsImportant = request.IsImportant.Value;
            item.UpdatedAtIst = IndianStandardTime.Now;
            item.UpdatedBy = actor.Id;

            // The old file is untouched until this save succeeds; a failure disposes the new file (removing it) and the old one stays referenced.
            await _repository.UpdateAsync(item, cancellationToken);
            file?.Complete();

            if (previousPath != null && !_storage.TryDelete(previousPath))
                _logger.LogWarning("Replaced Digital Library file of record {RecordId} could not be deleted", item.Id);

            await AuditAsync("DigitalLibraryUpdate", item.Id, actor);
            return Result.Success(item.ToAdminDto());
        }
        finally
        {
            if (file != null) await file.DisposeAsync();
        }
    }

    public async Task<Result<ApiResult<DigitalLibraryAdminDto>>> DeactivateAsync(long id, CancellationToken cancellationToken)
    {
        if (!_currentUser.TryGetAdminActor(out AdminActor actor)) return Result.Failure<ApiResult<DigitalLibraryAdminDto>>(ApiErrorCodes.Unauthorized, UnauthorizedMessage);
        if (id < 1) return Result.Failure<ApiResult<DigitalLibraryAdminDto>>(ApiErrorCodes.ValidationFailed, "id must be 1 or greater.");

        DigitalLibraryItem? item = await _repository.GetForUpdateAsync(id, cancellationToken);
        if (item == null) return Result.Failure<ApiResult<DigitalLibraryAdminDto>>(ApiErrorCodes.NotFound, DigitalLibraryRules.NotFoundMessage);
        if (!item.IsActive)
            return Result.Success(new ApiResult<DigitalLibraryAdminDto>(DigitalLibraryRules.AlreadyInactiveMessage, item.ToAdminDto()));

        // Soft delete only: the row and the stored file are kept.
        item.IsActive = false;
        item.UpdatedAtIst = IndianStandardTime.Now;
        item.UpdatedBy = actor.Id;
        await _repository.UpdateAsync(item, cancellationToken);

        await AuditAsync("DigitalLibraryDeactivate", item.Id, actor);
        return Result.Success(new ApiResult<DigitalLibraryAdminDto>(DigitalLibraryRules.DeactivatedMessage, item.ToAdminDto()));
    }

    private static string? ValidateDescription(string? description)
    {
        if (string.IsNullOrWhiteSpace(description)) return "description is required.";
        if (description.Trim().Length > DigitalLibraryRules.DescriptionMaxLength)
            return $"description must be at most {DigitalLibraryRules.DescriptionMaxLength} characters.";
        return null;
    }

    private async Task<Result<IDigitalLibraryStagedFile>> StageSafelyAsync(DigitalLibraryFileType type, DigitalLibraryUploadFile upload, CancellationToken cancellationToken)
    {
        try
        {
            return await _storage.StageAsync(type, upload.Content, _settings.MaxFileSizeBytes, cancellationToken);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogError(ex, "A Digital Library upload could not be staged");
            return Result.Failure<IDigitalLibraryStagedFile>(ApiErrorCodes.InternalError, DigitalLibraryRules.StorageFailureMessage);
        }
    }

    private bool TryCommit(IDigitalLibraryStagedFile file)
    {
        try
        {
            file.Commit();
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            _logger.LogError(ex, "A Digital Library file could not be moved into place");
            return false;
        }
    }

    private async Task AuditAsync(string operation, long recordId, AdminActor actor)
    {
        try
        {
            await _audit.LogAsync(AuditAction.AdminAction, DomainConstants.AuditResults.Success,
                userId: actor.Key, entityType: nameof(DigitalLibraryItem), entityId: recordId.ToString(), correlationId: _currentUser.CorrelationId,
                additionalData: $"{{\"operation\":\"{operation}\",\"adminProfileId\":{actor.Id}}}",
                cancellationToken: CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Audit entry for Digital Library record {RecordId} could not be written", recordId);
        }
    }
}
