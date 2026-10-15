using System.Security.Cryptography;
using System.Text;
using NSG.Mobile.Application.Authentication.Interfaces;
using NSG.Mobile.Application.Common;
using NSG.Mobile.Application.Interfaces.Repositories;
using NSG.Mobile.Application.Interfaces.Services;
using NSG.Mobile.Application.MobileAuth.Services;
using NSG.Mobile.Application.Pis.DTOs;
using NSG.Mobile.Domain.Common;
using NSG.Mobile.Domain.Entities;
using NSG.Mobile.Domain.Enums;
using Microsoft.Extensions.Logging.Abstractions;

namespace NSG.Mobile.Tests;

// In-memory stand-ins for the repositories and infrastructure services, so the registration / OTP / MPIN workflow can be
// tested without a database. They mirror the contracts of the MySQL implementations (atomic claim of a grant, unique keys,
// one pending request per NSG number). The real SQL behaviour is covered by MobileAuthDatabaseTests (needs MySQL).

internal sealed class TestClock : TimeProvider
{
    public DateTimeOffset Now { get; set; } = new(2026, 10, 15, 6, 0, 0, TimeSpan.Zero);
    public override DateTimeOffset GetUtcNow() => Now;
    public void Advance(TimeSpan by) => Now += by;
}

internal sealed class FakePisRepository : IUserPisRepository
{
    public List<UserPis> Rows { get; } = new();
    public int NsgLookups { get; private set; }

    public Task<UserPis?> GetByNsgNumberAsync(string nsgNumber, CancellationToken cancellationToken = default)
    {
        NsgLookups++;
        return Task.FromResult(Rows.FirstOrDefault(p => string.Equals(p.NsgNumber, nsgNumber, StringComparison.OrdinalIgnoreCase)));
    }

