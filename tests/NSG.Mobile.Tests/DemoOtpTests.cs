using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSG.Mobile.Application.Common;
using NSG.Mobile.Application.Interfaces.Infrastructure;
using NSG.Mobile.Application.Interfaces.Services;
using NSG.Mobile.Application.Interfaces.Repositories;
using NSG.Mobile.Domain.Common;
using NSG.Mobile.Domain.Entities;
using NSG.Mobile.Domain.Enums;
using NSG.Mobile.Infrastructure.External;
using NSG.Mobile.Infrastructure.Options;
using NSG.Mobile.Infrastructure.Security;

namespace NSG.Mobile.Tests;

/// <summary>
/// Unit tests (no database) for the OTP mode: the fixed demo OTP, its fail-closed guard, and the no-fallback rule of the SMS mode.
/// They use an in-memory OTP repository, so they say nothing about MySQL behaviour.
/// </summary>
public class DemoOtpTests
{
    private const string Mobile = "+919876543210";

    private sealed class Env : IHostEnvironment
    {
        public Env(string name) => EnvironmentName = name;
        public string EnvironmentName { get; set; }
        public string ApplicationName { get; set; } = "test";
        public string ContentRootPath { get; set; } = ".";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private sealed class CountingSms : INicSmsService
    {
        public int Calls { get; private set; }
        public bool Succeeds { get; set; } = true;
        public string? LastCode { get; private set; }
        public Task<SmsSendResult> SendOtpAsync(string mobileNumber, string otpCode, string? templateId = null, CancellationToken cancellationToken = default)
        {
            Calls++; LastCode = otpCode;
            return Task.FromResult(new SmsSendResult(Succeeds, ErrorMessage: Succeeds ? null : "down"));
        }
    }

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<string> Lines { get; } = new();
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => Lines.Add(formatter(state, exception));
    }

