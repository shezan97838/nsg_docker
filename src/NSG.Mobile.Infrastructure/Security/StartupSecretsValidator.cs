using Microsoft.Extensions.Configuration;

namespace NSG.Mobile.Infrastructure.Security;

/// <summary>
/// Refuses to start the host with a missing, placeholder or weak secret. The committed <c>appsettings.json</c> carries
/// <c>REPLACE_*</c> placeholders that are public: a deployment that forgets the environment variables must stop, not sign tokens
/// with a key everybody knows. Messages never include the offending value.
/// </summary>
public static class StartupSecretsValidator
{
    private const string PlaceholderPrefix = "REPLACE_";
    private const int MinJwtSecretBytes = 32;

    public static void EnsureValid(IConfiguration configuration)
    {
        string jwt = configuration["Jwt:Secret"] ?? string.Empty;
        if (string.IsNullOrWhiteSpace(jwt) || jwt.StartsWith(PlaceholderPrefix, StringComparison.Ordinal))
            throw new InvalidOperationException("Jwt:Secret is not configured (empty or a placeholder). Set the Jwt__Secret environment variable to a random value of at least 32 bytes.");
        if (System.Text.Encoding.UTF8.GetByteCount(jwt) < MinJwtSecretBytes)
            throw new InvalidOperationException("Jwt:Secret is too short: HMAC-SHA256 needs at least 32 bytes of random key material.");

        RequireSet(configuration, "Jwt:Issuer");
        RequireSet(configuration, "Jwt:Audience");

        byte[] encryptionKey = RequireBase64(configuration, "Encryption:Key", exactBytes: 32);
        byte[] blindIndexKey = RequireBase64(configuration, "BlindIndex:Key", minBytes: 32);
        if (encryptionKey.AsSpan().SequenceEqual(blindIndexKey))
            throw new InvalidOperationException("BlindIndex:Key must be different from Encryption:Key.");
    }

    private static void RequireSet(IConfiguration configuration, string key)
    {
        string value = configuration[key] ?? string.Empty;
        if (string.IsNullOrWhiteSpace(value) || value.StartsWith(PlaceholderPrefix, StringComparison.Ordinal))
            throw new InvalidOperationException($"{key} is not configured (empty or a placeholder).");
    }

    private static byte[] RequireBase64(IConfiguration configuration, string key, int exactBytes = 0, int minBytes = 0)
    {
        string value = configuration[key] ?? string.Empty;
        byte[] bytes;
        try { bytes = Convert.FromBase64String(value); }
        catch (FormatException) { throw new InvalidOperationException($"{key} must be a base64 string (it is empty, a placeholder or malformed)."); }

        if ((exactBytes > 0 && bytes.Length != exactBytes) || (minBytes > 0 && bytes.Length < minBytes))
            throw new InvalidOperationException($"{key} has the wrong length: expected {(exactBytes > 0 ? $"exactly {exactBytes}" : $"at least {minBytes}")} bytes.");
        return bytes;
    }
}
