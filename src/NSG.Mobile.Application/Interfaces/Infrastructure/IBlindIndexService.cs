namespace NSG.Mobile.Application.Interfaces.Infrastructure;

/// <summary>
/// Keyed, deterministic hash ("blind index") of a sensitive value, used only to enforce uniqueness and exact-match
/// lookup on fields whose stored form is randomised ciphertext (PAN, salary account number).
/// The hash is not reversible without the key, and the key must come from secure configuration — never source code.
/// </summary>
public interface IBlindIndexService
{
    /// <summary>
    /// Lower-case hex HMAC-SHA256 (64 characters) of <paramref name="normalizedValue"/>, domain-separated by
    /// <paramref name="purpose"/> so the same value in two different fields never produces the same hash.
    /// The caller must pass the already-normalised value (for example an upper-case PAN).
    /// </summary>
    string Compute(string purpose, string normalizedValue);
}
