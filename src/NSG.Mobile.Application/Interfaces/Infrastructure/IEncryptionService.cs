namespace NSG.Mobile.Application.Interfaces.Infrastructure;

/// <summary>
/// Encryption service abstraction.
/// SOW §3.8: "AES-256 encryption for data at rest."
/// Used for application-level field encryption of highly sensitive data.
/// Keys must come from secure configuration — never source code.
/// </summary>
public interface IEncryptionService
{
    string Encrypt(string plainText);
    string Decrypt(string cipherText);
}
