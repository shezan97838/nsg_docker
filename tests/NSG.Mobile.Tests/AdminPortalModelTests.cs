using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using NSG.Mobile.Application.Admin;
using NSG.Mobile.Domain.Constants;

namespace NSG.Mobile.Tests;

/// <summary>Checks that need no database: the password policy and the authorization attribute on every admin endpoint.</summary>
public class AdminPortalModelTests
{
    [Theory]
    [InlineData("Str0ng!Passw0rd#1", "ADM-1", null)]
    [InlineData("short1!A", "ADM-1", "at least 12")]
    [InlineData("alllowercase1!x", "ADM-1", "upper-case")]
    [InlineData("ALLUPPERCASE1!X", "ADM-1", "upper-case")]
    [InlineData("NoDigitsHere!!Aa", "ADM-1", "digit")]
    [InlineData("NoSymbolsHere12Aa", "ADM-1", "symbol")]
    [InlineData("My-adm-1-Passw0rd!", "ADM-1", "NSG number")]
    public void Password_policy_requires_length_complexity_and_no_nsg_number(string password, string nsg, string? expectedFragment)
    {
        string? violation = AdminPasswordPolicy.Violation(password, nsg);

        if (expectedFragment == null) Assert.Null(violation);
        else Assert.Contains(expectedFragment, violation);
    }

    [Fact]
    public void Every_admin_endpoint_is_explicitly_anonymous_or_behind_an_admin_policy()
    {
        Type[] controllers = typeof(Program).Assembly.GetTypes()
            .Where(t => t.IsSubclassOf(typeof(ControllerBase)) && t.Name.StartsWith("Admin", StringComparison.Ordinal)).ToArray();
        Assert.Equal(3, controllers.Length);

        HashSet<string> adminPolicies = new()
        {
            DomainConstants.Policies.RequireAdminAccount, DomainConstants.Policies.RequireSuperAdmin, DomainConstants.Policies.RequireRegistrationAdmin,
            DomainConstants.Policies.RequirePisAdmin, DomainConstants.Policies.RequireGrievanceAdmin, DomainConstants.Policies.RequireMediaCellAdmin,
            DomainConstants.Policies.RequireSalaryManagementAdmin, DomainConstants.Policies.RequireLeaveAdmin
        };

        List<string> problems = new();
        foreach (Type controller in controllers)
        {
            string? classPolicy = controller.GetCustomAttribute<AuthorizeAttribute>()?.Policy;
            foreach (MethodInfo action in controller.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                         .Where(m => m.GetCustomAttributes<HttpMethodAttribute>().Any()))
            {
                bool anonymous = action.GetCustomAttribute<AllowAnonymousAttribute>() != null;
                string? policy = action.GetCustomAttribute<AuthorizeAttribute>()?.Policy ?? classPolicy;
                if (anonymous) continue;
                if (policy == null || !adminPolicies.Contains(policy))
                    problems.Add($"{controller.Name}.{action.Name} has no admin policy");
            }
        }

        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
    }

    [Fact]
    public void Only_the_public_sign_in_and_activation_steps_are_anonymous()
    {
        string[] anonymous = typeof(Program).Assembly.GetTypes()
            .Where(t => t.IsSubclassOf(typeof(ControllerBase)) && t.Name.StartsWith("Admin", StringComparison.Ordinal))
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Where(m => m.GetCustomAttribute<AllowAnonymousAttribute>() != null).Select(m => $"{t.Name}.{m.Name}"))
            .OrderBy(n => n).ToArray();

        Assert.Equal(new[]
        {
            "AdminActivationController.Complete", "AdminActivationController.SendOtp",
            "AdminAuthController.Login", "AdminAuthController.Logout", "AdminAuthController.Refresh", "AdminAuthController.VerifyLogin"
        }, anonymous);
    }
}
