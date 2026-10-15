using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using NSG.Mobile.Application.Interfaces.Infrastructure;
using NSG.Mobile.Infrastructure.Options;

namespace NSG.Mobile.Infrastructure.Security;

/// <summary>
/// HMAC-SHA256 blind index. The key is separate from the AES encryption key so that compromising one does not
/// compromise the other. Changing the key invalidates every stored hash (a re-index of all rows is then required).
/// </summary>
public class HmacBlindIndexService : IBlindIndexService
{
    private readonly byte[] _key;

    public HmacBlindIndexService(IOptions<BlindIndexOptions> options)
    {
        try
        {
            _key = Convert.FromBase64String(options.Value.Key);
        }
        catch (FormatException)
        {
            throw new InvalidOperationException("BlindIndex key must be a base64 string.");
        }

        if (_key.Length < 32)
            throw new InvalidOperationException("BlindIndex key must be at least 256 bits (32 bytes).");
    }

    public string Compute(string purpose, string normalizedValue)
    {
        ArgumentException.ThrowIfNullOrEmpty(purpose);
        ArgumentNullException.ThrowIfNull(normalizedValue);

        // Length-prefix the purpose so ("ab","c") and ("a","bc") can never collide.
        byte[] message = Encoding.UTF8.GetBytes($"{purpose.Length}:{purpose}:{normalizedValue}");
        return Convert.ToHexString(HMACSHA256.HashData(_key, message)).ToLowerInvariant();
    }
}
