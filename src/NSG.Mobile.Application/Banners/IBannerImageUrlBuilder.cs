namespace NSG.Mobile.Application.Banners;

/// <summary>
/// Turns a relative API route into the absolute URL the mobile app calls. The base address comes from configuration (<c>PublicApi:BaseUrl</c>)
/// or, when that is empty, from the current request; it is never hard-coded. Implemented in the Infrastructure layer.
/// </summary>
public interface IBannerImageUrlBuilder
{
    /// <param name="relativePath">A route that starts with <c>/</c>, for example <c>/api/v1/banners/5/image</c>.</param>
    string ToAbsolute(string relativePath);
}
