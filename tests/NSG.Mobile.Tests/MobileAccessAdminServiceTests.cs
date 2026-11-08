using System.Text.Json;
using Microsoft.Extensions.Logging;
using NSG.Mobile.Application.Common;
using NSG.Mobile.Application.Interfaces.Repositories;
using NSG.Mobile.Application.Interfaces.Services;
using NSG.Mobile.Application.MobileAccess;
using NSG.Mobile.Application.MobileAuth.DTOs;
using NSG.Mobile.Domain.Common;
using NSG.Mobile.Domain.Entities;
using NSG.Mobile.Domain.Enums;
using NSG.Mobile.Infrastructure.Services;

namespace NSG.Mobile.Tests;

/// <summary>In-memory <see cref="IMobileAccessRepository"/> with the same conditional rules as the SQL implementation.</summary>
internal sealed class FakeMobileAccessRepository : IMobileAccessRepository
{
    private readonly FakeProfileRepository _profiles;
    private readonly FakeSessionRepository _sessions;

    public FakeMobileAccessRepository(FakeProfileRepository profiles, FakeSessionRepository sessions)
    {
        _profiles = profiles;
        _sessions = sessions;
    }

    public Task<MobileAccessChangeOutcome> BlockAsync(long profileId, int adminId, string? reason, DateTime nowIst, CancellationToken cancellationToken = default)
    {
        MobileUserProfile? p = _profiles.Profiles.FirstOrDefault(x => x.Id == profileId);
        if (p == null) return Task.FromResult(MobileAccessChangeOutcome.NotFound);

        MobileAccessChangeOutcome outcome;
        if (p.IsActive && !p.IsAdminBlocked)
        {
            p.IsActive = false; p.AccessBlockedAtIst = nowIst; p.AccessBlockedBy = adminId; p.AccessBlockReason = reason; p.UpdatedAtIst = nowIst;
            outcome = MobileAccessChangeOutcome.Changed;
        }
        else if (p.IsAdminBlocked) outcome = MobileAccessChangeOutcome.NoChange;
        else return Task.FromResult(MobileAccessChangeOutcome.Refused);

        _sessions.RevokeAllForProfileAsync(profileId, MobileSessionRevokeReasons.AdminBlocked, nowIst).GetAwaiter().GetResult();
        return Task.FromResult(outcome);
    }

    public Task<MobileAccessChangeOutcome> RestoreAsync(long profileId, int adminId, DateTime nowIst, CancellationToken cancellationToken = default)
    {
        MobileUserProfile? p = _profiles.Profiles.FirstOrDefault(x => x.Id == profileId);
        if (p == null) return Task.FromResult(MobileAccessChangeOutcome.NotFound);
        if (p.IsAdminBlocked)
        {
            p.IsActive = true; p.AccessBlockedAtIst = null; p.AccessBlockedBy = null; p.AccessBlockReason = null; p.UpdatedAtIst = nowIst;
            return Task.FromResult(MobileAccessChangeOutcome.Changed);   // refresh tokens are deliberately not touched
        }
        return Task.FromResult(p.IsActive ? MobileAccessChangeOutcome.NoChange : MobileAccessChangeOutcome.Refused);
    }

    public Task<bool> HasLiveSessionAsync(long profileId, DateTime nowIst, CancellationToken cancellationToken = default)
        => Task.FromResult(_sessions.Tokens.Any(t => t.ProfileId == profileId && t.RevokedAtIst == null && t.ExpiresAtIst > nowIst));
}

internal sealed class ThrowingAudit : IAuditService
{
    public int Attempts { get; private set; }

    public Task LogAsync(AuditAction action, string result, int? userId = null, string? entityType = null, string? entityId = null,
        string? ipAddress = null, int? deviceId = null, string? correlationId = null, string? additionalData = null, CancellationToken cancellationToken = default)
    {
        Attempts++;
        throw new InvalidOperationException("audit store unavailable");
    }
}

internal sealed class CapturingLogger<T> : ILogger<T>
{
    public List<(LogLevel Level, string Message, Exception? Exception)> Entries { get; } = new();
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    public bool IsEnabled(LogLevel logLevel) => true;
    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        => Entries.Add((logLevel, formatter(state, exception), exception));
}

