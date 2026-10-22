using System.Text.Json;
using Microsoft.Extensions.Logging;
using NSG.Mobile.Application.Common;
using NSG.Mobile.Application.DgMessages.DTOs;
using NSG.Mobile.Application.DgMessages.Mappings;
using NSG.Mobile.Application.DgMessages.Validators;
using NSG.Mobile.Application.Grievances;
using NSG.Mobile.Application.Grievances.DTOs;
using NSG.Mobile.Application.Interfaces.Infrastructure;
using NSG.Mobile.Application.Interfaces.Repositories;
using NSG.Mobile.Application.Interfaces.Services;
using NSG.Mobile.Domain.Common;
using NSG.Mobile.Domain.Constants;
using NSG.Mobile.Domain.Entities;
using NSG.Mobile.Domain.Enums;

namespace NSG.Mobile.Application.DgMessages.Services;

/// <summary>DG messages: the active list for employees (and the image), and create/update/soft-delete for Media Cell admins.</summary>
public interface IDgMessageService
{
    Task<Result<PaginatedResponse<DgMessageDto>>> GetActiveAsync(int pageNumber, int pageSize, CancellationToken cancellationToken);

    /// <summary>Opens the private image of an active, non-deleted message for streaming.</summary>
    Task<Result<GrievanceDocumentContent>> OpenImageAsync(int id, CancellationToken cancellationToken);

    Task<Result<ApiResult<DgMessageDto>>> CreateAsync(SaveDgMessageRequestDto request, DgImageUpload? image, CancellationToken cancellationToken);

    /// <summary>Replaces the text fields; the image is replaced only when <paramref name="image"/> is given, otherwise the existing one is kept.</summary>
    Task<Result<ApiResult<DgMessageDto>>> UpdateAsync(int id, SaveDgMessageRequestDto request, DgImageUpload? image, CancellationToken cancellationToken);

    /// <summary>Soft delete. Repeating the request answers with a message instead of failing.</summary>
    Task<Result<ApiMessageResponse>> DeleteAsync(int id, CancellationToken cancellationToken);
}

