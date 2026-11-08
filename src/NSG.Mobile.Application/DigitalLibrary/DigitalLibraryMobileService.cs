using Microsoft.Extensions.Logging;
using NSG.Mobile.Application.Common;
using NSG.Mobile.Application.Interfaces.Infrastructure;
using NSG.Mobile.Application.Interfaces.Repositories;
using NSG.Mobile.Domain.Entities;

namespace NSG.Mobile.Application.DigitalLibrary;

/// <summary>What a signed-in mobile user can do with the Digital Library: list and open active documents.</summary>
public interface IDigitalLibraryMobileService
{
    /// <summary>Active records, newest first (created time, then id), optionally only the important (or only the non-important) ones.</summary>
    Task<Result<PaginatedResponse<DigitalLibraryMobileDto>>> GetPagedAsync(bool? isImportant, int pageNumber, int pageSize, CancellationToken cancellationToken);

    /// <summary>Opens the file of an active record. Inactive, missing or unreadable is a 404.</summary>
    Task<Result<DigitalLibraryDownload>> OpenFileAsync(long id, CancellationToken cancellationToken);
}

public class DigitalLibraryMobileService : IDigitalLibraryMobileService
{
    private readonly IDigitalLibraryRepository _repository;
    private readonly IDigitalLibraryStorageService _storage;
    private readonly ILogger<DigitalLibraryMobileService> _logger;

    public DigitalLibraryMobileService(IDigitalLibraryRepository repository, IDigitalLibraryStorageService storage, ILogger<DigitalLibraryMobileService> logger)
    {
        _repository = repository;
        _storage = storage;
        _logger = logger;
    }

    public async Task<Result<PaginatedResponse<DigitalLibraryMobileDto>>> GetPagedAsync(bool? isImportant, int pageNumber, int pageSize, CancellationToken cancellationToken)
    {
        if (pageNumber < 1 || pageSize < 1 || pageSize > DigitalLibraryRules.MaxPageSize)
            return Result.Failure<PaginatedResponse<DigitalLibraryMobileDto>>(ApiErrorCodes.ValidationFailed,
                $"pageNumber must be 1 or greater and pageSize between 1 and {DigitalLibraryRules.MaxPageSize}.");

        (IReadOnlyList<DigitalLibraryItem> items, int total) = await _repository.GetActivePagedAsync(isImportant, pageNumber, pageSize, cancellationToken);
        return Result.Success(new PaginatedResponse<DigitalLibraryMobileDto>(items.Select(i => i.ToMobileDto()).ToList(), total, pageNumber, pageSize));
    }

    public async Task<Result<DigitalLibraryDownload>> OpenFileAsync(long id, CancellationToken cancellationToken)
    {
        if (id < 1)
            return Result.Failure<DigitalLibraryDownload>(ApiErrorCodes.ValidationFailed, "id must be 1 or greater.");

        DigitalLibraryItem? item = await _repository.GetActiveByIdAsync(id, cancellationToken);
        if (item == null)
            return Result.Failure<DigitalLibraryDownload>(ApiErrorCodes.NotFound, DigitalLibraryRules.NotFoundMessage);

        Stream? stream;
        try
        {
            stream = _storage.OpenRead(item.FilePath);
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            _logger.LogError(ex, "Digital Library record {RecordId} could not be opened", id);
            stream = null;
        }

        if (stream == null)
        {
            _logger.LogWarning("Digital Library record {RecordId} is recorded but its file is missing", id);
            return Result.Failure<DigitalLibraryDownload>(ApiErrorCodes.NotFound, DigitalLibraryRules.FileUnavailableMessage);
        }

        // Content type and name come from the validated file_type column, never from the client or the stored path.
        return Result.Success(new DigitalLibraryDownload(stream, DigitalLibraryRules.ContentType(item.FileType), $"digital-library-{id}.{item.FileType.ToLowerInvariant()}"));
    }
}