    private sealed class InMemoryOtpRepository : IOtpRequestRepository
    {
        public List<OtpRequest> Requests { get; } = new();
        public Task<OtpRequest?> GetByReferenceIdAsync(Guid referenceId, CancellationToken cancellationToken = default) => Task.FromResult(Requests.FirstOrDefault(o => o.ReferenceId == referenceId));
        public Task<OtpRequest?> GetLatestByMobileAndPurposeAsync(string normalizedMobileNumber, OtpPurpose purpose, CancellationToken cancellationToken = default)
            => Task.FromResult(Requests.Where(o => o.MobileNumber == normalizedMobileNumber && o.Purpose == purpose).OrderByDescending(o => o.CreatedAt).FirstOrDefault());
        public Task<int> CountSinceAsync(string normalizedMobileNumber, OtpPurpose purpose, DateTime sinceIst, CancellationToken cancellationToken = default) => Task.FromResult(0);
        public Task AddAsync(OtpRequest otpRequest, CancellationToken cancellationToken = default) { Requests.Add(otpRequest); return Task.CompletedTask; }
        public Task UpdateAsync(OtpRequest otpRequest, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private static (OtpService Service, InMemoryOtpRepository Store, CountingSms Sms, CapturingLogger<OtpService> Log) Build(
        OtpMode mode, string environment = "Development", bool sms = true)
    {
        InMemoryOtpRepository store = new();
        CountingSms smsService = new() { Succeeds = sms };
        CapturingLogger<OtpService> log = new();
        OtpService service = new(store, smsService, Options.Create(new SecurityOptions()), Options.Create(new OtpOptions { Mode = mode }), new Env(environment), log);
        return (service, store, smsService, log);
    }

    // ───── demo mode ─────

    [Fact]
    public async Task Demo_mode_issues_123456_without_sending_an_sms_and_says_so()
    {
        var (service, store, sms, _) = Build(OtpMode.Demo);
        OtpGenerationResult sent = await service.GenerateAndSendAsync(Mobile, OtpPurpose.Registration);

        Assert.True(sent.Success);
        Assert.True(sent.IsDemo);
        Assert.Equal(0, sms.Calls);
        Assert.True((await service.VerifyAsync(sent.ReferenceId!.Value, "123456", OtpPurpose.Registration)).Success);
        Assert.Single(store.Requests);
    }

    [Fact]
    public async Task Demo_mode_keeps_attempt_limit_single_use_purpose_and_expiry()
    {
        var (service, store, _, _) = Build(OtpMode.Demo);
        SecurityOptions defaults = new();

        // Attempt limit: after the maximum wrong guesses even the right code is refused.
        OtpGenerationResult first = await service.GenerateAndSendAsync(Mobile, OtpPurpose.Registration);
        for (int i = 0; i < defaults.OtpMaxAttempts; i++)
            Assert.False((await service.VerifyAsync(first.ReferenceId!.Value, "000000", OtpPurpose.Registration)).Success);
        Assert.Equal(ApiErrorCodes.OtpMaxAttemptsExceeded, (await service.VerifyAsync(first.ReferenceId!.Value, "123456", OtpPurpose.Registration)).ErrorCode);

        // Single use + purpose isolation.
        store.Requests[0].CreatedAt = IndianStandardTime.Now.AddSeconds(-(defaults.OtpResendCooldownSeconds + 1));   // step past the resend cooldown
        OtpGenerationResult second = await service.GenerateAndSendAsync(Mobile, OtpPurpose.Registration);
        Assert.False((await service.VerifyAsync(second.ReferenceId!.Value, "123456", OtpPurpose.PasswordReset)).Success);
        Assert.True((await service.VerifyAsync(second.ReferenceId.Value, "123456", OtpPurpose.Registration)).Success);
        Assert.False((await service.VerifyAsync(second.ReferenceId.Value, "123456", OtpPurpose.Registration)).Success);

        Assert.Equal(2, store.Requests.Count);
    }

    [Fact]
    public async Task Demo_mode_keeps_the_resend_cooldown()
    {
        var (service, _, _, _) = Build(OtpMode.Demo);
        Assert.True((await service.GenerateAndSendAsync(Mobile, OtpPurpose.Registration)).Success);
        OtpGenerationResult again = await service.GenerateAndSendAsync(Mobile, OtpPurpose.Registration);
        Assert.Equal(ApiErrorCodes.OtpResendCooldown, again.ErrorCode);
    }

    [Fact]
    public async Task Demo_mode_never_logs_the_otp()
    {
        var (service, _, _, log) = Build(OtpMode.Demo);
        OtpGenerationResult sent = await service.GenerateAndSendAsync(Mobile, OtpPurpose.Registration);
        await service.VerifyAsync(sent.ReferenceId!.Value, "123456", OtpPurpose.Registration);
        Assert.NotEmpty(log.Lines);
        Assert.All(log.Lines, l => Assert.DoesNotContain("123456", l));
        Assert.Contains(log.Lines, l => l.Contains("DEMO OTP MODE"));
    }

    // ───── SMS mode: real delivery or failure, never the demo code ─────

    [Fact]
    public async Task Sms_mode_sends_a_random_code_through_the_gateway_and_does_not_accept_123456()
    {
        var (service, _, sms, _) = Build(OtpMode.Sms);
        OtpGenerationResult sent = await service.GenerateAndSendAsync(Mobile, OtpPurpose.Registration);

        Assert.True(sent.Success);
        Assert.False(sent.IsDemo);
        Assert.Equal(1, sms.Calls);
        if (sms.LastCode != "123456")   // 1-in-a-million chance the random code happens to equal it
            Assert.False((await service.VerifyAsync(sent.ReferenceId!.Value, "123456", OtpPurpose.Registration)).Success);
        Assert.True((await service.VerifyAsync(sent.ReferenceId!.Value, sms.LastCode!, OtpPurpose.Registration)).Success);
    }

    [Fact]
    public async Task Sms_mode_gateway_failure_fails_the_send_with_no_fallback_to_demo()
    {
        var (service, _, sms, _) = Build(OtpMode.Sms, sms: false);
        OtpGenerationResult sent = await service.GenerateAndSendAsync(Mobile, OtpPurpose.Registration);

        Assert.False(sent.Success);
        Assert.False(sent.IsDemo);
        Assert.Null(sent.ReferenceId);
        Assert.Equal(ApiErrorCodes.OtpSendFailed, sent.ErrorCode);
        Assert.Equal(1, sms.Calls);
    }

    [Fact]
    public async Task Placeholder_gateway_fails_closed_and_does_not_log_the_otp()
    {
        CapturingLogger<NicSmsServicePlaceholder> log = new();
        SmsSendResult result = await new NicSmsServicePlaceholder(log).SendOtpAsync(Mobile, "654321");
        Assert.False(result.Success);
        Assert.NotEmpty(log.Lines);
        Assert.All(log.Lines, l => { Assert.DoesNotContain("654321", l); Assert.DoesNotContain("9876543210", l); });
    }

    [Fact]
    public void Default_mode_is_sms()
    {
        Assert.Equal(OtpMode.Sms, new OtpOptions().Mode);
        Assert.Equal(OtpMode.Sms, new ConfigurationBuilder().Build().GetSection("Otp").Get<OtpOptions>()?.Mode ?? OtpMode.Sms);
    }

    // ───── fail-closed guard ─────

    [Theory]
    [InlineData("Production", false)]
    [InlineData("production", true)]
    [InlineData("", false)]
    [InlineData(null, false)]
    [InlineData("Staging", false)]
    [InlineData("Development", false)]
    [InlineData("Demo", true)]
    public void Demo_mode_is_allowed_in_every_environment_by_owner_decision(string? environment, bool allowFlag)
        => OtpModeGuard.EnsureAllowed(new OtpOptions { Mode = OtpMode.Demo, AllowDemoOutsideDevelopment = allowFlag }, environment);

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    [InlineData("Development")]
    [InlineData(null)]
    public void Sms_mode_is_always_allowed(string? environment)
        => OtpModeGuard.EnsureAllowed(new OtpOptions { Mode = OtpMode.Sms }, environment);

    [Fact]
    public void The_service_can_be_built_in_production_in_demo_mode()
    {
        Assert.NotNull(Build(OtpMode.Demo, "Production").Service);
    }

    [Fact]
    public void An_unrecognised_mode_in_configuration_stops_startup_instead_of_defaulting()
    {
        IConfiguration config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Otp:Mode"] = "Fixed" }).Build();
        Assert.ThrowsAny<Exception>(() => config.GetSection("Otp").Get<OtpOptions>());
    }

    [Fact]
    public void Committed_configuration_enables_demo_in_base_and_development_files()
    {
        string root = FindRoot();
        string baseJson = File.ReadAllText(Path.Combine(root, "src", "NSG.Mobile.API", "appsettings.json"));
        string devJson = File.ReadAllText(Path.Combine(root, "src", "NSG.Mobile.API", "appsettings.Development.json"));
        Assert.Contains("\"Mode\": \"Demo\"", baseJson);
        Assert.Contains("\"Mode\": \"Demo\"", devJson);
        Assert.False(File.Exists(Path.Combine(root, "src", "NSG.Mobile.API", "appsettings.Production.json")));
    }

    private static string FindRoot()
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "NSG.Mobile.API.sln"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Repository root (NSG.Mobile.API.sln) not found.");
    }
}
