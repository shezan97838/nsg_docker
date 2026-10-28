namespace NSG.Mobile.Domain.Entities;

/// <summary>
/// Home-screen banner shown to employees in the mobile app. Table <c>banners</c>. The image is optional: <see cref="ImageStorageKey"/> is the
/// image uploaded with the banner to private storage (R2); only its object key is saved. <see cref="NsgUnitId"/> is saved for possible future
/// unit-wise targeting and is not used for filtering today. A banner is shown while <see cref="IsActive"/> is true (manual activation, no schedule).
/// Timestamps are IST instants set by the server. Banners are never physically deleted.
/// </summary>
public class Banner
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;

    /// <summary>Free-text label shown with the banner (for example Urgent, Directive, Welfare). Required; the server stores whatever the Media Cell admin typed.</summary>
    public string Category { get; set; } = string.Empty;

    /// <summary>Object key of the image in the private bucket (never a URL); null when the banner is text only.</summary>
    public string? ImageStorageKey { get; set; }

    /// <summary>Alternative text of the image. Always null when the banner has no image.</summary>
    public string? ImageAltText { get; set; }

    /// <summary>When true the app shows the image alone (no description). Only meaningful with an image: always false when <see cref="ImageStorageKey"/> is null (also enforced by a CHECK constraint).</summary>
    public bool ShowOnlyImage { get; set; }

    /// <summary>Optional <c>nsg_units.id</c> kept for future targeting. Not used to filter anything yet.</summary>
    public int? NsgUnitId { get; set; }

    /// <summary>Display order for the mobile list: the lower the number, the earlier the banner appears. Null sorts after every number.</summary>
    public int? Priority { get; set; }

    public bool IsActive { get; set; }

    public DateTime CreatedAtIst { get; set; } = NSG.Mobile.Domain.Common.IndianStandardTime.Now;
    public DateTime UpdatedAtIst { get; set; } = NSG.Mobile.Domain.Common.IndianStandardTime.Now;
}
