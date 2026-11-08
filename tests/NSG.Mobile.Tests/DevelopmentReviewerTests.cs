using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using NSG.Mobile.Application.MobileAuth.DTOs;
using NSG.Mobile.Application.MobileAuth.Services;
using NSG.Mobile.Domain.Enums;
using NSG.Mobile.Infrastructure.Services;

namespace NSG.Mobile.Tests;

/// <summary>
/// TEMP-DEV-REVIEWER: the "AsmarTest" reviewer fallback works only in Development with the switch on, never overrides a real
/// token identity, and is recorded for approval and rejection. Delete with the fallback once real IT Admin login exists.
/// </summary>
public class DevelopmentReviewerTests
{
    private sealed class Env : IHostEnvironment
    {
        public Env(string name) => EnvironmentName = name;
        public string EnvironmentName { get; set; }
        public string ApplicationName { get; set; } = "test";
        public string ContentRootPath { get; set; } = ".";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private static ReviewerIdentityResolver Resolver(string environment, bool enabled, int? tokenUserId = null)
        => new(new FakeCurrentUser { UserId = tokenUserId }, new Env(environment),
               Options.Create(new DevelopmentReviewerOptions { Enabled = enabled }));

    [Fact]
    public void Development_with_the_switch_on_and_no_token_user_uses_AsmarTest()
    {
        var reviewer = Resolver(Environments.Development, true).Resolve();
        Assert.Equal("AsmarTest", reviewer!.DisplayName);
    }

    [Theory]
    [InlineData("Staging")]
    [InlineData("Production")]
    public void Other_environments_never_accept_the_fallback_even_if_the_switch_is_on(string environment)
        => Assert.Null(Resolver(environment, true).Resolve());

    [Fact]
    public void Development_with_the_switch_off_has_no_fallback()
        => Assert.Null(Resolver(Environments.Development, false).Resolve());

    [Fact]
    public void A_real_token_identity_always_wins()
    {
        var reviewer = Resolver(Environments.Development, true, tokenUserId: 9001).Resolve();
        Assert.Equal(9001, reviewer!.UserId);
        Assert.Null(reviewer.DisplayName);
    }

}

internal sealed class TestEnv : IHostEnvironment
{
    public TestEnv(string name) => EnvironmentName = name;
    public string EnvironmentName { get; set; }
    public string ApplicationName { get; set; } = "test";
    public string ContentRootPath { get; set; } = ".";
    public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
}
