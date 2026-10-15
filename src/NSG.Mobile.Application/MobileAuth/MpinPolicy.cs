namespace NSG.Mobile.Application.MobileAuth;

/// <summary>
/// MPIN rules (ADR-014: 4-6 digits). A 4-6 digit secret has a tiny key space, so the real protection is the slow salted hash,
/// the attempt limit and the lockout; this policy only removes the most guessable values.
/// </summary>
public static class MpinPolicy
{
    public const int MinLength = 4;
    public const int MaxLength = 6;
    public const string Pattern = @"^[0-9]{4,6}$";

    /// <summary>True for an MPIN made of one repeated digit (1111) or a straight run (1234, 4321).</summary>
    public static bool IsTrivial(string mpin)
    {
        if (string.IsNullOrEmpty(mpin) || mpin.Length < 2) return true;
        bool same = true, up = true, down = true;
        for (int i = 1; i < mpin.Length; i++)
        {
            int diff = mpin[i] - mpin[i - 1];
            same &= diff == 0;
            up &= diff == 1;
            down &= diff == -1;
        }
        return same || up || down;
    }
}
