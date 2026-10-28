using System.Text.Json;
using Microsoft.Extensions.Logging;
using NSG.Mobile.Application.Banners.DTOs;
using NSG.Mobile.Application.Banners.Mappings;
using NSG.Mobile.Application.Banners.Validators;
using NSG.Mobile.Application.Common;
using NSG.Mobile.Application.Grievances;
using NSG.Mobile.Application.Grievances.DTOs;
using NSG.Mobile.Application.Interfaces.Infrastructure;
using NSG.Mobile.Application.Interfaces.Repositories;
using NSG.Mobile.Application.Interfaces.Services;
using NSG.Mobile.Domain.Common;
using NSG.Mobile.Domain.Constants;
using NSG.Mobile.Domain.Entities;
using NSG.Mobile.Domain.Enums;

namespace NSG.Mobile.Application.Banners.Services;

/// <summary>
/// Banners: the active list and image for employees, and create/update/status/read (and image preview) for Media Cell admins. Images go to the
/// private object storage through <see cref="IFileStorageService"/> (the same abstraction, availability guard and key shape as Grievance and DG
/// messages); only the object key is saved in the database.
/// </summary>
public interface IBannerService
{
    /// <summary>Active banners in display order: priority ascending (null last), then newest id (paged).</summary>
    Task<Result<PaginatedResponse<BannerDisplayDto>>> GetDisplayAsync(int pageNumber, int pageSize, CancellationToken cancellationToken);

    /// <summary>All banners including inactive and expired ones (paged).</summary>
    Task<Result<PaginatedResponse<BannerAdminDto>>> GetAllAsync(int pageNumber, int pageSize, CancellationToken cancellationToken);

    Task<Result<BannerAdminDto>> GetByIdAsync(int id, CancellationToken cancellationToken);

    /// <summary>Opens the private image of an active banner.</summary>
    Task<Result<GrievanceDocumentContent>> OpenDisplayImageAsync(int id, CancellationToken cancellationToken);

    /// <summary>Opens the private image of any banner for the Media Cell admin (preview).</summary>
    Task<Result<GrievanceDocumentContent>> OpenAdminImageAsync(int id, CancellationToken cancellationToken);

    Task<Result<ApiResult<BannerAdminDto>>> CreateAsync(SaveBannerRequestDto request, BannerImageUpload? image, CancellationToken cancellationToken);

    /// <summary>
    /// Replaces every editable field (<c>isActive</c> is required). The image is replaced when <paramref name="image"/> is given, removed when
    /// <see cref="SaveBannerRequestDto.RemoveImage"/> is true, and otherwise kept.
    /// </summary>
    Task<Result<ApiResult<BannerAdminDto>>> UpdateAsync(int id, SaveBannerRequestDto request, BannerImageUpload? image, CancellationToken cancellationToken);

    /// <summary>Activates or deactivates. Repeating the request answers with a message instead of failing.</summary>
    Task<Result<ApiResult<BannerAdminDto>>> SetStatusAsync(int id, UpdateBannerStatusRequestDto request, CancellationToken cancellationToken);
}

