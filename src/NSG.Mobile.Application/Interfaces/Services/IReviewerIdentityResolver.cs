namespace NSG.Mobile.Application.Interfaces.Services;

/// <summary>The admin who is reviewing a mobile approval request. <see cref="DisplayName"/> is null when the token carries no name.</summary>
public sealed record ReviewerIdentity(int UserId, string? DisplayName);

/// <summary>
/// Resolves who is reviewing an approval request, for the reviewer column and the audit trail. The identity comes from the
/// authenticated user only (never from the request body). Returns null when no reviewer can be identified.
/// </summary>
public interface IReviewerIdentityResolver
{
    /// <summary>The reviewer for the current request, or null when none can be identified.</summary>
    ReviewerIdentity? Resolve();
}
