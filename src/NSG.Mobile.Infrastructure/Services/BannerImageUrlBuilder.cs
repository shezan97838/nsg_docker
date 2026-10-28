using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using NSG.Mobile.Application.Banners;
using NSG.Mobile.Infrastructure.Options;

namespace NSG.Mobile.Infrastructure.Services;

/// <summary>
/// Builds absolute banner image URLs from <c>PublicApi:BaseUrl</c> when it is configured, otherwise from the current request
/// (<c>{scheme}://{host}{pathBase}</c>). A configured value that is not an absolute http(s) address is a configuration error and is reported, not ignored.
/// </summary>
public class BannerImageUrlBuilder : IBannerImageUrlBuilder
{
    private readonly IOptions<PublicApiOptions> _options;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public BannerImageUrlBuilder(IOptions<PublicApiOptions> options, IHttpContextAccessor httpContextAccessor)
    {
        _options = options;
        _httpContextAccessor = httpContextAccessor;
    }

    public string ToAbsolute(string relativePath)
    {
        string path = "/" + relativePath.TrimStart('/');
        string configured = (_options.Value.BaseUrl ?? string.Empty).Trim();

        if (configured.Length > 0)
        {
            if (!Uri.TryCreate(configured, UriKind.Absolute, out Uri? uri) || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp)
                || !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
                throw new InvalidOperationException("PublicApi:BaseUrl must be an absolute http(s) address without credentials, query or fragment.");
            return configured.TrimEnd('/') + path;
        }

        HttpRequest request = _httpContextAccessor.HttpContext?.Request
            ?? throw new InvalidOperationException("PublicApi:BaseUrl is not configured and there is no current request to take the address from.");
        return $"{request.Scheme}://{request.Host.Value}{request.PathBase.Value}{path}";
    }
}
