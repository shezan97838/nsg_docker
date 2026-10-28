using NSG.Mobile.Application.Banners.DTOs;
using NSG.Mobile.Application.Common;
using NSG.Mobile.Domain.Entities;

namespace NSG.Mobile.Application.Banners.Mappings;

/// <summary>
/// Explicit entity → DTO mapping. Stored IST instants leave the API with the fixed +05:30 offset; the image key never leaves the server, only the
/// absolute route that streams it. A banner without an image has neither an image URL nor alt text.
/// </summary>
public static class BannerMappingExtensions
{
    public static BannerDisplayDto ToDisplayDto(this Banner b, IBannerImageUrlBuilder urls)
    {
        BannerDisplayDto dto = new();
        Fill(dto, b, HasImage(b) ? urls.ToAbsolute(BannerLimits.MobileImagePath(b.Id)) : null);
        return dto;
    }

    public static BannerAdminDto ToAdminDto(this Banner b, IBannerImageUrlBuilder urls)
    {
        BannerAdminDto dto = new() { HasImage = HasImage(b) };
        Fill(dto, b, HasImage(b) ? urls.ToAbsolute(BannerLimits.AdminImagePath(b.Id)) : null);
        return dto;
    }

    private static void Fill(BannerDisplayDto dto, Banner b, string? imageUrl)
    {
        dto.Id = b.Id;
        dto.Title = b.Title;
        dto.Description = b.Description;
        dto.Category = b.Category;
        dto.ImageUrl = imageUrl;
        dto.ImageAltText = HasImage(b) ? b.ImageAltText : null;
        dto.ShowOnlyImage = HasImage(b) && b.ShowOnlyImage;   // never expose "image only" without an image, whatever is stored
        dto.NsgUnitId = b.NsgUnitId;
        dto.Priority = b.Priority;
        dto.IsActive = b.IsActive;
        dto.CreatedAt = IstTime.FromIst(b.CreatedAtIst);
        dto.UpdatedAt = IstTime.FromIst(b.UpdatedAtIst);
    }

    private static bool HasImage(Banner b) => !string.IsNullOrEmpty(b.ImageStorageKey);
}