/// <summary>A wired-up <see cref="MobileAccessAdminService"/> over the same fakes as <see cref="MobileAuthHarness"/>.</summary>
internal sealed class MobileAccessHarness
{
    public const int AdminId = 77;

    public MobileAuthHarness Auth { get; } = new(pisStatus: MobileAppActivationStatus.Approved);
    public FakeMobileAccessRepository Access { get; }
    public CapturingLogger<MobileAccessAdminService> Logger { get; } = new();
    public MobileAccessAdminService Service { get; }
    public MobileUserProfile Profile { get; }

    public MobileAccessHarness(IAuditService? audit = null, bool withProfile = true)
    {
        Auth.CurrentUser.AdminProfileId = AdminId;
        Auth.Pis.Rows[0].FullName = "Test Person";
        Auth.Pis.Rows[0].NsgRank = "Inspector";
        Auth.Pis.Rows[0].NsgUnit = "Unit One";
        Access = new FakeMobileAccessRepository(Auth.Profiles, Auth.Sessions);
        Service = new MobileAccessAdminService(Auth.Profiles, Auth.Pis, Access, audit ?? Auth.Audit, Auth.CurrentUser, Auth.Clock, Logger);
        Profile = withProfile ? Auth.AddProfile() : new MobileUserProfile();
    }

    public async Task<MobileLoginResponseDto> LoginAsync()
        => (await Auth.Service.LoginAsync(new MobileLoginRequestDto
        { NsgNumber = MobileAuthHarness.Nsg, MobileNumber = MobileAuthHarness.Mobile, DateOfBirth = MobileAuthHarness.Dob, Mpin = MobileAuthHarness.Mpin }, "127.0.0.1")).Data!;
}

/// <summary>Unit tests (no database) of the PIS Admin lookup, block and restore of mobile access.</summary>
public class MobileAccessAdminServiceTests
{
    private const string Nsg = MobileAuthHarness.Nsg;

    private static MobileAccessStatusDto Data(Result<ApiResult<MobileAccessStatusDto>> result)
    {
        Assert.True(result.IsSuccess, result.ErrorMessage);
        return result.Data!.Data!;
    }

    // ───── lookup ─────

