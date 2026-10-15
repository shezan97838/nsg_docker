namespace NSG.Mobile.Application.Authentication.Interfaces;

/// <summary>
/// Password hashing abstraction.
/// Infrastructure provides the implementation using a secure adaptive algorithm.
/// </summary>
public interface IPasswordHashingService
{
    /// <summary>Hash a password using a secure adaptive algorithm.</summary>
    string HashPassword(string password);

    /// <summary>Verify a password against a stored hash.</summary>
    bool VerifyPassword(string password, string passwordHash);
}