public class DgMessageService : IDgMessageService
{
    private static readonly byte[] JpegMagic = { 0xFF, 0xD8, 0xFF };
    private static readonly byte[] PngMagic = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };

    private readonly IDgMessageRepository _repository;
    private readonly IFileStorageService _storage;
    private readonly IStorageAvailability _availability;
    private readonly IAuditService _audit;
    private readonly ICurrentUserService _currentUser;
    private readonly TimeProvider _time;
    private readonly ILogger<DgMessageService> _logger;

    public DgMessageService(IDgMessageRepository repository, IFileStorageService storage, IStorageAvailability availability,
        IAuditService audit, ICurrentUserService currentUser, TimeProvider time, ILogger<DgMessageService> logger)
    {
        _repository = repository;
        _storage = storage;
        _availability = availability;
        _audit = audit;
        _currentUser = currentUser;
        _time = time;
        _logger = logger;
    }

    public async Task<Result<PaginatedResponse<DgMessageDto>>> GetActiveAsync(int pageNumber, int pageSize, CancellationToken cancellationToken)
    {
        (IReadOnlyList<DgMessage> items, int total) = await _repository.GetActivePagedAsync(pageNumber, pageSize, cancellationToken);
        return Result.Success(new PaginatedResponse<DgMessageDto>(items.Select(m => m.ToDto()).ToList(), total, pageNumber, pageSize));
    }

    public async Task<Result<GrievanceDocumentContent>> OpenImageAsync(int id, CancellationToken cancellationToken)
    {
        DgMessage? message = await _repository.GetActiveByIdAsync(id, cancellationToken);
        if (message == null)
            return Result.Failure<GrievanceDocumentContent>(ApiErrorCodes.NotFound, DgMessageLimits.NotFoundMessage);
        if (string.IsNullOrEmpty(message.ImageStorageKey))
            return Result.Failure<GrievanceDocumentContent>(ApiErrorCodes.NotFound, "This DG message has no image.");

        // Outside Development only private R2 may serve files; a host without it must never read the local disk.
        if (!_availability.IsAvailable)
            return Result.Failure<GrievanceDocumentContent>(GrievanceErrorCodes.StorageUnavailable, GrievanceMessages.StorageUnavailable);

        FileStorageResult? file;
        try
        {
            file = await _storage.GetAsync(message.ImageStorageKey, cancellationToken);
        }
        catch (FileStorageUnavailableException)
        {
            return Result.Failure<GrievanceDocumentContent>(GrievanceErrorCodes.StorageUnavailable, GrievanceMessages.StorageUnavailable);
        }

        if (file == null)
        {
            _logger.LogWarning("DG message {MessageId} image is recorded but missing in storage", id);
            return Result.Failure<GrievanceDocumentContent>(ApiErrorCodes.NotFound, "The image is no longer available.");
        }

        // The type is derived from the validated extension in the key, never from what storage reports.
        bool png = message.ImageStorageKey.EndsWith(".png", StringComparison.OrdinalIgnoreCase);
        return Result.Success(new GrievanceDocumentContent(file.Stream, png ? "image/png" : "image/jpeg", png ? "dg-message.png" : "dg-message.jpg"));
    }

    public async Task<Result<ApiResult<DgMessageDto>>> CreateAsync(SaveDgMessageRequestDto request, DgImageUpload? image, CancellationToken cancellationToken)
    {
        Result<Prepared> prepared = await PrepareAsync(request, image, requireIsActive: false, cancellationToken);
        if (!prepared.IsSuccess)
            return Result.Failure<ApiResult<DgMessageDto>>(prepared.ErrorCode!, prepared.ErrorMessage!);

        string? key = await StoreAsync(prepared.Data!, cancellationToken);
        if (prepared.Data!.Image != null && key == null)
            return Result.Failure<ApiResult<DgMessageDto>>(GrievanceErrorCodes.StorageUnavailable, GrievanceMessages.StorageUnavailable);

        DgMessage message = new()
        {
            DgName = prepared.Data.DgName,
            Designation = prepared.Data.Designation,
            Message = prepared.Data.Message,
            ImageStorageKey = key,
            IsActive = prepared.Data.IsActive,
            IsDeleted = false,
            CreatedAtIst = NowIst(),
            CreatedBy = _currentUser.AdminProfileId
        };

        try
        {
            await _repository.AddAsync(message, cancellationToken);
        }
        catch
        {
            await RemoveOrphanAsync(key);   // the object exists but the row does not: do not leave it behind
            throw;
        }
        finally
        {
            prepared.Data.Buffer?.Dispose();
        }

        await AuditAsync(AuditAction.DgMessageCreated, message.Id, cancellationToken);
        return Result.Success(new ApiResult<DgMessageDto>("DG message created successfully.", message.ToDto()));
    }

    public async Task<Result<ApiResult<DgMessageDto>>> UpdateAsync(int id, SaveDgMessageRequestDto request, DgImageUpload? image, CancellationToken cancellationToken)
    {
        Result<Prepared> prepared = await PrepareAsync(request, image, requireIsActive: true, cancellationToken);
        if (!prepared.IsSuccess)
            return Result.Failure<ApiResult<DgMessageDto>>(prepared.ErrorCode!, prepared.ErrorMessage!);

        string? newKey = await StoreAsync(prepared.Data!, cancellationToken);
        if (prepared.Data!.Image != null && newKey == null)
            return Result.Failure<ApiResult<DgMessageDto>>(GrievanceErrorCodes.StorageUnavailable, GrievanceMessages.StorageUnavailable);

        DgMessageUpdateResult result;
        try
        {
            result = await _repository.UpdateAsync(id, prepared.Data.DgName, prepared.Data.Designation, prepared.Data.Message,
                prepared.Data.IsActive, newKey, _currentUser.AdminProfileId, NowIst(), cancellationToken);
        }
        catch
        {
            await RemoveOrphanAsync(newKey);
            throw;
        }
        finally
        {
            prepared.Data.Buffer?.Dispose();
        }

        if (result.Outcome == DgMessageUpdateOutcome.NotFound)
        {
            await RemoveOrphanAsync(newKey);
            return Result.Failure<ApiResult<DgMessageDto>>(ApiErrorCodes.NotFound, DgMessageLimits.NotFoundMessage);
        }

        // The row now points at the new object; the replaced one is removed on a best-effort basis.
        if (newKey != null && !string.IsNullOrEmpty(result.PreviousImageKey))
            await RemoveOrphanAsync(result.PreviousImageKey);

        await AuditAsync(AuditAction.DgMessageUpdated, id, cancellationToken);
        return Result.Success(new ApiResult<DgMessageDto>("DG message updated successfully.", result.Message!.ToDto()));
    }

    public async Task<Result<ApiMessageResponse>> DeleteAsync(int id, CancellationToken cancellationToken)
    {
        switch (await _repository.SoftDeleteAsync(id, _currentUser.AdminProfileId, NowIst(), cancellationToken))
        {
            case DgMessageDeleteOutcome.NotFound:
                return Result.Failure<ApiMessageResponse>(ApiErrorCodes.NotFound, DgMessageLimits.NotFoundMessage);
            case DgMessageDeleteOutcome.AlreadyDeleted:
                return Result.Success(new ApiMessageResponse("DG message is already deleted."));
        }

        await AuditAsync(AuditAction.DgMessageDeleted, id, cancellationToken);
        return Result.Success(new ApiMessageResponse("DG message deleted successfully."));
    }

    // ── Helpers ──

    /// <summary>Validated text fields plus the checked image (with the in-memory copy made for a non-seekable stream).</summary>
    private sealed record Prepared(string DgName, string Designation, string Message, bool IsActive, DgImageUpload? Image, Stream? Content, MemoryStream? Buffer, string Extension);

    private async Task<Result<Prepared>> PrepareAsync(SaveDgMessageRequestDto request, DgImageUpload? image, bool requireIsActive, CancellationToken cancellationToken)
    {
        FluentValidation.Results.ValidationResult validation = new SaveDgMessageRequestValidator().Validate(request);
        if (!validation.IsValid)
            return Result.Failure<Prepared>(ApiErrorCodes.ValidationFailed, string.Join(" ", validation.Errors.Select(e => e.ErrorMessage).Distinct()));
        if (requireIsActive && request.IsActive == null)
            return Result.Failure<Prepared>(ApiErrorCodes.ValidationFailed, "isActive is required.");

        Prepared text = new(request.DgName.Trim(), request.Designation.Trim(), request.Message.Trim(), request.IsActive ?? true, null, null, null, string.Empty);
        if (image == null) return Result.Success(text);

        // Everything about the image is checked BEFORE anything is sent to storage.
        if (image.Length <= 0)
            return Result.Failure<Prepared>(ApiErrorCodes.ValidationFailed, "The uploaded image is empty.");
        if (image.Length > DgMessageLimits.ImageMaxBytes)
            return Result.Failure<Prepared>(ApiErrorCodes.ValidationFailed, $"The image is too large. The maximum size is {DgMessageLimits.ImageMaxMegabytes} MB.");

        string extension = Path.GetExtension((image.FileName ?? string.Empty).Replace('\\', '/')).ToLowerInvariant();
        string declared = (image.ContentType ?? string.Empty).Split(';')[0].Trim().ToLowerInvariant();
        bool jpeg = extension is ".jpg" or ".jpeg" && declared is "image/jpeg" or "image/jpg";
        bool png = extension == ".png" && declared == "image/png";
        if (!jpeg && !png)
            return Result.Failure<Prepared>(ApiErrorCodes.ValidationFailed, "Only JPEG or PNG images are accepted, and the content type must match the extension.");

        Stream content = image.Content;
        MemoryStream? buffer = null;
        try
        {
            if (!content.CanSeek)
            {
                buffer = new MemoryStream();
                await content.CopyToAsync(buffer, cancellationToken);
                buffer.Position = 0;
                content = buffer;
            }

            long size = content.Length - content.Position;
            if (size > DgMessageLimits.ImageMaxBytes)
            {
                buffer?.Dispose();
                return Result.Failure<Prepared>(ApiErrorCodes.ValidationFailed, "The image is larger than allowed.");
            }
            if (!await HasSignatureAsync(content, jpeg ? JpegMagic : PngMagic, cancellationToken))
            {
                buffer?.Dispose();
                return Result.Failure<Prepared>(ApiErrorCodes.ValidationFailed, "The image's content does not match its type.");
            }

            if (!_availability.IsAvailable)
            {
                buffer?.Dispose();
                return Result.Failure<Prepared>(GrievanceErrorCodes.StorageUnavailable, GrievanceMessages.StorageUnavailable);
            }

            return Result.Success(text with { Image = image, Content = content, Buffer = buffer, Extension = jpeg ? ".jpg" : ".png" });
        }
        catch
        {
            buffer?.Dispose();
            throw;
        }
    }

    /// <summary>Stores the checked image and returns its key; null when there is no image or storage is down.</summary>
    private async Task<string?> StoreAsync(Prepared prepared, CancellationToken cancellationToken)
    {
        if (prepared.Image == null || prepared.Content == null) return null;
        try
        {
            string contentType = prepared.Extension == ".png" ? "image/png" : "image/jpeg";
            return await _storage.StoreAsync(prepared.Content, "dg-message" + prepared.Extension, contentType, DgMessageLimits.StorageCategory, cancellationToken);
        }
        catch (FileStorageUnavailableException)
        {
            prepared.Buffer?.Dispose();
            return null;
        }
    }

    private static async Task<bool> HasSignatureAsync(Stream stream, byte[] expected, CancellationToken cancellationToken)
    {
        long start = stream.Position;
        byte[] head = new byte[expected.Length];
        int read = 0;
        while (read < head.Length)
        {
            int n = await stream.ReadAsync(head.AsMemory(read), cancellationToken);
            if (n == 0) break;
            read += n;
        }
        stream.Position = start;
        return read == expected.Length && head.AsSpan().SequenceEqual(expected);
    }

    private async Task RemoveOrphanAsync(string? key)
    {
        if (string.IsNullOrEmpty(key)) return;

        // Not tied to the request token: the cleanup must still run when the request was cancelled.
        bool deleted = false;
        try { deleted = await _storage.DeleteAsync(key, CancellationToken.None); }
        catch (Exception ex) { _logger.LogError(ex, "Delete of DG message object {StorageKey} threw", key); }
        if (!deleted) _logger.LogError("DG message object {StorageKey} could not be removed and needs manual clean-up", key);
    }

    private async Task AuditAsync(AuditAction action, int id, CancellationToken cancellationToken)
        => await _audit.LogAsync(action, DomainConstants.AuditResults.Success,
            entityType: nameof(DgMessage), entityId: id.ToString(), correlationId: _currentUser.CorrelationId,
            additionalData: JsonSerializer.Serialize(new { actorType = "Admin", actorId = _currentUser.AdminProfileId }),
            cancellationToken: cancellationToken);

    private DateTime NowIst() => IndianStandardTime.ConvertFromUtc(_time.GetUtcNow().UtcDateTime);
}