public class BannerService : IBannerService
{
    private static readonly byte[] JpegMagic = { 0xFF, 0xD8, 0xFF };
    private static readonly byte[] PngMagic = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };

    private readonly IBannerRepository _repository;
    private readonly INsgUnitRepository _units;
    private readonly IBannerImageUrlBuilder _urls;
    private readonly IFileStorageService _storage;
    private readonly IStorageAvailability _availability;
    private readonly IAuditService _audit;
    private readonly ICurrentUserService _currentUser;
    private readonly TimeProvider _time;
    private readonly ILogger<BannerService> _logger;

    public BannerService(IBannerRepository repository, INsgUnitRepository units, IBannerImageUrlBuilder urls, IFileStorageService storage, IStorageAvailability availability,
        IAuditService audit, ICurrentUserService currentUser, TimeProvider time, ILogger<BannerService> logger)
    {
        _repository = repository;
        _units = units;
        _urls = urls;
        _storage = storage;
        _availability = availability;
        _audit = audit;
        _currentUser = currentUser;
        _time = time;
        _logger = logger;
    }

    public async Task<Result<PaginatedResponse<BannerDisplayDto>>> GetDisplayAsync(int pageNumber, int pageSize, CancellationToken cancellationToken)
    {
        (IReadOnlyList<Banner> items, int total) = await _repository.GetActivePagedAsync(pageNumber, pageSize, cancellationToken);
        return Result.Success(new PaginatedResponse<BannerDisplayDto>(items.Select(b => b.ToDisplayDto(_urls)).ToList(), total, pageNumber, pageSize));
    }

    public async Task<Result<PaginatedResponse<BannerAdminDto>>> GetAllAsync(int pageNumber, int pageSize, CancellationToken cancellationToken)
    {
        (IReadOnlyList<Banner> items, int total) = await _repository.GetAllPagedAsync(pageNumber, pageSize, cancellationToken);
        return Result.Success(new PaginatedResponse<BannerAdminDto>(items.Select(b => b.ToAdminDto(_urls)).ToList(), total, pageNumber, pageSize));
    }

    public async Task<Result<BannerAdminDto>> GetByIdAsync(int id, CancellationToken cancellationToken)
    {
        Banner? banner = await _repository.GetByIdAsync(id, cancellationToken);
        return banner == null
            ? Result.Failure<BannerAdminDto>(ApiErrorCodes.NotFound, BannerLimits.NotFoundMessage)
            : Result.Success(banner.ToAdminDto(_urls));
    }

    public async Task<Result<GrievanceDocumentContent>> OpenDisplayImageAsync(int id, CancellationToken cancellationToken)
        => await OpenImageAsync(await _repository.GetActiveByIdAsync(id, cancellationToken), id, cancellationToken);

    public async Task<Result<GrievanceDocumentContent>> OpenAdminImageAsync(int id, CancellationToken cancellationToken)
        => await OpenImageAsync(await _repository.GetByIdAsync(id, cancellationToken), id, cancellationToken);

    public async Task<Result<ApiResult<BannerAdminDto>>> CreateAsync(SaveBannerRequestDto request, BannerImageUpload? image, CancellationToken cancellationToken)
    {
        Result<Prepared> prepared = await PrepareAsync(request, image, null, isUpdate: false, cancellationToken);
        if (!prepared.IsSuccess)
            return Result.Failure<ApiResult<BannerAdminDto>>(prepared.ErrorCode!, prepared.ErrorMessage!);

        Prepared p = prepared.Data!;
        try
        {
            string? key = await StoreAsync(p, cancellationToken);
            if (p.Image != null && key == null)
                return Result.Failure<ApiResult<BannerAdminDto>>(GrievanceErrorCodes.StorageUnavailable, GrievanceMessages.StorageUnavailable);

            DateTime now = NowIst();
            Banner banner = new()
            {
                Title = p.Values.Title,
                Description = p.Values.Description,
                Category = p.Values.Category,
                ImageStorageKey = key,
                ImageAltText = key != null ? p.Values.ImageAltText : null,   // alt text only describes an image
                ShowOnlyImage = key != null && p.Values.ShowOnlyImage,
                NsgUnitId = p.Values.NsgUnitId,
                Priority = p.Values.Priority,
                IsActive = p.Values.IsActive,
                CreatedAtIst = now,
                UpdatedAtIst = now
            };

            try
            {
                await _repository.AddAsync(banner, cancellationToken);
            }
            catch
            {
                await RemoveOrphanAsync(key);   // the object exists but the row does not: do not leave it behind
                throw;
            }

            await AuditAsync(AuditAction.BannerCreated, banner.Id, cancellationToken);
            return Result.Success(new ApiResult<BannerAdminDto>("Banner created successfully.", banner.ToAdminDto(_urls)));
        }
        finally
        {
            p.Buffer?.Dispose();
        }
    }

    public async Task<Result<ApiResult<BannerAdminDto>>> UpdateAsync(int id, SaveBannerRequestDto request, BannerImageUpload? image, CancellationToken cancellationToken)
    {
        Banner? existing = await _repository.GetByIdAsync(id, cancellationToken);
        Result<Prepared> prepared = await PrepareAsync(request, image, existing, isUpdate: true, cancellationToken);
        if (!prepared.IsSuccess)
            return Result.Failure<ApiResult<BannerAdminDto>>(prepared.ErrorCode!, prepared.ErrorMessage!);

        Prepared p = prepared.Data!;
        try
        {
            string? newKey = await StoreAsync(p, cancellationToken);
            if (p.Image != null && newKey == null)
                return Result.Failure<ApiResult<BannerAdminDto>>(GrievanceErrorCodes.StorageUnavailable, GrievanceMessages.StorageUnavailable);

            BannerUpdateResult result;
            try
            {
                result = await _repository.UpdateAsync(id, p.Values, newKey, p.RemoveImage, NowIst(), cancellationToken);
            }
            catch
            {
                await RemoveOrphanAsync(newKey);
                throw;
            }

            if (result.Banner == null)
            {
                await RemoveOrphanAsync(newKey);
                return Result.Failure<ApiResult<BannerAdminDto>>(ApiErrorCodes.NotFound, BannerLimits.NotFoundMessage);
            }

            // The row now points at the new object (or at none); the replaced or removed one is deleted on a best-effort basis.
            if ((newKey != null || p.RemoveImage) && !string.IsNullOrEmpty(result.PreviousImageKey))
                await RemoveOrphanAsync(result.PreviousImageKey);

            await AuditAsync(AuditAction.BannerUpdated, id, cancellationToken);
            return Result.Success(new ApiResult<BannerAdminDto>("Banner updated successfully.", result.Banner.ToAdminDto(_urls)));
        }
        finally
        {
            p.Buffer?.Dispose();
        }
    }

    public async Task<Result<ApiResult<BannerAdminDto>>> SetStatusAsync(int id, UpdateBannerStatusRequestDto request, CancellationToken cancellationToken)
    {
        if (request.IsActive == null)
            return Result.Failure<ApiResult<BannerAdminDto>>(ApiErrorCodes.ValidationFailed, "isActive is required.");

        bool active = request.IsActive.Value;
        BannerStatusResult result = await _repository.SetStatusAsync(id, active, NowIst(), cancellationToken);
        if (result.Outcome == BannerStatusOutcome.NotFound)
            return Result.Failure<ApiResult<BannerAdminDto>>(ApiErrorCodes.NotFound, BannerLimits.NotFoundMessage);

        if (result.Outcome == BannerStatusOutcome.Unchanged)
            return Result.Success(new ApiResult<BannerAdminDto>($"Banner is already {(active ? "active" : "inactive")}.", result.Banner!.ToAdminDto(_urls)));

        await AuditAsync(AuditAction.BannerStatusChanged, id, cancellationToken);
        return Result.Success(new ApiResult<BannerAdminDto>($"Banner {(active ? "activated" : "deactivated")} successfully.", result.Banner!.ToAdminDto(_urls)));
    }

    // ── Image retrieval ──

    private async Task<Result<GrievanceDocumentContent>> OpenImageAsync(Banner? banner, int id, CancellationToken cancellationToken)
    {
        if (banner == null)
            return Result.Failure<GrievanceDocumentContent>(ApiErrorCodes.NotFound, BannerLimits.NotFoundMessage);
        if (string.IsNullOrEmpty(banner.ImageStorageKey))
            return Result.Failure<GrievanceDocumentContent>(ApiErrorCodes.NotFound, BannerLimits.NoImageMessage);

        // Outside Development only private R2 may serve files; a host without it must never read the local disk.
        if (!_availability.IsAvailable)
            return Result.Failure<GrievanceDocumentContent>(GrievanceErrorCodes.StorageUnavailable, GrievanceMessages.StorageUnavailable);

        FileStorageResult? file;
        try
        {
            file = await _storage.GetAsync(banner.ImageStorageKey, cancellationToken);
        }
        catch (FileStorageUnavailableException)
        {
            return Result.Failure<GrievanceDocumentContent>(GrievanceErrorCodes.StorageUnavailable, GrievanceMessages.StorageUnavailable);
        }

        if (file == null)
        {
            _logger.LogWarning("Banner {BannerId} image is recorded but missing in storage", id);
            return Result.Failure<GrievanceDocumentContent>(ApiErrorCodes.NotFound, "The image is no longer available.");
        }

        // The type is derived from the validated extension in the key, never from what storage reports.
        bool png = banner.ImageStorageKey.EndsWith(".png", StringComparison.OrdinalIgnoreCase);
        return Result.Success(new GrievanceDocumentContent(file.Stream, png ? "image/png" : "image/jpeg", png ? "banner.png" : "banner.jpg"));
    }

    // ── Write helpers ──

    /// <summary>Validated values plus the checked image (with the in-memory copy made for a non-seekable stream).</summary>
    private sealed record Prepared(BannerValues Values, bool RemoveImage, BannerImageUpload? Image, Stream? Content, MemoryStream? Buffer, string Extension);

    private async Task<Result<Prepared>> PrepareAsync(SaveBannerRequestDto request, BannerImageUpload? image, Banner? existing, bool isUpdate, CancellationToken cancellationToken)
    {
        FluentValidation.Results.ValidationResult validation = new SaveBannerRequestValidator().Validate(request);
        if (!validation.IsValid)
            return Result.Failure<Prepared>(ApiErrorCodes.ValidationFailed, string.Join(" ", validation.Errors.Select(e => e.ErrorMessage).Distinct()));
        if (image != null && request.RemoveImage == true)
            return Result.Failure<Prepared>(ApiErrorCodes.ValidationFailed, "Send either a new image or removeImage=true, not both.");
        if (isUpdate && existing == null)
            return Result.Failure<Prepared>(ApiErrorCodes.NotFound, BannerLimits.NotFoundMessage);

        // Image-only display needs an image. Work out whether the banner will have one after this request, before anything is uploaded.
        bool existingHasImage = !string.IsNullOrEmpty(existing?.ImageStorageKey);
        bool hasImageAfter = image != null || (existingHasImage && request.RemoveImage != true);
        if (request.ShowOnlyImage == true && !hasImageAfter)
            return Result.Failure<Prepared>(ApiErrorCodes.ValidationFailed, BannerLimits.ShowOnlyImageNeedsImageMessage);
        bool showOnlyImage = hasImageAfter && (request.ShowOnlyImage ?? existing?.ShowOnlyImage ?? false);

        if (request.NsgUnitId.HasValue)
        {
            NsgUnit? unit = await _units.GetByIdAsync(request.NsgUnitId.Value, cancellationToken);
            if (unit == null || !unit.IsActive)
                return Result.Failure<Prepared>(ApiErrorCodes.ValidationFailed, BannerLimits.UnitInvalidMessage);
        }

        BannerValues values = new(request.Title.Trim(), request.Description.Trim(), request.Category.Trim(),
            string.IsNullOrWhiteSpace(request.ImageAltText) ? null : request.ImageAltText.Trim(), request.NsgUnitId, request.Priority, request.IsActive!.Value, showOnlyImage);
        Prepared text = new(values, request.RemoveImage == true, null, null, null, string.Empty);
        if (image == null) return Result.Success(text);

        // Everything about the image is checked BEFORE anything is sent to storage.
        if (image.Length <= 0)
            return Result.Failure<Prepared>(ApiErrorCodes.ValidationFailed, "The uploaded image is empty.");
        if (image.Length > BannerLimits.ImageMaxBytes)
            return Result.Failure<Prepared>(ApiErrorCodes.ValidationFailed, $"The image is too large. The maximum size is {BannerLimits.ImageMaxMegabytes} MB.");

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

            if (content.Length - content.Position > BannerLimits.ImageMaxBytes)
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
            return await _storage.StoreAsync(prepared.Content, "banner" + prepared.Extension, contentType, BannerLimits.StorageCategory, cancellationToken);
        }
        catch (FileStorageUnavailableException)
        {
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
        catch (Exception ex) { _logger.LogError(ex, "Delete of banner object {StorageKey} threw", key); }
        if (!deleted) _logger.LogError("Banner object {StorageKey} could not be removed and needs manual clean-up", key);
    }

    private async Task AuditAsync(AuditAction action, int id, CancellationToken cancellationToken)
        => await _audit.LogAsync(action, DomainConstants.AuditResults.Success,
            entityType: nameof(Banner), entityId: id.ToString(), correlationId: _currentUser.CorrelationId,
            additionalData: JsonSerializer.Serialize(new { actorType = "Admin", actorId = _currentUser.AdminProfileId }),
            cancellationToken: cancellationToken);

    private DateTime NowIst() => IndianStandardTime.ConvertFromUtc(_time.GetUtcNow().UtcDateTime);
}