    [Fact]
    public async Task Lookup_of_an_active_person_returns_identity_status_and_session_but_nothing_sensitive()
    {
        MobileAccessHarness h = new();
        await h.LoginAsync();

        var result = await h.Service.GetAsync("  " + Nsg + " ");
        MobileAccessStatusDto dto = Data(result);

        Assert.Equal(MobileAccessMessages.Retrieved, result.Data!.Message);
        Assert.Equal(Nsg, dto.NsgNumber);
        Assert.Equal("Test Person", dto.FullName);
        Assert.Equal("Inspector", dto.Rank);
        Assert.Equal("Unit One", dto.Unit);
        Assert.NotEqual(MobileAuthHarness.Mobile, dto.MaskedMobileNumber);
        Assert.EndsWith(MobileAuthHarness.Mobile[^4..], dto.MaskedMobileNumber);
        Assert.True(dto.PisRecordExists);
        Assert.True(dto.MobileProfileExists);
        Assert.Equal(MobileAccessStatuses.Active, dto.Status);
        Assert.True(dto.IsActive);
        Assert.True(dto.HasActiveSession);
        Assert.False(dto.IsLockedOut);
        Assert.NotNull(dto.LastLoginAt);
        Assert.Equal(TimeSpan.FromHours(5.5), dto.LastLoginAt!.Value.Offset);
        Assert.Null(dto.AccessBlockedAt);

        // The payload is exactly the allowed shape: no hash, date of birth, full mobile number or sensitive PIS identifier.
        string json = JsonSerializer.Serialize(dto, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        foreach (string forbidden in new[] { "mpin", "hash", "dateOfBirth", "aadhaar", "pan", "pran", "salary", MobileAuthHarness.Mobile })
            Assert.DoesNotContain(forbidden, json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Lookup_of_a_person_in_pis_without_a_mobile_profile_is_distinct()
    {
        MobileAccessHarness h = new(withProfile: false);

        var result = await h.Service.GetAsync(Nsg);
        MobileAccessStatusDto dto = Data(result);

        Assert.Equal(MobileAccessMessages.RetrievedNoProfile, result.Data!.Message);
        Assert.True(dto.PisRecordExists);
        Assert.False(dto.MobileProfileExists);
        Assert.Equal(MobileAccessStatuses.NoMobileProfile, dto.Status);
        Assert.Equal("Test Person", dto.FullName);
        Assert.Null(dto.IsActive);
        Assert.Null(dto.HasActiveSession);
    }

    [Fact]
    public async Task Lookup_of_an_unknown_nsg_number_is_not_found_with_a_message()
    {
        MobileAccessHarness h = new();
        var result = await h.Service.GetAsync("NOPE-1");
        Assert.Equal(ApiErrorCodes.NotFound, result.ErrorCode);
        Assert.Equal(MobileAccessMessages.NothingFound, result.ErrorMessage);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task A_blank_nsg_number_is_a_validation_error_for_every_operation(string nsg)
    {
        MobileAccessHarness h = new();
        foreach (var result in new[] { await h.Service.GetAsync(nsg), await h.Service.BlockAsync(nsg, null, null), await h.Service.RestoreAsync(nsg, null, null) })
        {
            Assert.Equal(ApiErrorCodes.ValidationFailed, result.ErrorCode);
            Assert.Equal(MobileAccessMessages.NsgRequired, result.ErrorMessage);
        }
    }

    [Fact]
    public async Task An_nsg_number_longer_than_the_column_is_a_validation_error()
    {
        MobileAccessHarness h = new();
        var result = await h.Service.GetAsync(new string('N', 31));
        Assert.Equal(ApiErrorCodes.ValidationFailed, result.ErrorCode);
        Assert.Equal(MobileAccessMessages.NsgTooLong, result.ErrorMessage);
    }

    // ───── block ─────

    [Fact]
    public async Task Block_marks_the_profile_revokes_every_session_and_audits_who_when_and_why()
    {
        MobileAccessHarness h = new();
        await h.LoginAsync();
        MobileUserProfile p = h.Profile;
        DateTime now = IndianStandardTime.ConvertFromUtc(h.Auth.Clock.Now.UtcDateTime);

        var result = await h.Service.BlockAsync(Nsg, "  lost phone  ", "10.0.0.5");
        MobileAccessStatusDto dto = Data(result);

        Assert.Equal(MobileAccessMessages.Blocked, result.Data!.Message);
        Assert.False(p.IsActive);
        Assert.Equal(now, p.AccessBlockedAtIst);
        Assert.Equal(MobileAccessHarness.AdminId, p.AccessBlockedBy);
        Assert.Equal("lost phone", p.AccessBlockReason);
        Assert.All(h.Auth.Sessions.Tokens, t => { Assert.NotNull(t.RevokedAtIst); Assert.Equal(MobileSessionRevokeReasons.AdminBlocked, t.RevokeReason); });

        Assert.Equal(MobileAccessStatuses.AdminBlocked, dto.Status);
        Assert.False(dto.IsActive);
        Assert.False(dto.HasActiveSession);
        Assert.Equal(TimeSpan.FromHours(5.5), dto.AccessBlockedAt!.Value.Offset);
        Assert.Equal(MobileAccessHarness.AdminId, dto.AccessBlockedBy);
        Assert.Equal("lost phone", dto.AccessBlockReason);

        var audit = Assert.Single(h.Auth.Audit.Entries, e => e.Action == AuditAction.MobileAccessBlocked);
        Assert.Equal(DomainConstantsResults.Success, audit.Result);
        Assert.Equal(MobileAccessHarness.AdminId, audit.UserId);
        Assert.Equal(p.Id.ToString(), audit.EntityId);
        using JsonDocument data = JsonDocument.Parse(audit.Data!);
        Assert.Equal("Block", data.RootElement.GetProperty("action").GetString());
        Assert.Equal(Nsg, data.RootElement.GetProperty("nsgNumber").GetString());
        Assert.Equal(p.Id, data.RootElement.GetProperty("mobileProfileId").GetInt64());
        Assert.Equal("AdminPortal", data.RootElement.GetProperty("actorSource").GetString());
        Assert.Equal("lost phone", data.RootElement.GetProperty("reason").GetString());
    }

    [Fact]
    public async Task Block_changes_nothing_in_onboarding_status()
    {
        MobileAccessHarness h = new();
        await h.Service.BlockAsync(Nsg, null, null);
        Assert.Equal(MobileAppActivationStatus.Approved, h.Auth.Pis.Rows[0].MobileAppActivationStatus);
    }

    [Fact]
    public async Task A_repeated_block_is_idempotent_keeps_the_first_marker_and_is_not_audited_again()
    {
        MobileAccessHarness h = new();
        await h.Service.BlockAsync(Nsg, "first", null);
        DateTime? firstAt = h.Profile.AccessBlockedAtIst;
        h.Auth.Clock.Advance(TimeSpan.FromHours(1));
        h.Auth.CurrentUser.AdminProfileId = 99;

        var second = await h.Service.BlockAsync(Nsg, "second", null);

        Assert.True(second.IsSuccess);
        Assert.Equal(MobileAccessMessages.AlreadyBlocked, second.Data!.Message);
        Assert.Equal(firstAt, h.Profile.AccessBlockedAtIst);
        Assert.Equal(MobileAccessHarness.AdminId, h.Profile.AccessBlockedBy);
        Assert.Equal("first", h.Profile.AccessBlockReason);
        Assert.Single(h.Auth.Audit.Entries, e => e.Action == AuditAction.MobileAccessBlocked);
    }

    [Fact]
    public async Task Reason_is_optional_blank_means_none_and_at_most_500_characters()
    {
        MobileAccessHarness h = new();
        var blank = await h.Service.BlockAsync(Nsg, "   ", null);
        Assert.True(blank.IsSuccess);
        Assert.Null(h.Profile.AccessBlockReason);
        await h.Service.RestoreAsync(Nsg, null, null);

        var tooLong = await h.Service.BlockAsync(Nsg, new string('x', 501), null);
        Assert.Equal(ApiErrorCodes.ValidationFailed, tooLong.ErrorCode);
        Assert.Equal(MobileAccessMessages.ReasonTooLong, tooLong.ErrorMessage);
        Assert.True(h.Profile.IsActive);                                           // nothing happened

        Assert.True((await h.Service.BlockAsync(Nsg, new string('x', 500), null)).IsSuccess);
        Assert.Equal(500, h.Profile.AccessBlockReason!.Length);
        Assert.Equal(ApiErrorCodes.ValidationFailed, (await h.Service.RestoreAsync(Nsg, new string('x', 501), null)).ErrorCode);
    }

    [Fact]
    public async Task A_profile_that_is_inactive_for_another_reason_cannot_be_blocked_here()
    {
        MobileAccessHarness h = new();
        h.Profile.IsActive = false;

        var result = await h.Service.BlockAsync(Nsg, "x", null);

        Assert.Equal(ApiErrorCodes.Conflict, result.ErrorCode);
        Assert.Equal(MobileAccessMessages.BlockRefused, result.ErrorMessage);
        Assert.Null(h.Profile.AccessBlockedAtIst);
        Assert.DoesNotContain(h.Auth.Audit.Entries, e => e.Action == AuditAction.MobileAccessBlocked);
    }

    [Fact]
    public async Task Block_and_restore_of_a_person_without_a_mobile_profile_are_not_found_with_distinct_messages()
    {
        MobileAccessHarness inPis = new(withProfile: false);
        Assert.Equal(MobileAccessMessages.NoMobileProfile, (await inPis.Service.BlockAsync(Nsg, null, null)).ErrorMessage);
        Assert.Equal(MobileAccessMessages.NoMobileProfile, (await inPis.Service.RestoreAsync(Nsg, null, null)).ErrorMessage);

        MobileAccessHarness nobody = new(withProfile: false);
        nobody.Auth.Pis.Rows.Clear();
        var result = await nobody.Service.BlockAsync(Nsg, null, null);
        Assert.Equal(ApiErrorCodes.NotFound, result.ErrorCode);
        Assert.Equal(MobileAccessMessages.NothingFound, result.ErrorMessage);
    }

    [Fact]
    public async Task Without_an_admin_identity_in_the_token_nothing_is_changed()
    {
        MobileAccessHarness h = new();
        h.Auth.CurrentUser.AdminProfileId = null;
        h.Auth.CurrentUser.UserId = 5;                       // a staff user id is not an Admin Portal identity

        Assert.Equal(ApiErrorCodes.Unauthorized, (await h.Service.BlockAsync(Nsg, null, null)).ErrorCode);
        Assert.Equal(ApiErrorCodes.Unauthorized, (await h.Service.RestoreAsync(Nsg, null, null)).ErrorCode);
        Assert.True(h.Profile.IsActive);
        Assert.Null(h.Profile.AccessBlockedAtIst);
    }

    // ───── restore ─────

    [Fact]
    public async Task Restore_reactivates_a_blocked_profile_clears_the_marker_and_never_revives_a_session()
    {
        MobileAccessHarness h = new();
        MobileLoginResponseDto session = await h.LoginAsync();
        await h.Service.BlockAsync(Nsg, "why", null);

        var result = await h.Service.RestoreAsync(Nsg, "mistake", "10.0.0.5");
        MobileAccessStatusDto dto = Data(result);

        Assert.Equal(MobileAccessMessages.Restored, result.Data!.Message);
        Assert.True(h.Profile.IsActive);
        Assert.Null(h.Profile.AccessBlockedAtIst);
        Assert.Null(h.Profile.AccessBlockedBy);
        Assert.Null(h.Profile.AccessBlockReason);
        Assert.Equal(MobileAccessStatuses.Active, dto.Status);
        Assert.False(dto.HasActiveSession);                                          // the user has to log in again
        Assert.All(h.Auth.Sessions.Tokens, t => Assert.Equal(MobileSessionRevokeReasons.AdminBlocked, t.RevokeReason));
        Assert.DoesNotContain(h.Auth.Sessions.Tokens, t => t.RevokedAtIst == null);

        // The old refresh token stays dead; a fresh login works.
        Assert.False((await h.Auth.Service.RefreshAsync(new MobileRefreshRequestDto { RefreshToken = session.RefreshToken }, "127.0.0.1")).IsSuccess);
        Assert.True((await h.LoginAsyncResult()).IsSuccess);

        var audit = Assert.Single(h.Auth.Audit.Entries, e => e.Action == AuditAction.MobileAccessRestored);
        Assert.Equal(MobileAccessHarness.AdminId, audit.UserId);
        using JsonDocument data = JsonDocument.Parse(audit.Data!);
        Assert.Equal("Restore", data.RootElement.GetProperty("action").GetString());
        Assert.Equal("mistake", data.RootElement.GetProperty("reason").GetString());
    }

    [Fact]
    public async Task A_repeated_restore_is_idempotent_and_not_audited_again()
    {
        MobileAccessHarness h = new();
        await h.Service.BlockAsync(Nsg, null, null);
        await h.Service.RestoreAsync(Nsg, null, null);

        var again = await h.Service.RestoreAsync(Nsg, null, null);

        Assert.True(again.IsSuccess);
        Assert.Equal(MobileAccessMessages.AlreadyActive, again.Data!.Message);
        Assert.Single(h.Auth.Audit.Entries, e => e.Action == AuditAction.MobileAccessRestored);
    }

    [Fact]
    public async Task Restore_never_reactivates_a_profile_that_was_deactivated_for_an_unrelated_reason()
    {
        MobileAccessHarness h = new();
        h.Profile.IsActive = false;

        var result = await h.Service.RestoreAsync(Nsg, null, null);

        Assert.Equal(ApiErrorCodes.Conflict, result.ErrorCode);
        Assert.Equal(MobileAccessMessages.RestoreRefused, result.ErrorMessage);
        Assert.False(h.Profile.IsActive);
        Assert.DoesNotContain(h.Auth.Audit.Entries, e => e.Action == AuditAction.MobileAccessRestored);
    }

    [Fact]
    public async Task Restore_leaves_an_unrelated_lockout_alone()
    {
        MobileAccessHarness h = new();
        h.Profile.LockedUntilIst = IndianStandardTime.ConvertFromUtc(h.Auth.Clock.Now.UtcDateTime).AddMinutes(20);
        h.Profile.FailedLoginAttempts = 3;
        await h.Service.BlockAsync(Nsg, null, null);

        MobileAccessStatusDto dto = Data(await h.Service.RestoreAsync(Nsg, null, null));

        Assert.True(dto.IsLockedOut);
        Assert.Equal(3, h.Profile.FailedLoginAttempts);
    }

    // ───── audit failure ─────

    [Fact]
    public async Task An_audit_failure_neither_undoes_nor_hides_a_committed_block_or_restore()
    {
        ThrowingAudit audit = new();
        MobileAccessHarness h = new(audit);

        var block = await h.Service.BlockAsync(Nsg, "r", null);
        Assert.True(block.IsSuccess);
        Assert.Equal(MobileAccessMessages.Blocked, block.Data!.Message);
        Assert.True(h.Profile.IsAdminBlocked);
        Assert.False(h.Profile.IsActive);

        var restore = await h.Service.RestoreAsync(Nsg, null, null);
        Assert.True(restore.IsSuccess);
        Assert.True(h.Profile.IsActive);

        Assert.Equal(2, audit.Attempts);
        // Never silent: each failure is in the structured log, with what is needed to find the entry again.
        var errors = h.Logger.Entries.Where(e => e.Level == LogLevel.Error).ToList();
        Assert.Equal(2, errors.Count);
        Assert.All(errors, e => Assert.IsType<InvalidOperationException>(e.Exception));
        Assert.Contains("MobileAccessBlocked", errors[0].Message);
        Assert.Contains($"profile {h.Profile.Id}", errors[0].Message);
        Assert.Contains("MobileAccessRestored", errors[1].Message);
    }

    [Fact]
    public async Task The_audit_service_logs_a_failed_write_instead_of_swallowing_it_and_never_throws()
    {
        CapturingLogger<AuditService> logger = new();
        AuditService service = new(new FailingAuditRepository(), logger);

        await service.LogAsync(AuditAction.MobileAccessBlocked, "Success", userId: 7, entityType: "MobileUserProfile", entityId: "42",
            correlationId: "corr-1", additionalData: "{\"secret\":\"must-not-be-logged\"}");

        var entry = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Error, entry.Level);
        Assert.IsType<InvalidOperationException>(entry.Exception);
        Assert.Contains("MobileAccessBlocked", entry.Message);
        Assert.Contains("42", entry.Message);
        Assert.Contains("corr-1", entry.Message);
        Assert.DoesNotContain("must-not-be-logged", entry.Message);          // the payload is never logged
    }

    private sealed class FailingAuditRepository : IAuditLogRepository
    {
        public Task AddAsync(AuditLog auditLog, CancellationToken cancellationToken = default) => throw new InvalidOperationException("db down");
        public Task<IReadOnlyList<AuditLog>> GetByUserIdAsync(int userId, int pageNumber, int pageSize, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<int> CountByUserIdAsync(int userId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}

internal static class DomainConstantsResults
{
    public const string Success = NSG.Mobile.Domain.Constants.DomainConstants.AuditResults.Success;
}

internal static class MobileAccessHarnessExtensions
{
    public static Task<Result<MobileLoginResponseDto>> LoginAsyncResult(this MobileAccessHarness h, string mpin = MobileAuthHarness.Mpin)
        => h.Auth.Service.LoginAsync(new MobileLoginRequestDto
        { NsgNumber = MobileAuthHarness.Nsg, MobileNumber = MobileAuthHarness.Mobile, DateOfBirth = MobileAuthHarness.Dob, Mpin = mpin }, "127.0.0.1");
}
