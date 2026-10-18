namespace NSG.Mobile.Application.Admin;

/// <summary>Complexity rules of an Admin Portal password. Longer and stricter than the MPIN: an admin can reach a lot of personnel data.</summary>
public static class AdminPasswordPolicy
{
    public const int MinLength = 12;
    public const int MaxLength = 128;

    /// <summary>Why <paramref name="password"/> is unacceptable, or null when it is fine.</summary>
    public static string? Violation(string password, string nsgNumber)
    {
        if (string.IsNullOrEmpty(password)) return "Password is required.";
        if (password.Length < MinLength) return $"Password must be at least {MinLength} characters.";
        if (password.Length > MaxLength) return $"Password must not exceed {MaxLength} characters.";
        if (!password.Any(char.IsUpper) || !password.Any(char.IsLower) || !password.Any(char.IsDigit) || password.All(char.IsLetterOrDigit))
            return "Password must contain an upper-case letter, a lower-case letter, a digit and a symbol.";
        if (!string.IsNullOrWhiteSpace(nsgNumber) && password.Contains(nsgNumber.Trim(), StringComparison.OrdinalIgnoreCase))
            return "Password must not contain your NSG number.";
        return null;
    }
}
