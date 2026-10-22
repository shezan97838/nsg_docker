namespace NSG.Mobile.Infrastructure.Options;

/// <summary>
/// Cloudflare R2 (S3-compatible) object storage. The bucket is private; the application is the only reader and writer.
/// <see cref="AccessKey"/> and <see cref="SecretKey"/> come from user-secrets or the environment variables
/// <c>R2Storage__AccessKey</c> / <c>R2Storage__SecretKey</c> — never from a committed file.
/// R2 is used only when <see cref="ServiceUrl"/> holds a real value; otherwise the local file storage stays in place.
/// </summary>
public class R2Options
{
    public const string SectionName = "R2Storage";
    private const string PlaceholderPrefix = "REPLACE_WITH";

    /// <summary><c>https://&lt;ACCOUNT_ID&gt;.r2.cloudflarestorage.com</c> (no bucket name in the URL).</summary>
    public string ServiceUrl { get; set; } = string.Empty;
    public string AccessKey { get; set; } = string.Empty;
    public string SecretKey { get; set; } = string.Empty;
    public string BucketName { get; set; } = string.Empty;

    /// <summary>Optional folder in front of every object key (for example one per environment). Not part of the keys the application stores.</summary>
    public string KeyPrefix { get; set; } = string.Empty;

    /// <summary>True when a real service URL is configured, which switches storage to R2 and makes the other settings mandatory.</summary>
    public bool IsEnabled => IsReal(ServiceUrl);

    /// <summary>Throws with a message naming the missing settings (never their values) when R2 is enabled but incomplete.</summary>
    public void EnsureValid()
    {
        List<string> problems = new();

        if (!Uri.TryCreate(ServiceUrl, UriKind.Absolute, out Uri? uri) || uri.Scheme != Uri.UriSchemeHttps)
            problems.Add($"{SectionName}:ServiceUrl must be an absolute https URL (https://<ACCOUNT_ID>.r2.cloudflarestorage.com)");
        if (!IsReal(BucketName))
            problems.Add($"{SectionName}:BucketName is missing");
        if (!IsReal(AccessKey))
            problems.Add($"{SectionName}:AccessKey is missing (set user-secret or environment variable {SectionName}__AccessKey)");
        if (!IsReal(SecretKey))
            problems.Add($"{SectionName}:SecretKey is missing (set user-secret or environment variable {SectionName}__SecretKey)");

        if (problems.Count > 0)
            throw new InvalidOperationException("Cloudflare R2 storage is enabled but not configured correctly: " + string.Join("; ", problems) + ".");
    }

    /// <summary><see cref="KeyPrefix"/> without surrounding slashes, plus one trailing slash; empty when no prefix is configured.</summary>
    public string NormalizedPrefix
    {
        get
        {
            string trimmed = (KeyPrefix ?? string.Empty).Trim().Trim('/');
            return trimmed.Length == 0 ? string.Empty : trimmed + "/";
        }
    }

    private static bool IsReal(string? value)
        => !string.IsNullOrWhiteSpace(value) && !value.StartsWith(PlaceholderPrefix, StringComparison.Ordinal);
}
