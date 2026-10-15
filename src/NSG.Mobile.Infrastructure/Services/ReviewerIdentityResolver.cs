using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using NSG.Mobile.Application.Interfaces.Services;

namespace NSG.Mobile.Infrastructure.Services;

/// <summary>
/// TEMPORARY, Development-only switch for the reviewer fallback below. Never bound from configuration: Program.cs turns it on only
/// inside the Development-only authorization-bypass block, so Staging and Production cannot enable it.
/// REMOVE this class, the fallback in <see cref="ReviewerIdentityResolver"/>, and the Configure call in Program.cs once real
/// IT Admin authentication exists (search for "TEMP-DEV-REVIEWER").
/// </summary>
public sealed class DevelopmentReviewerOptions
{
    public bool Enabled { get; set; }
}

/// <summary>
/// The reviewer is the authenticated admin (token user id). TEMP-DEV-REVIEWER: when no user id is present, the environment is
/// Development and <see cref="DevelopmentReviewerOptions.Enabled"/> is set, the reviewer is the fixed test identity
/// <see cref="DevelopmentReviewerName"/> instead. A real token always wins; nothing from the request is ever read.
/// </summary>
public sealed class ReviewerIdentityResolver : IReviewerIdentityResolver
{
    /// <summary>TEMP-DEV-REVIEWER: fixed local test reviewer name (written to the audit trail).</summary>
    public const string DevelopmentReviewerName = "AsmarTest";

    /// <summary>TEMP-DEV-REVIEWER: stand-in value for the numeric reviewer column; real user ids are positive.</summary>
    public const int DevelopmentReviewerUserId = -1;

    private readonly ICurrentUserService _currentUser;
    private readonly IHostEnvironment _environment;
    private readonly DevelopmentReviewerOptions _developmentOptions;

    public ReviewerIdentityResolver(ICurrentUserService currentUser, IHostEnvironment environment, IOptions<DevelopmentReviewerOptions> developmentOptions)
    {
        _currentUser = currentUser;
        _environment = environment;
        _developmentOptions = developmentOptions.Value;
    }

    public ReviewerIdentity? Resolve()
    {
        if (_currentUser.UserId is int userId)
            return new ReviewerIdentity(userId, null);

        // TEMP-DEV-REVIEWER: strictly Development (checked here and again at startup in Program.cs).
        if (_developmentOptions.Enabled && _environment.IsDevelopment())
            return new ReviewerIdentity(DevelopmentReviewerUserId, DevelopmentReviewerName);

        return null;
    }
}
