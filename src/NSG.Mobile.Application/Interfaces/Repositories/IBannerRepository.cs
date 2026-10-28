using NSG.Mobile.Domain.Entities;

namespace NSG.Mobile.Application.Interfaces.Repositories;

/// <summary>The text and state values an update replaces. The image is handled separately (see <see cref="IBannerRepository.UpdateAsync"/>).</summary>
public sealed record BannerValues(string Title, string Description, string Category, string? ImageAltText, int? NsgUnitId, int? Priority, bool IsActive, bool ShowOnlyImage);

/// <summary><see cref="PreviousImageKey"/> is the image key the row held before the update, so a replaced or removed object can be deleted afterwards.</summary>
public sealed record BannerUpdateResult(Banner? Banner, string? PreviousImageKey = null);

public enum BannerStatusOutcome { Changed = 0, Unchanged, NotFound }

public sealed record BannerStatusResult(BannerStatusOutcome Outcome, Banner? Banner = null);

/// <summary>Data access for <c>banners</c>. There is no physical delete.</summary>
public interface IBannerRepository
{
    Task AddAsync(Banner banner, CancellationToken cancellationToken = default);

    Task<Banner?> GetByIdAsync(int id, CancellationToken cancellationToken = default);

    /// <summary>One active banner; null when it does not exist or is inactive.</summary>
    Task<Banner?> GetActiveByIdAsync(int id, CancellationToken cancellationToken = default);

    /// <summary>Every banner, active or not, newest first.</summary>
    Task<(IReadOnlyList<Banner> Items, int TotalCount)> GetAllPagedAsync(int pageNumber, int pageSize, CancellationToken cancellationToken = default);

    /// <summary>Active banners in display order: priority ascending with null priorities last, then the highest id first.</summary>
    Task<(IReadOnlyList<Banner> Items, int TotalCount)> GetActivePagedAsync(int pageNumber, int pageSize, CancellationToken cancellationToken = default);

    /// <summary>
    /// Replaces the editable values and stamps <c>updated_at_ist</c>. The image key is replaced when <paramref name="newImageKey"/> is not null,
    /// cleared when <paramref name="removeImage"/> is true, and otherwise left untouched; a banner left without an image also loses its alt text and its show-only-image flag.
    /// <see cref="BannerUpdateResult.Banner"/> is null when the banner does not exist.
    /// </summary>
    Task<BannerUpdateResult> UpdateAsync(int id, BannerValues values, string? newImageKey, bool removeImage, DateTime nowIst, CancellationToken cancellationToken = default);

    Task<BannerStatusResult> SetStatusAsync(int id, bool isActive, DateTime nowIst, CancellationToken cancellationToken = default);
}