    public Task<UserPis?> GetByIdAsync(long id, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<UserPis?> GetDetailAsync(long id, CancellationToken cancellationToken = default) => Task.FromResult(Rows.FirstOrDefault(p => p.Id == id));
    public Task<(IReadOnlyList<UserPis> Items, int TotalCount)> GetPagedAsync(int pageNumber, int pageSize, UserPisFilter filter, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<IReadOnlyList<string>> GetMissingReferencesAsync(UserPis candidate, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<PisSaveOutcome> AddAsync(UserPis entity, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<PisSaveOutcome> UpdateAsync(UserPis entity, CancellationToken cancellationToken = default) => throw new NotSupportedException();
}

internal sealed class FakeGrantRepository : IMobileAuthGrantRepository
{
    private readonly TimeProvider _clock;
    private long _nextId = 1;
    public List<MobileAuthGrant> Grants { get; } = new();

    public FakeGrantRepository(TimeProvider clock) => _clock = clock;

    public Task AddAsync(MobileAuthGrant grant, CancellationToken cancellationToken = default)
    {
        grant.Id = _nextId++;
        Grants.Add(grant);
        return Task.CompletedTask;
    }

    public Task<MobileAuthGrant?> GetByTokenHashAsync(string tokenHash, CancellationToken cancellationToken = default)
        => Task.FromResult(Grants.FirstOrDefault(g => g.TokenHash == tokenHash));

    public Task<bool> TryConsumeAsync(long grantId, DateTime nowUtc, CancellationToken cancellationToken = default)
        => Task.FromResult(Claim(grantId, nowUtc));

    public Task InvalidateForNsgNumberAsync(string nsgNumber, DateTime nowUtc, CancellationToken cancellationToken = default)
    {
        foreach (MobileAuthGrant g in Grants.Where(g => g.NsgNumber == nsgNumber && g.ConsumedAtIst == null)) g.ConsumedAtIst = nowUtc;
        return Task.CompletedTask;
    }

    internal bool Claim(long grantId, DateTime nowUtc)
    {
        MobileAuthGrant? g = Grants.FirstOrDefault(x => x.Id == grantId);
        if (g == null || g.ConsumedAtIst != null || g.ExpiresAtIst <= nowUtc) return false;
        g.ConsumedAtIst = nowUtc;
        return true;
    }
}

internal sealed class FakeProfileRepository : IMobileUserProfileRepository
{
    private readonly FakeGrantRepository _grants;
    private long _nextId = 1;
    public List<MobileUserProfile> Profiles { get; } = new();
    public FakeSessionRepository? Sessions { get; set; }

    public FakeProfileRepository(FakeGrantRepository grants) => _grants = grants;

    public Task<MobileUserProfile?> GetByNsgNumberAsync(string nsgNumber, CancellationToken cancellationToken = default)
        => Task.FromResult(Profiles.FirstOrDefault(p => string.Equals(p.NsgNumber, nsgNumber, StringComparison.OrdinalIgnoreCase)));

    public Task<MobileUserProfile?> GetByMobileNumberAsync(string mobileNumber, CancellationToken cancellationToken = default)
        => Task.FromResult(Profiles.FirstOrDefault(p => p.MobileNumber == mobileNumber));

    public Task<MobileUserProfile?> GetByIdAsync(long id, CancellationToken cancellationToken = default)
        => Task.FromResult(Profiles.FirstOrDefault(p => p.Id == id));

    public Task<FailedLoginState> RegisterFailedLoginAsync(long id, int maxAttempts, TimeSpan lockoutDuration, DateTime nowUtc, CancellationToken cancellationToken = default)
    {
        MobileUserProfile p = Profiles.Single(x => x.Id == id);
        p.FailedLoginAttempts++;
        if (p.FailedLoginAttempts >= maxAttempts && (p.LockedUntilIst == null || p.LockedUntilIst <= nowUtc)) p.LockedUntilIst = nowUtc.Add(lockoutDuration);
        return Task.FromResult(new FailedLoginState(p.FailedLoginAttempts, p.LockedUntilIst));
    }

    public Task ClearExpiredLockoutAsync(long id, DateTime nowUtc, CancellationToken cancellationToken = default)
    {
        MobileUserProfile p = Profiles.Single(x => x.Id == id);
        if (p.LockedUntilIst != null && p.LockedUntilIst <= nowUtc) { p.LockedUntilIst = null; p.FailedLoginAttempts = 0; }
        return Task.CompletedTask;
    }

    public Task RecordSuccessfulLoginAsync(long id, DateTime nowUtc, CancellationToken cancellationToken = default)
    {
        MobileUserProfile p = Profiles.Single(x => x.Id == id);
        p.FailedLoginAttempts = 0; p.LockedUntilIst = null; p.LastLoginAtIst = nowUtc;
        return Task.CompletedTask;
    }

    public Task SetMpinAsync(long id, string mpinHash, DateTime nowUtc, CancellationToken cancellationToken = default)
    {
        MobileUserProfile p = Profiles.Single(x => x.Id == id);
        p.MpinHash = mpinHash; p.FailedLoginAttempts = 0; p.LockedUntilIst = null; p.UpdatedAtIst = nowUtc;
        Sessions?.RevokeForMpinChange(id, MobileSessionRevokeReasons.MpinChanged, nowUtc);
        return Task.CompletedTask;
    }

    public Task<ProfileCreateOutcome> CreateWithGrantAsync(MobileUserProfile profile, long grantId, DateTime nowUtc, CancellationToken cancellationToken = default)
    {
        if (!_grants.Claim(grantId, nowUtc)) return Task.FromResult(ProfileCreateOutcome.GrantUnavailable);
        if (Profiles.Any(p => string.Equals(p.NsgNumber, profile.NsgNumber, StringComparison.OrdinalIgnoreCase)))
        {
            _grants.Grants.Single(g => g.Id == grantId).ConsumedAtIst = null;   // the real transaction rolls the claim back
            return Task.FromResult(ProfileCreateOutcome.DuplicateNsgNumber);
        }
        if (Profiles.Any(p => p.MobileNumber == profile.MobileNumber))
        {
            _grants.Grants.Single(g => g.Id == grantId).ConsumedAtIst = null;
            return Task.FromResult(ProfileCreateOutcome.DuplicateMobileNumber);
        }
        profile.Id = _nextId++;
        Profiles.Add(profile);
        return Task.FromResult(ProfileCreateOutcome.Created);
    }

    public Task<bool> ResetMpinWithGrantAsync(long profileId, string nsgNumber, string mpinHash, long grantId, DateTime nowUtc, CancellationToken cancellationToken = default)
    {
        if (!_grants.Claim(grantId, nowUtc)) return Task.FromResult(false);
        SetMpinAsync(profileId, mpinHash, nowUtc, cancellationToken);
        Sessions?.RevokeForMpinChange(profileId, MobileSessionRevokeReasons.MpinReset, nowUtc);
        foreach (MobileAuthGrant g in _grants.Grants.Where(g => g.NsgNumber == nsgNumber && g.ConsumedAtIst == null)) g.ConsumedAtIst = nowUtc;
        return Task.FromResult(true);
    }
}

internal sealed class FakeRequestRepository : IApproveMobileUserRepository
{
    private long _nextId = 1;
    public List<ApproveMobileUser> Requests { get; } = new();

    // Recorded for the approval-service tests.
    public (long Id, MobileAppActivationStatus Decision, int Approver, string? Remarks, DateTime Now)? LastDecision { get; private set; }
    public ApprovalDecisionResult NextDecisionResult { get; set; } = new(ApprovalDecisionOutcome.Decided, new ApproveMobileUser { Id = 1, NsgNumber = "N", MobileNumber = "9876543210" });

    public Task<ApproveMobileUser?> GetPendingByNsgNumberAsync(string nsgNumber, CancellationToken cancellationToken = default)
        => Task.FromResult(Requests.FirstOrDefault(r => r.ApprovalStatus == MobileAppActivationStatus.Pending && string.Equals(r.NsgNumber, nsgNumber, StringComparison.OrdinalIgnoreCase)));

    public Task<PendingRequestOutcome> AddPendingAsync(ApproveMobileUser request, CancellationToken cancellationToken = default)
    {
        // Mirrors ix_approve_mobile_users_pending_nsg_number.
        if (Requests.Any(r => r.ApprovalStatus == MobileAppActivationStatus.Pending && string.Equals(r.NsgNumber, request.NsgNumber, StringComparison.OrdinalIgnoreCase)))
            return Task.FromResult(PendingRequestOutcome.AlreadyPending);
        request.Id = _nextId++;
        Requests.Add(request);
        return Task.FromResult(PendingRequestOutcome.Created);
    }

    public Task<(IReadOnlyList<ApprovalRequestRow> Items, int TotalCount)> GetPagedAsync(int pageNumber, int pageSize, MobileAppActivationStatus? status, string? search, CancellationToken cancellationToken = default)
    {
        IReadOnlyList<ApprovalRequestRow> rows = Requests.Where(r => status == null || r.ApprovalStatus == status)
            .Select(r => new ApprovalRequestRow(r, null)).ToList();
        return Task.FromResult((rows, rows.Count));
    }

    public Task<ApprovalDecisionResult> DecideAsync(long id, MobileAppActivationStatus decision, int approverUserId, string? remarks, DateTime nowUtc, CancellationToken cancellationToken = default)
    {
        LastDecision = (id, decision, approverUserId, remarks, nowUtc);
        return Task.FromResult(NextDecisionResult);
    }
}

/// <summary>OTP service and OTP repository in one: the code is always <see cref="Code"/>; resend cooldown, expiry and attempt limits behave like <c>OtpService</c>.</summary>
internal sealed class FakeOtp : IOtpService, IOtpRequestRepository
{
    public const string Code = "123456";
    private readonly TimeProvider _clock;
    public List<OtpRequest> Requests { get; } = new();
    public int Sends => Requests.Count;
    public int MaxAttempts { get; set; } = 5;
    public int CooldownSeconds { get; set; } = 60;
    public bool SmsFails { get; set; }
    /// <summary>Runs after the OTP matched and before the service re-evaluates eligibility (simulates a change during the OTP wait).</summary>
    public Action? DuringVerify { get; set; }

    public FakeOtp(TimeProvider clock) => _clock = clock;

    public Task<OtpGenerationResult> GenerateAndSendAsync(string normalizedMobileNumber, OtpPurpose purpose, CancellationToken cancellationToken = default)
    {
        DateTime now = IndianStandardTime.ConvertFromUtc(_clock.GetUtcNow().UtcDateTime);
        OtpRequest? latest = Requests.Where(o => o.MobileNumber == normalizedMobileNumber && o.Purpose == purpose).OrderByDescending(o => o.CreatedAt).FirstOrDefault();
        if (latest != null && now < latest.CreatedAt.AddSeconds(CooldownSeconds))
            return Task.FromResult(new OtpGenerationResult(false, null, ApiErrorCodes.OtpResendCooldown, "Please wait before requesting another OTP."));
        if (SmsFails)
            return Task.FromResult(new OtpGenerationResult(false, null, ApiErrorCodes.OtpSendFailed, "Failed to send OTP via SMS."));

        OtpRequest otp = new() { MobileNumber = normalizedMobileNumber, Purpose = purpose, OtpHash = Code, CreatedAt = now, ExpiresAt = now.AddMinutes(5) };
        Requests.Add(otp);
        return Task.FromResult(new OtpGenerationResult(true, otp.ReferenceId, ExpiresAtIst: otp.ExpiresAt));
    }

    public Task<OtpVerificationResult> VerifyAsync(Guid referenceId, string otpCode, OtpPurpose expectedPurpose, CancellationToken cancellationToken = default)
    {
        OtpRequest? otp = Requests.FirstOrDefault(o => o.ReferenceId == referenceId);
        if (otp == null || otp.Purpose != expectedPurpose) return Task.FromResult(new OtpVerificationResult(false, ApiErrorCodes.OtpInvalid, "Invalid OTP reference."));
        if (otp.IsVerified) return Task.FromResult(new OtpVerificationResult(false, ApiErrorCodes.OtpInvalid, "OTP has already been used."));
        DateTime now = IndianStandardTime.ConvertFromUtc(_clock.GetUtcNow().UtcDateTime);
        if (now > otp.ExpiresAt) return Task.FromResult(new OtpVerificationResult(false, ApiErrorCodes.OtpExpired, "OTP has expired."));
        if (otp.AttemptCount >= MaxAttempts) return Task.FromResult(new OtpVerificationResult(false, ApiErrorCodes.OtpMaxAttemptsExceeded, "Maximum verification attempts exceeded."));
        otp.AttemptCount++;
        if (otpCode != otp.OtpHash) return Task.FromResult(new OtpVerificationResult(false, ApiErrorCodes.OtpInvalid, "Invalid OTP code."));
        otp.IsVerified = true;
        otp.VerifiedAt = now;
        DuringVerify?.Invoke();
        return Task.FromResult(new OtpVerificationResult(true));
    }

    public Task<OtpRequest?> GetByReferenceIdAsync(Guid referenceId, CancellationToken cancellationToken = default)
        => Task.FromResult(Requests.FirstOrDefault(o => o.ReferenceId == referenceId));

    public Task<OtpRequest?> GetLatestByMobileAndPurposeAsync(string normalizedMobileNumber, OtpPurpose purpose, CancellationToken cancellationToken = default)
        => Task.FromResult(Requests.Where(o => o.MobileNumber == normalizedMobileNumber && o.Purpose == purpose).OrderByDescending(o => o.CreatedAt).FirstOrDefault());

    public Task<int> CountSinceAsync(string normalizedMobileNumber, OtpPurpose purpose, DateTime sinceIst, CancellationToken cancellationToken = default)
        => Task.FromResult(Requests.Count(o => o.MobileNumber == normalizedMobileNumber && o.Purpose == purpose && o.CreatedAt >= sinceIst));

    public Task AddAsync(OtpRequest otpRequest, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task UpdateAsync(OtpRequest otpRequest, CancellationToken cancellationToken = default) => throw new NotSupportedException();
}

internal sealed class FakeHasher : IPasswordHashingService
{
    public string HashPassword(string password) => "hash:" + password;
    public bool VerifyPassword(string password, string passwordHash) => passwordHash == "hash:" + password;
}

internal sealed class FakeTokenService : ITokenService
{
    public int MobileTokensIssued { get; private set; }
    public Guid LastSessionId { get; private set; }
    private int _refreshCounter;

    public MobileAccessToken GenerateMobileAccessToken(MobileUserProfile profile, Guid sessionId)
    {
        MobileTokensIssued++;
        LastSessionId = sessionId;
        return new MobileAccessToken("jwt-for-" + profile.Id, 900);
    }

    public string HashToken(string token) => Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
    public string GenerateRefreshToken() => "refresh-secret-" + (++_refreshCounter);
}

internal sealed class FakeAudit : IAuditService
{
    public List<(AuditAction Action, string Result, int? UserId, string? EntityId, string? Data)> Entries { get; } = new();

    public Task LogAsync(AuditAction action, string result, int? userId = null, string? entityType = null, string? entityId = null,
        string? ipAddress = null, int? deviceId = null, string? correlationId = null, string? additionalData = null, CancellationToken cancellationToken = default)
    {
        Entries.Add((action, result, userId, entityId, additionalData));
        return Task.CompletedTask;
    }
}

internal sealed class FakeCurrentUser : ICurrentUserService
{
    public int? UserId { get; set; }
    public long? ProfileId { get; set; }
    public int? DeviceId => null;
    public string? Role => null;
    public string? CorrelationId => "test-correlation";
    public bool IsAuthenticated => UserId != null || ProfileId != null;
}

/// <summary>A wired-up <see cref="MobileAuthService"/> over the in-memory fakes, with one person ready in PIS.</summary>
internal sealed class MobileAuthHarness
{
    public const string Nsg = "NSG-1001";
    public const string Mobile = "9876543210";
    public static readonly DateOnly Dob = new(1990, 5, 15);
    public const string Mpin = "2580";

    public TestClock Clock { get; } = new();
    public FakePisRepository Pis { get; } = new();
    public FakeGrantRepository Grants { get; }
    public FakeProfileRepository Profiles { get; }
    public FakeSessionRepository Sessions { get; } = new();
    public FakeRequestRepository Requests { get; } = new();
    public FakeOtp Otp { get; }
    public FakeTokenService Tokens { get; } = new();
    public FakeAudit Audit { get; } = new();
    public FakeCurrentUser CurrentUser { get; } = new();
    public MobileAuthSettings Settings { get; } = new() { MaxFailedAttempts = 3, LockoutMinutes = 30, GrantLifetimeMinutes = 10, OtpMaxSendsPerHour = 3 };
    public MobileAuthService Service { get; }

    public MobileAuthHarness(MobileAppActivationStatus? pisStatus = MobileAppActivationStatus.Pending)
    {
        Grants = new FakeGrantRepository(Clock);
        Profiles = new FakeProfileRepository(Grants) { Sessions = Sessions };
        Sessions.Profiles = Profiles;
        Otp = new FakeOtp(Clock);
        if (pisStatus != null) AddPis(Nsg, Mobile, Dob, pisStatus.Value);
        Service = new MobileAuthService(Profiles, Requests, Grants, Sessions, Pis, Otp, Otp, new FakeHasher(), Tokens, Audit, CurrentUser,
            Settings, Clock, NullLogger<MobileAuthService>.Instance);
    }

    public UserPis AddPis(string nsg, string mobile, DateOnly dob, MobileAppActivationStatus status, bool active = true)
    {
        UserPis pis = new() { NsgNumber = nsg, ServiceNumber = "SVC", MobileNumber = mobile, DateOfBirth = dob, MobileAppActivationStatus = status, IsActive = active };
        Pis.Rows.Add(pis);
        return pis;
    }

    public MobileUserProfile AddProfile(bool active = true, string? mpin = Mpin, string nsg = Nsg, string mobile = Mobile)
    {
        MobileUserProfile profile = new()
        {
            NsgNumber = nsg, MobileNumber = mobile, DateOfBirth = Dob, Role = UserRole.Employee, IsActive = active,
            MpinHash = mpin == null ? null : "hash:" + mpin
        };
        Profiles.CreateWithGrantAsync(profile, SeedGrant(), Clock.Now.UtcDateTime).GetAwaiter().GetResult();
        return profile;
    }

    private long SeedGrant()
    {
        MobileAuthGrant g = new() { TokenHash = Guid.NewGuid().ToString(), ExpiresAtIst = Clock.Now.UtcDateTime.AddMinutes(1), NsgNumber = "seed", MobileNumber = Mobile };
        Grants.AddAsync(g).GetAwaiter().GetResult();
        return g.Id;
    }

    public NSG.Mobile.Application.MobileAuth.DTOs.MobileLoginCheckRequestDto Check(string? nsg = null, string? mobile = null, DateOnly? dob = null)
        => new() { NsgNumber = nsg ?? Nsg, MobileNumber = mobile ?? Mobile, DateOfBirth = dob ?? Dob };

    public NSG.Mobile.Application.MobileAuth.DTOs.MobileOtpSendRequestDto Send(NSG.Mobile.Application.MobileAuth.DTOs.MobileOtpFlow flow = NSG.Mobile.Application.MobileAuth.DTOs.MobileOtpFlow.Registration)
        => new() { NsgNumber = Nsg, MobileNumber = Mobile, DateOfBirth = Dob, Flow = flow };

    public NSG.Mobile.Application.MobileAuth.DTOs.MobileOtpVerifyRequestDto Verify(Guid reference, string code = FakeOtp.Code,
        NSG.Mobile.Application.MobileAuth.DTOs.MobileOtpFlow flow = NSG.Mobile.Application.MobileAuth.DTOs.MobileOtpFlow.Registration)
        => new() { NsgNumber = Nsg, MobileNumber = Mobile, DateOfBirth = Dob, Flow = flow, OtpReferenceId = reference, OtpCode = code };

    /// <summary>Sends and verifies a registration OTP and returns the verify result.</summary>
    public async Task<Result<NSG.Mobile.Application.MobileAuth.DTOs.MobileAuthFlowDto>> SendAndVerifyAsync(
        NSG.Mobile.Application.MobileAuth.DTOs.MobileOtpFlow flow = NSG.Mobile.Application.MobileAuth.DTOs.MobileOtpFlow.Registration)
    {
        var sent = await Service.SendOtpAsync(Send(flow), "127.0.0.1");
        Assert.True(sent.IsSuccess, sent.ErrorMessage);
        return await Service.VerifyOtpAsync(Verify(sent.Data!.OtpReferenceId!.Value, flow: flow), "127.0.0.1");
    }
}

/// <summary>In-memory stand-in for <c>mobile_refresh_tokens</c> with the same rotation, reuse and revocation rules as the SQL implementation.</summary>
internal sealed class FakeSessionRepository : IMobileRefreshTokenRepository
{
    private long _nextId = 1;
    public List<MobileRefreshToken> Tokens { get; } = new();
    public FakeProfileRepository? Profiles { get; set; }

    public Task StartSessionAsync(MobileRefreshToken first, DateTime nowUtc, CancellationToken cancellationToken = default)
    {
        RevokeLive(t => t.ProfileId == first.ProfileId, MobileSessionRevokeReasons.NewLogin, nowUtc);
        first.Id = _nextId++;
        Tokens.Add(first);
        return Task.CompletedTask;
    }

    public Task<RefreshRotationResult> RotateAsync(string presentedTokenHash, MobileRefreshToken next, DateTime nowUtc, CancellationToken cancellationToken = default)
    {
        MobileRefreshToken? t = Tokens.FirstOrDefault(x => x.TokenHash == presentedTokenHash);
        if (t == null) return Task.FromResult(new RefreshRotationResult(RefreshRotationOutcome.Invalid));
        if (t.RevokedAtIst == null && t.ExpiresAtIst > nowUtc)
        {
            t.RevokedAtIst = nowUtc; t.RevokeReason = MobileSessionRevokeReasons.Rotated;
            next.Id = _nextId++; next.ProfileId = t.ProfileId; next.FamilyId = t.FamilyId;
            Tokens.Add(next);
            return Task.FromResult(new RefreshRotationResult(RefreshRotationOutcome.Rotated, t.ProfileId, t.FamilyId));
        }
        if (t.RevokedAtIst != null && t.RevokeReason == MobileSessionRevokeReasons.Rotated)
        {
            t.ReuseDetectedAtIst = nowUtc;
            RevokeLive(x => x.FamilyId == t.FamilyId, MobileSessionRevokeReasons.ReuseDetected, nowUtc);
            return Task.FromResult(new RefreshRotationResult(RefreshRotationOutcome.ReuseDetected, t.ProfileId, t.FamilyId));
        }
        return Task.FromResult(new RefreshRotationResult(t.RevokedAtIst == null ? RefreshRotationOutcome.Expired : RefreshRotationOutcome.Invalid, t.ProfileId, t.FamilyId));
    }

    public Task<bool> RevokeSessionByTokenAsync(string tokenHash, string reason, DateTime nowUtc, CancellationToken cancellationToken = default)
    {
        MobileRefreshToken? t = Tokens.FirstOrDefault(x => x.TokenHash == tokenHash);
        if (t == null) return Task.FromResult(false);
        RevokeLive(x => x.FamilyId == t.FamilyId, reason, nowUtc);
        return Task.FromResult(true);
    }

    public Task RevokeAllForProfileAsync(long profileId, string reason, DateTime nowUtc, CancellationToken cancellationToken = default)
    {
        RevokeLive(t => t.ProfileId == profileId, reason, nowUtc);
        return Task.CompletedTask;
    }

    public Task<bool> IsSessionActiveAsync(Guid sessionId, long profileId, CancellationToken cancellationToken = default)
        => Task.FromResult(Tokens.Any(t => t.FamilyId == sessionId && t.ProfileId == profileId && t.RevokedAtIst == null)
                           && (Profiles?.Profiles.FirstOrDefault(p => p.Id == profileId)?.IsActive ?? true));

    /// <summary>What <c>SetMpinAsync</c> / <c>ResetMpinWithGrantAsync</c> do to sessions in the same transaction as the MPIN update.</summary>
    public void RevokeForMpinChange(long profileId, string reason, DateTime nowUtc) => RevokeLive(t => t.ProfileId == profileId, reason, nowUtc);

    private void RevokeLive(Func<MobileRefreshToken, bool> where, string reason, DateTime nowUtc)
    {
        foreach (MobileRefreshToken t in Tokens.Where(where).Where(t => t.RevokedAtIst == null)) { t.RevokedAtIst = nowUtc; t.RevokeReason = reason; }
    }
}
