namespace NSG.Mobile.Application.Banners;

/// <summary>Limits and messages of the Banner module.</summary>
public static class BannerLimits
{
    public const int TitleMaxLength = 200;
    public const int DescriptionMaxLength = 1000;
    public const int CategoryMaxLength = 100;
    public const int ImageAltTextMaxLength = 250;
    public const int PriorityMax = 9999;

    public const int ImageMaxMegabytes = 5;
    public const long ImageMaxBytes = ImageMaxMegabytes * 1024L * 1024L;

    /// <summary>Storage category, which makes object keys start with <c>banners/</c>.</summary>
    public const string StorageCategory = "banners";

    /// <summary>Routes (relative to the API base address) that stream a banner image; the client calls them with its bearer token.</summary>
    public static string MobileImagePath(int id) => $"/api/v1/banners/{id}/image";
    public static string AdminImagePath(int id) => $"/api/v1/admin/banners/{id}/image";

    public const string NotFoundMessage = "Banner not found.";
    public const string NoneFoundMessage = "No banners found.";
    public const string NoneActiveMessage = "No active banners found.";
    public const string NoImageMessage = "This banner has no image.";
    public const string ShowOnlyImageNeedsImageMessage = "showOnlyImage can only be true when the banner has an image.";
    public const string UnitInvalidMessage = "The selected NSG unit does not exist or is inactive.";
}
