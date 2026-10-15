using Microsoft.AspNetCore.Authorization;

namespace NSG.Mobile.API.Security;

/// <summary>
/// TEMPORARY, Development-only: satisfies every authorization requirement so endpoints can be called without a token
/// while the token/login flow is being built. Registered only when <c>Security:DisableAuthorization</c> is true, and
/// <c>Program.cs</c> refuses to start if that setting is true in any environment other than Development.
/// The [Authorize] attributes and policies stay in place; turning the setting off restores full enforcement.
/// A caller that does present a valid token is still authenticated normally.
/// </summary>
public sealed class DevelopmentAuthorizationBypassHandler : IAuthorizationHandler
{
    public const string SettingName = "Security:DisableAuthorization";

    public Task HandleAsync(AuthorizationHandlerContext context)
    {
        foreach (IAuthorizationRequirement requirement in context.PendingRequirements.ToList())
            context.Succeed(requirement);

        return Task.CompletedTask;
    }
}
