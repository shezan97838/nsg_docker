namespace NSG.Mobile.Application.Banners.DTOs;

/// <summary>A banner as the mobile app sees it. The list is already in display order.</summary>
public class BannerDisplayDto
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;

    /// <summary>The saved category label (for example Urgent, Directive, Welfare), exactly as stored; never computed or hard-coded.</summary>
    public string Category { get; set; } = string.Empty;

    /// <summary>
    /// Absolute URL of the image route (<c>{api base}/api/v1/banners/{id}/image</c>); null for a text-only banner. The image is private: load it with
    /// the same <c>Authorization: Bearer</c> token as the other calls. The app falls back to the text when this is null or the image fails to load.
    /// </summary>
    public string? ImageUrl { get; set; }

    /// <summary>Null whenever the banner has no image.</summary>
    public string? ImageAltText { get; set; }

    /// <summary>True: show the image alone. False: image + description, or description only when there is no image. Always false when <see cref="ImageUrl"/> is null.</summary>
    public bool ShowOnlyImage { get; set; }

    /// <summary>Saved for future targeting; not used for filtering.</summary>
    public int? NsgUnitId { get; set; }

    /// <summary>Lower numbers are listed first; null sorts after every number.</summary>
    public int? Priority { get; set; }

    public bool IsActive { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>A banner as the Media Cell admin sees it: the same fields, with <see cref="BannerDisplayDto.ImageUrl"/> pointing at the admin preview route.</summary>
public class BannerAdminDto : BannerDisplayDto
{
    public bool HasImage { get; set; }
}

/// <summary>
/// Create or update body (the multipart form is adapted to it). The server sets and maintains <c>createdAt</c> and <c>updatedAt</c>; the image
/// key is never accepted from a client. <see cref="IsActive"/> is required.
/// </summary>
public class SaveBannerRequestDto
{
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string? ImageAltText { get; set; }

    /// <summary>
    /// Optional. True only makes sense with an image: sending true when the banner would have no image is a 400. Omitted: false on create; on update the
    /// current value is kept while the image is kept or replaced. Removing the image always sets false.
    /// </summary>
    public bool? ShowOnlyImage { get; set; }
    public int? NsgUnitId { get; set; }

    /// <summary>Optional. The lower the number, the earlier the banner is listed; omitted sorts last.</summary>
    public int? Priority { get; set; }

    public bool? IsActive { get; set; }

    /// <summary>Update only: true removes the existing image. Not allowed together with a new image. Without an image and without this flag the existing image is kept.</summary>
    public bool? RemoveImage { get; set; }
}

/// <summary>Body of the activate/deactivate call.</summary>
public class UpdateBannerStatusRequestDto
{
    public bool? IsActive { get; set; }
}

/// <summary>An uploaded image as the Application layer sees it (the controller adapts <c>IFormFile</c> to this).</summary>
public sealed record BannerImageUpload(Stream Content, string FileName, string ContentType, long Length);
