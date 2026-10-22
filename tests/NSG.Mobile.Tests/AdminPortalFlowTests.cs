using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NSG.Mobile.Application.Admin.Services;
using NSG.Mobile.Application.Common;
using NSG.Mobile.Domain.Constants;
using NSG.Mobile.Domain.Entities;
using NSG.Mobile.Domain.Enums;
using NSG.Mobile.Infrastructure.Persistence;

namespace NSG.Mobile.Tests;

/// <summary>
/// End-to-end Admin Portal flow over HTTP against real MySQL, in demo-OTP mode (the test host runs in Development, where
/// Otp:Mode=Demo): bootstrap, activation, password + OTP sign-in, refresh rotation, sign-out, role enforcement, token separation and
/// deactivation. Needs NSG_TEST_MYSQL (a database whose name ends in _test); see ApiFactory.
/// </summary>
[Collection("mysql")]
public class AdminPortalFlowTests : IAsyncLifetime
{
    private const string Auth = "/api/v1/admin/auth", Activation = "/api/v1/admin/activation", Accounts = "/api/v1/admin/accounts";
    private const string DemoOtp = "123456", Password = "Str0ng!Passw0rd#1", Dob = "1985-06-15";

    private const string SuperNsg = "ADM-0001", SuperMobile = "9811110001";
    private const string PisNsg = "ADM-0002", PisMobile = "9811110002";
    private const string OtherNsg = "ADM-0003", OtherMobile = "9811110003";

    private readonly ApiFactory _factory;
    private readonly HttpClient _anonymous;

    public AdminPortalFlowTests(DatabaseFixture fixture)
    {
        _factory = fixture.Factory;
        _anonymous = _factory.CreateClientFor(null);
    }

    public async Task InitializeAsync() => await CleanAsync();
    public async Task DisposeAsync() => await CleanAsync();

    private async Task CleanAsync()
    {
        await using AppDbContext ctx = _factory.CreateContext();
        await ctx.Database.ExecuteSqlRawAsync("DELETE FROM `admin_refresh_tokens`");
        await ctx.Database.ExecuteSqlRawAsync("DELETE FROM `admin_profiles`");
        await ctx.Database.ExecuteSqlRawAsync("DELETE FROM `otp_requests`");
        await ctx.Database.ExecuteSqlRawAsync("DELETE FROM `approve_mobile_users`");
        await ctx.Database.ExecuteSqlRawAsync("DELETE FROM `users_pis`");
        await ctx.Database.ExecuteSqlRawAsync("DELETE FROM `audit_logs`");
    }

    // ───────────────────────── helpers ─────────────────────────

    private async Task SeedPisAsync(string nsg, string mobile, string name, bool active = true, string dob = Dob)
    {
        await using AppDbContext ctx = _factory.CreateContext();
        ctx.UsersPis.Add(new UserPis { NsgNumber = nsg, ServiceNumber = "S-" + nsg, FullName = name, MobileNumber = mobile, DateOfBirth = DateOnly.Parse(dob), IsActive = active });
        await ctx.SaveChangesAsync();
    }

    /// <summary>Moves stored OTPs into the past so the resend cooldown and hourly cap do not interfere with a multi-step test.</summary>
    private async Task AgeOtpsAsync()
    {
        await using AppDbContext ctx = _factory.CreateContext();
        await ctx.Database.ExecuteSqlRawAsync("UPDATE `otp_requests` SET `created_at_ist` = `created_at_ist` - INTERVAL 3 HOUR");
    }

    private static StringContent Json(object body) => new(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

    private static async Task<(HttpStatusCode Status, JsonElement Body)> ReadAsync(HttpResponseMessage response, string what)
    {
        string text = await response.Content.ReadAsStringAsync();
        Assert.False(string.IsNullOrWhiteSpace(text), $"{what} returned a blank body ({(int)response.StatusCode}).");
        return (response.StatusCode, JsonDocument.Parse(text).RootElement.Clone());
    }

    private async Task<(HttpStatusCode Status, JsonElement Body)> PostAsync(HttpClient client, string url, object? body = null)
        => await ReadAsync(await client.PostAsync(url, Json(body ?? new { })), "POST " + url);

    private async Task<(HttpStatusCode Status, JsonElement Body)> GetAsync(HttpClient client, string url)
        => await ReadAsync(await client.GetAsync(url), "GET " + url);

    private static string Detail(JsonElement body) => body.GetProperty("message").GetString()!;

    private HttpClient Bearer(string accessToken)
    {
        HttpClient client = _factory.CreateClientFor(null);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return client;
    }

    private async Task<long> BootstrapAsync(string nsg)
    {
        await using AsyncServiceScope scope = _factory.Services.CreateAsyncScope();
        Result<long> result = await scope.ServiceProvider.GetRequiredService<IAdminBootstrapService>().CreateFirstSuperAdminAsync(nsg);
        Assert.True(result.IsSuccess, result.ErrorMessage);
        return result.Data;
    }

    private async Task ActivateAsync(string nsg, string mobile, string password = Password)
    {
        await AgeOtpsAsync();
        (HttpStatusCode sendStatus, JsonElement sent) = await PostAsync(_anonymous, $"{Activation}/otp", new { nsgNumber = nsg, mobileNumber = mobile, dateOfBirth = Dob });
        Assert.Equal(HttpStatusCode.OK, sendStatus);
        Guid reference = sent.GetProperty("data").GetProperty("otpReferenceId").GetGuid();

        (HttpStatusCode status, JsonElement body) = await PostAsync(_anonymous, $"{Activation}/complete", new
        {
            nsgNumber = nsg, mobileNumber = mobile, dateOfBirth = Dob, otpReferenceId = reference, otpCode = DemoOtp,
            newPassword = password, confirmPassword = password
        });
        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Contains("activated", body.GetProperty("message").GetString());
    }

    /// <summary>Signs in with password + OTP and returns the full response data (tokens and profile).</summary>
    private async Task<JsonElement> SignInAsync(string nsg, string password = Password)
    {
        await AgeOtpsAsync();
        (HttpStatusCode step1, JsonElement sent) = await PostAsync(_anonymous, $"{Auth}/login", new { nsgNumber = nsg, password });
        Assert.Equal(HttpStatusCode.OK, step1);
        Guid reference = sent.GetProperty("data").GetProperty("otpReferenceId").GetGuid();

        (HttpStatusCode step2, JsonElement tokens) = await PostAsync(_anonymous, $"{Auth}/login/verify", new { nsgNumber = nsg, password, otpReferenceId = reference, otpCode = DemoOtp });
        Assert.Equal(HttpStatusCode.OK, step2);
        return tokens.GetProperty("data");
    }

    /// <summary>Bootstraps and activates the first Super Admin and returns a client that carries their access token.</summary>
    private async Task<HttpClient> SuperAdminClientAsync()
    {
        await SeedPisAsync(SuperNsg, SuperMobile, "Super Person");
        await BootstrapAsync(SuperNsg);
        await ActivateAsync(SuperNsg, SuperMobile);
        return Bearer((await SignInAsync(SuperNsg)).GetProperty("accessToken").GetString()!);
    }

    private async Task<long> CreateAccountAsync(HttpClient superAdmin, string nsg, string mobile, string roleCode)
    {
        (HttpStatusCode status, JsonElement body) = await PostAsync(superAdmin, Accounts, new { nsgNumber = nsg, mobileNumber = mobile, dateOfBirth = Dob, roleCode });
        Assert.Equal(HttpStatusCode.Created, status);
        return body.GetProperty("data").GetProperty("id").GetInt64();
    }

    // ───────────────────────── bootstrap ─────────────────────────

    [Fact]
    public async Task Bootstrap_creates_one_pending_super_admin_without_a_password_and_refuses_a_second()
    {
        await SeedPisAsync(SuperNsg, SuperMobile, "Super Person");
        await SeedPisAsync(OtherNsg, OtherMobile, "Other Person");

        long id = await BootstrapAsync(SuperNsg);

        await using (AppDbContext ctx = _factory.CreateContext())
        {
            AdminProfile profile = await ctx.AdminProfiles.Include(p => p.Role).SingleAsync(p => p.Id == id);
            Assert.Equal(AdminRoleCodes.SuperAdmin, profile.Role!.RoleCode);
            Assert.Equal(AdminActivationStatus.PendingActivation, profile.ActivationStatus);
            Assert.Null(profile.PasswordHash);
            Assert.Null(profile.CreatedBy);
        }

        await using AsyncServiceScope scope = _factory.Services.CreateAsyncScope();
        Result<long> second = await scope.ServiceProvider.GetRequiredService<IAdminBootstrapService>().CreateFirstSuperAdminAsync(OtherNsg);
        Assert.False(second.IsSuccess);
        Assert.Equal(ApiErrorCodes.Conflict, second.ErrorCode);

        await using AppDbContext after = _factory.CreateContext();
        Assert.Equal(1, await after.AdminProfiles.CountAsync());
    }

    [Fact]
    public async Task Bootstrap_requires_a_personnel_record()
    {
        await using AsyncServiceScope scope = _factory.Services.CreateAsyncScope();
        Result<long> result = await scope.ServiceProvider.GetRequiredService<IAdminBootstrapService>().CreateFirstSuperAdminAsync("NOT-IN-PIS");

        Assert.False(result.IsSuccess);
        Assert.Equal(ApiErrorCodes.NotFound, result.ErrorCode);
    }

    // ───────────────────────── activation ─────────────────────────

    [Fact]
    public async Task Activation_sends_the_otp_to_the_pis_number_stores_only_a_password_hash_and_activates_once()
    {
        await SeedPisAsync(SuperNsg, SuperMobile, "Super Person");
        long id = await BootstrapAsync(SuperNsg);

        await AgeOtpsAsync();
        (HttpStatusCode status, JsonElement sent) = await PostAsync(_anonymous, $"{Activation}/otp", new { nsgNumber = SuperNsg, mobileNumber = "+91" + SuperMobile, dateOfBirth = Dob });
        Assert.Equal(HttpStatusCode.OK, status);
        Assert.True(sent.GetProperty("data").GetProperty("demoOtpMode").GetBoolean());
        Assert.EndsWith("0001", sent.GetProperty("data").GetProperty("maskedMobileNumber").GetString());
        Assert.DoesNotContain(DemoOtp, sent.ToString());

        await using (AppDbContext ctx = _factory.CreateContext())
        {
            OtpRequest otp = await ctx.OtpRequests.SingleAsync();
            Assert.Equal("+91" + SuperMobile, otp.MobileNumber);          // the PIS number
            Assert.Equal(OtpPurpose.AdminActivation, otp.Purpose);
            Assert.NotEqual(DemoOtp, otp.OtpHash);
        }

        Guid reference = sent.GetProperty("data").GetProperty("otpReferenceId").GetGuid();
        object complete = new { nsgNumber = SuperNsg, mobileNumber = SuperMobile, dateOfBirth = Dob, otpReferenceId = reference, otpCode = DemoOtp, newPassword = Password, confirmPassword = Password };

        // A weak password is refused before the OTP is consumed.
        (HttpStatusCode weak, JsonElement weakBody) = await PostAsync(_anonymous, $"{Activation}/complete",
            new { nsgNumber = SuperNsg, mobileNumber = SuperMobile, dateOfBirth = Dob, otpReferenceId = reference, otpCode = DemoOtp, newPassword = "short", confirmPassword = "short" });
        Assert.Equal(HttpStatusCode.BadRequest, weak);
        Assert.Contains("Password", weakBody.ToString());

        (HttpStatusCode done, _) = await PostAsync(_anonymous, $"{Activation}/complete", complete);
        Assert.Equal(HttpStatusCode.OK, done);

        await using (AppDbContext ctx = _factory.CreateContext())
        {
            AdminProfile profile = await ctx.AdminProfiles.AsNoTracking().SingleAsync(p => p.Id == id);
            Assert.Equal(AdminActivationStatus.Active, profile.ActivationStatus);
            Assert.NotNull(profile.PasswordHash);
            Assert.DoesNotContain(Password, profile.PasswordHash);
            Assert.StartsWith("100000.", profile.PasswordHash);            // PBKDF2, 100K iterations
        }

        // Already active: the same call is no longer a match, and the OTP is spent anyway.
        Assert.Equal(HttpStatusCode.NotFound, (await PostAsync(_anonymous, $"{Activation}/complete", complete)).Status);
        Assert.Equal(HttpStatusCode.NotFound, (await PostAsync(_anonymous, $"{Activation}/otp", new { nsgNumber = SuperNsg, mobileNumber = SuperMobile, dateOfBirth = Dob })).Status);
    }

    [Fact]
    public async Task Activation_with_a_wrong_birth_date_unknown_person_or_wrong_otp_does_not_activate_or_reveal_which()
    {
        await SeedPisAsync(SuperNsg, SuperMobile, "Super Person");
        long id = await BootstrapAsync(SuperNsg);

        (HttpStatusCode wrongDob, JsonElement dobBody) = await PostAsync(_anonymous, $"{Activation}/otp", new { nsgNumber = SuperNsg, mobileNumber = SuperMobile, dateOfBirth = "1990-01-01" });
        (HttpStatusCode wrongMobile, _) = await PostAsync(_anonymous, $"{Activation}/otp", new { nsgNumber = SuperNsg, mobileNumber = "9999999999", dateOfBirth = Dob });
        (HttpStatusCode unknown, JsonElement unknownBody) = await PostAsync(_anonymous, $"{Activation}/otp", new { nsgNumber = "NOBODY", mobileNumber = SuperMobile, dateOfBirth = Dob });

        Assert.Equal(HttpStatusCode.NotFound, wrongDob);
        Assert.Equal(HttpStatusCode.NotFound, wrongMobile);
        Assert.Equal(HttpStatusCode.NotFound, unknown);
        Assert.Equal(Detail(dobBody), Detail(unknownBody));              // same answer: nothing to enumerate
        await using (AppDbContext ctx = _factory.CreateContext())
            Assert.Empty(await ctx.OtpRequests.ToListAsync());           // no SMS was triggered

        await AgeOtpsAsync();
        (_, JsonElement sent) = await PostAsync(_anonymous, $"{Activation}/otp", new { nsgNumber = SuperNsg, mobileNumber = SuperMobile, dateOfBirth = Dob });
        (HttpStatusCode wrongOtp, _) = await PostAsync(_anonymous, $"{Activation}/complete", new
        {
            nsgNumber = SuperNsg, mobileNumber = SuperMobile, dateOfBirth = Dob, otpReferenceId = sent.GetProperty("data").GetProperty("otpReferenceId").GetGuid(),
            otpCode = "000000", newPassword = Password, confirmPassword = Password
        });
        Assert.Equal(HttpStatusCode.BadRequest, wrongOtp);

        await using AppDbContext after = _factory.CreateContext();
        AdminProfile profile = await after.AdminProfiles.AsNoTracking().SingleAsync(p => p.Id == id);
        Assert.Equal(AdminActivationStatus.PendingActivation, profile.ActivationStatus);
        Assert.Null(profile.PasswordHash);
    }

    // ───────────────────────── account management ─────────────────────────

    [Fact]
    public async Task Super_admin_provisions_accounts_after_checking_pis_and_duplicates_are_refused()
    {
        HttpClient superAdmin = await SuperAdminClientAsync();
        await SeedPisAsync(PisNsg, PisMobile, "Pis Person");

        // Wrong birth date: PIS does not match.
        (HttpStatusCode mismatch, JsonElement mismatchBody) = await PostAsync(superAdmin, Accounts, new { nsgNumber = PisNsg, mobileNumber = PisMobile, dateOfBirth = "1999-09-09", roleCode = "PIS_ADMIN" });
        Assert.Equal(HttpStatusCode.NotFound, mismatch);
        Assert.False(string.IsNullOrWhiteSpace(Detail(mismatchBody)));

        // A role outside the six fixed ones.
        Assert.Equal(HttpStatusCode.BadRequest, (await PostAsync(superAdmin, Accounts, new { nsgNumber = PisNsg, mobileNumber = PisMobile, dateOfBirth = Dob, roleCode = "ROOT" })).Status);

        (HttpStatusCode created, JsonElement body) = await PostAsync(superAdmin, Accounts, new { nsgNumber = PisNsg, mobileNumber = PisMobile, dateOfBirth = Dob, roleCode = "pis_admin" });
        Assert.Equal(HttpStatusCode.Created, created);
        JsonElement data = body.GetProperty("data");
        Assert.Equal("PIS_ADMIN", data.GetProperty("roleCode").GetString());
        Assert.Equal("PendingActivation", data.GetProperty("activationStatus").GetString());
        Assert.Equal("Pis Person", data.GetProperty("fullName").GetString());
        Assert.NotEmpty(body.GetProperty("message").GetString()!);
        Assert.DoesNotContain("passwordHash", body.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(PisMobile, body.ToString());               // no mobile number in the response

        await using (AppDbContext ctx = _factory.CreateContext())
        {
            AdminProfile row = await ctx.AdminProfiles.AsNoTracking().SingleAsync(p => p.NsgNumber == PisNsg);
            Assert.Null(row.PasswordHash);                               // never a usable password at provisioning
            Assert.NotNull(row.CreatedBy);                               // the acting Super Admin, from the token
        }

        // Duplicate account.
        Assert.Equal(HttpStatusCode.Conflict, (await PostAsync(superAdmin, Accounts, new { nsgNumber = PisNsg, mobileNumber = PisMobile, dateOfBirth = Dob, roleCode = "PIS_ADMIN" })).Status);

        // Inactive personnel cannot be provisioned.
        await SeedPisAsync(OtherNsg, OtherMobile, "Retired Person", active: false);
        Assert.Equal(HttpStatusCode.NotFound, (await PostAsync(superAdmin, Accounts, new { nsgNumber = OtherNsg, mobileNumber = OtherMobile, dateOfBirth = Dob, roleCode = "PIS_ADMIN" })).Status);
    }

    [Fact]
    public async Task Lists_roles_and_accounts_with_messages_instead_of_blank_responses()
    {
        HttpClient superAdmin = await SuperAdminClientAsync();

        (HttpStatusCode rolesStatus, JsonElement roles) = await GetAsync(superAdmin, "/api/v1/admin/roles");
        Assert.Equal(HttpStatusCode.OK, rolesStatus);
        string[] codes = roles.GetProperty("data").EnumerateArray().Select(r => r.GetProperty("roleCode").GetString()!).ToArray();
        Assert.Equal(new[] { "SUPER_ADMIN", "REGISTRATION_ADMIN", "PIS_ADMIN", "GRIEVANCE_ADMIN", "MEDIA_CELL_ADMIN", "SALARY_MANAGEMENT_ADMIN" }, codes);

        (HttpStatusCode listStatus, JsonElement list) = await GetAsync(superAdmin, Accounts);
        Assert.Equal(HttpStatusCode.OK, listStatus);
        Assert.Equal(1, list.GetProperty("totalCount").GetInt32());

        (HttpStatusCode emptyStatus, JsonElement empty) = await GetAsync(superAdmin, Accounts + "?roleCode=GRIEVANCE_ADMIN");
        Assert.Equal(HttpStatusCode.OK, emptyStatus);
        Assert.Equal("No admin accounts found.", empty.GetProperty("message").GetString());
        Assert.False(empty.TryGetProperty("items", out _));

        (HttpStatusCode missing, JsonElement missingBody) = await GetAsync(superAdmin, Accounts + "/987654");
        Assert.Equal(HttpStatusCode.NotFound, missing);
        Assert.Equal("Admin account not found.", Detail(missingBody));
    }

    // ───────────────────────── sign-in ─────────────────────────

    [Fact]
    public async Task Sign_in_needs_password_then_otp_and_the_token_is_a_separate_admin_token()
    {
        await SeedPisAsync(SuperNsg, SuperMobile, "Super Person");
        await BootstrapAsync(SuperNsg);
        await ActivateAsync(SuperNsg, SuperMobile);

        // Step 1 sends an OTP and issues nothing else.
        await AgeOtpsAsync();
        (HttpStatusCode step1, JsonElement sent) = await PostAsync(_anonymous, $"{Auth}/login", new { nsgNumber = SuperNsg, password = Password });
        Assert.Equal(HttpStatusCode.OK, step1);
        Assert.False(sent.GetProperty("data").TryGetProperty("accessToken", out _));
        Guid reference = sent.GetProperty("data").GetProperty("otpReferenceId").GetGuid();

        await using (AppDbContext ctx = _factory.CreateContext())
        {
            OtpRequest otp = await ctx.OtpRequests.AsNoTracking().SingleAsync(o => o.ReferenceId == reference);
            Assert.Equal("+91" + SuperMobile, otp.MobileNumber);
            Assert.Equal(OtpPurpose.AdminLogin, otp.Purpose);
        }

        // Wrong password, wrong OTP and a valid OTP with the wrong password all fail.
        Assert.Equal(HttpStatusCode.Unauthorized, (await PostAsync(_anonymous, $"{Auth}/login", new { nsgNumber = SuperNsg, password = "Wrong!Passw0rd#9" })).Status);
        Assert.Equal(HttpStatusCode.BadRequest, (await PostAsync(_anonymous, $"{Auth}/login/verify", new { nsgNumber = SuperNsg, password = Password, otpReferenceId = reference, otpCode = "000000" })).Status);
        Assert.Equal(HttpStatusCode.Unauthorized, (await PostAsync(_anonymous, $"{Auth}/login/verify", new { nsgNumber = SuperNsg, password = "Wrong!Passw0rd#9", otpReferenceId = reference, otpCode = DemoOtp })).Status);

        (HttpStatusCode step2, JsonElement tokens) = await PostAsync(_anonymous, $"{Auth}/login/verify", new { nsgNumber = SuperNsg, password = Password, otpReferenceId = reference, otpCode = DemoOtp });
        Assert.Equal(HttpStatusCode.OK, step2);
        JsonElement data = tokens.GetProperty("data");
        Assert.Equal("SUPER_ADMIN", data.GetProperty("profile").GetProperty("roleCode").GetString());

        JwtSecurityToken jwt = new JwtSecurityTokenHandler().ReadJwtToken(data.GetProperty("accessToken").GetString());
        Assert.Contains(jwt.Audiences, a => a.EndsWith(".Admin", StringComparison.Ordinal));
        Assert.Equal(DomainConstants.AdminClaims.AdminAccess, jwt.Claims.Single(c => c.Type == "token_use").Value);
        Assert.StartsWith("admin:", jwt.Subject);
        Assert.DoesNotContain(jwt.Claims, c => c.Type == DomainConstants.MobileClaims.ProfileId);
        Assert.DoesNotContain(jwt.Claims, c => c.Type.Contains("password", StringComparison.OrdinalIgnoreCase));

        await using AppDbContext after = _factory.CreateContext();
        AdminProfile profile = await after.AdminProfiles.AsNoTracking().SingleAsync();
        Assert.NotNull(profile.LastLoginAtIst);
        Assert.Equal(0, profile.FailedLoginAttempts);

        // Only the hash of the refresh token is stored.
        AdminRefreshToken stored = await after.AdminRefreshTokens.AsNoTracking().SingleAsync();
        Assert.NotEqual(data.GetProperty("refreshToken").GetString(), stored.TokenHash);
    }

    [Fact]
    public async Task A_pending_account_cannot_sign_in_and_gets_the_same_answer_as_an_unknown_one()
    {
        await SeedPisAsync(SuperNsg, SuperMobile, "Super Person");
        await BootstrapAsync(SuperNsg);

        (HttpStatusCode pending, JsonElement pendingBody) = await PostAsync(_anonymous, $"{Auth}/login", new { nsgNumber = SuperNsg, password = Password });
        (HttpStatusCode unknown, JsonElement unknownBody) = await PostAsync(_anonymous, $"{Auth}/login", new { nsgNumber = "NOBODY", password = Password });

        Assert.Equal(HttpStatusCode.Unauthorized, pending);
        Assert.Equal(HttpStatusCode.Unauthorized, unknown);
        Assert.Equal(Detail(pendingBody), Detail(unknownBody));
        await using AppDbContext ctx = _factory.CreateContext();
        Assert.Empty(await ctx.OtpRequests.ToListAsync());
        Assert.Empty(await ctx.AdminRefreshTokens.ToListAsync());
    }

    [Fact]
    public async Task Repeated_wrong_passwords_lock_the_account()
    {
        await SeedPisAsync(SuperNsg, SuperMobile, "Super Person");
        await BootstrapAsync(SuperNsg);
        await ActivateAsync(SuperNsg, SuperMobile);

        HttpStatusCode last = HttpStatusCode.OK;
        for (int i = 0; i < 5; i++)
            last = (await PostAsync(_anonymous, $"{Auth}/login", new { nsgNumber = SuperNsg, password = "Wrong!Passw0rd#9" })).Status;
        Assert.Equal(HttpStatusCode.Locked, last);

        // Locked: even the right password is refused until the lockout ends.
        (HttpStatusCode locked, JsonElement body) = await PostAsync(_anonymous, $"{Auth}/login", new { nsgNumber = SuperNsg, password = Password });
        Assert.Equal(HttpStatusCode.Locked, locked);
        Assert.Contains("locked", Detail(body));
    }

    // ───────────────────────── sessions ─────────────────────────

    [Fact]
    public async Task Refresh_rotates_without_an_otp_reuse_ends_the_session_and_logout_ends_it_too()
    {
        await SeedPisAsync(SuperNsg, SuperMobile, "Super Person");
        await BootstrapAsync(SuperNsg);
        await ActivateAsync(SuperNsg, SuperMobile);
        JsonElement first = await SignInAsync(SuperNsg);
        string refresh1 = first.GetProperty("refreshToken").GetString()!;

        int otpsBefore;
        await using (AppDbContext ctx = _factory.CreateContext())
            otpsBefore = await ctx.OtpRequests.CountAsync();

        (HttpStatusCode refreshed, JsonElement second) = await PostAsync(_anonymous, $"{Auth}/token/refresh", new { refreshToken = refresh1 });
        Assert.Equal(HttpStatusCode.OK, refreshed);
        string refresh2 = second.GetProperty("data").GetProperty("refreshToken").GetString()!;
        string access2 = second.GetProperty("data").GetProperty("accessToken").GetString()!;
        Assert.NotEqual(refresh1, refresh2);
        await using (AppDbContext ctx = _factory.CreateContext())
            Assert.Equal(otpsBefore, await ctx.OtpRequests.CountAsync());   // a refresh needs and sends no OTP

        Assert.Equal(HttpStatusCode.OK, (await GetAsync(Bearer(access2), $"{Auth}/me")).Status);

        // The used token comes back: theft signal, the whole session dies, including the newest token and the access token.
        (HttpStatusCode reused, _) = await PostAsync(_anonymous, $"{Auth}/token/refresh", new { refreshToken = refresh1 });
        Assert.Equal(HttpStatusCode.Unauthorized, reused);
        Assert.Equal(HttpStatusCode.Unauthorized, (await PostAsync(_anonymous, $"{Auth}/token/refresh", new { refreshToken = refresh2 })).Status);
        Assert.Equal(HttpStatusCode.Unauthorized, (await GetAsync(Bearer(access2), $"{Auth}/me")).Status);

        // New sign-in, then logout.
        JsonElement third = await SignInAsync(SuperNsg);
        string refresh3 = third.GetProperty("refreshToken").GetString()!, access3 = third.GetProperty("accessToken").GetString()!;
        Assert.Equal(HttpStatusCode.OK, (await GetAsync(Bearer(access3), $"{Auth}/me")).Status);

        (HttpStatusCode loggedOut, JsonElement logoutBody) = await PostAsync(_anonymous, $"{Auth}/logout", new { refreshToken = refresh3 });
        Assert.Equal(HttpStatusCode.OK, loggedOut);
        Assert.Equal("You have been signed out.", logoutBody.GetProperty("message").GetString());
        Assert.Equal(HttpStatusCode.Unauthorized, (await PostAsync(_anonymous, $"{Auth}/token/refresh", new { refreshToken = refresh3 })).Status);
        Assert.Equal(HttpStatusCode.Unauthorized, (await GetAsync(Bearer(access3), $"{Auth}/me")).Status);

        // Logging out again, or with an unknown token, still answers with a message.
        Assert.Equal(HttpStatusCode.OK, (await PostAsync(_anonymous, $"{Auth}/logout", new { refreshToken = "unknown-token" })).Status);

        // The next sign-in needs an OTP again: step 1 sends one and issues no token.
        await AgeOtpsAsync();
        (HttpStatusCode again, JsonElement againBody) = await PostAsync(_anonymous, $"{Auth}/login", new { nsgNumber = SuperNsg, password = Password });
        Assert.Equal(HttpStatusCode.OK, again);
        Assert.False(againBody.GetProperty("data").TryGetProperty("accessToken", out _));
    }

    [Fact]
    public async Task A_new_sign_in_ends_the_previous_session()
    {
        await SeedPisAsync(SuperNsg, SuperMobile, "Super Person");
        await BootstrapAsync(SuperNsg);
        await ActivateAsync(SuperNsg, SuperMobile);

        JsonElement old = await SignInAsync(SuperNsg);
        JsonElement current = await SignInAsync(SuperNsg);

        Assert.Equal(HttpStatusCode.Unauthorized, (await GetAsync(Bearer(old.GetProperty("accessToken").GetString()!), $"{Auth}/me")).Status);
        Assert.Equal(HttpStatusCode.OK, (await GetAsync(Bearer(current.GetProperty("accessToken").GetString()!), $"{Auth}/me")).Status);
    }

    // ───────────────────────── authorization and token separation ─────────────────────────

    [Fact]
    public async Task Roles_are_enforced_and_admin_and_mobile_tokens_are_not_interchangeable()
    {
        HttpClient superAdmin = await SuperAdminClientAsync();
        await SeedPisAsync(PisNsg, PisMobile, "Pis Person");
        await CreateAccountAsync(superAdmin, PisNsg, PisMobile, AdminRoleCodes.PisAdmin);
        await ActivateAsync(PisNsg, PisMobile);
        HttpClient pisAdmin = Bearer((await SignInAsync(PisNsg)).GetProperty("accessToken").GetString()!);

        // Any admin may read their own identity; only a Super Admin manages accounts.
        Assert.Equal(HttpStatusCode.OK, (await GetAsync(pisAdmin, $"{Auth}/me")).Status);
        Assert.Equal(HttpStatusCode.Forbidden, (await GetAsync(pisAdmin, Accounts)).Status);
        Assert.Equal(HttpStatusCode.Forbidden, (await PostAsync(pisAdmin, Accounts, new { nsgNumber = OtherNsg, mobileNumber = OtherMobile, dateOfBirth = Dob, roleCode = "SUPER_ADMIN" })).Status);
        Assert.Equal(HttpStatusCode.Forbidden, (await GetAsync(pisAdmin, "/api/v1/admin/roles")).Status);
        Assert.Equal(HttpStatusCode.OK, (await GetAsync(superAdmin, Accounts)).Status);

        // No token at all.
        Assert.Equal(HttpStatusCode.Unauthorized, (await GetAsync(_anonymous, Accounts)).Status);
        Assert.Equal(HttpStatusCode.Unauthorized, (await GetAsync(_anonymous, $"{Auth}/me")).Status);

        // A mobile-audience token, even one carrying an admin-looking role, is refused by every admin endpoint.
        foreach (string role in new[] { "SUPER_ADMIN", DomainConstants.Roles.SuperAdmin, DomainConstants.Roles.PisAdmin })
        {
            HttpClient mobileLike = _factory.CreateClientFor(role);
            Assert.Equal(HttpStatusCode.Unauthorized, (await GetAsync(mobileLike, Accounts)).Status);
            Assert.Equal(HttpStatusCode.Unauthorized, (await GetAsync(mobileLike, $"{Auth}/me")).Status);
        }

        // An admin token, even a Super Admin's, is refused by mobile-profile and staff endpoints.
        Assert.Equal(HttpStatusCode.Unauthorized, (await GetAsync(superAdmin, "/api/v1/profile")).Status);
        Assert.Equal(HttpStatusCode.Forbidden, (await GetAsync(superAdmin, "/api/v1/onboarding/pending")).Status);
        // Mobile approval is an admin-portal endpoint restricted to REGISTRATION_ADMIN: a Super Admin is authenticated but lacks the role.
        Assert.Equal(HttpStatusCode.Forbidden, (await GetAsync(superAdmin, "/api/v1/mobile-approval-requests")).Status);
        Assert.Equal(HttpStatusCode.Forbidden, (await GetAsync(pisAdmin, "/api/v1/mobile-approval-requests")).Status);

        // PIS lookups are accessible to PIS_ADMIN, while a Super Admin lacks the role.
        Assert.Equal(HttpStatusCode.Forbidden, (await GetAsync(superAdmin, "/api/v1/ranks")).Status);
        Assert.Equal(HttpStatusCode.OK, (await GetAsync(pisAdmin, "/api/v1/ranks")).Status);
    }

    // ───────────────────────── mobile approval decisions ─────────────────────────

    private const string RegNsg = "ADM-0004", RegMobile = "9811110004", Decision = "/api/v1/mobile-approval-requests";

    private async Task<(HttpStatusCode Status, JsonElement Body)> PutAsync(HttpClient client, string url, object body)
    {
        HttpResponseMessage response = await client.PutAsync(url, Json(body));
        return await ReadAsync(response, "PUT " + url);
    }

    private async Task<long> SeedPendingRequestAsync(string nsg, string mobile)
    {
        await SeedPisAsync(nsg, mobile, "Applicant " + nsg);
        await using AppDbContext ctx = _factory.CreateContext();
        ApproveMobileUser request = new() { NsgNumber = nsg, MobileNumber = mobile, DateOfBirth = DateOnly.Parse(Dob) };
        ctx.ApproveMobileUsers.Add(request);
        await ctx.SaveChangesAsync();
        return request.Id;
    }

    [Fact]
    public async Task Registration_admin_can_approve_and_reject_and_their_admin_id_is_stored_in_approved_by_and_the_audit()
    {
        HttpClient superAdmin = await SuperAdminClientAsync();
        await SeedPisAsync(RegNsg, RegMobile, "Registration Person");
        await CreateAccountAsync(superAdmin, RegNsg, RegMobile, AdminRoleCodes.RegistrationAdmin);
        await ActivateAsync(RegNsg, RegMobile);
        string accessToken = (await SignInAsync(RegNsg)).GetProperty("accessToken").GetString()!;
        HttpClient registrationAdmin = Bearer(accessToken);
        int adminProfileId = int.Parse(new JwtSecurityTokenHandler().ReadJwtToken(accessToken).Claims.Single(c => c.Type == DomainConstants.AdminClaims.ProfileId).Value);

        // The list works for the role.
        Assert.Equal(HttpStatusCode.OK, (await GetAsync(registrationAdmin, Decision)).Status);

        long toApprove = await SeedPendingRequestAsync("REQ-0001", "9811110011");
        long toReject = await SeedPendingRequestAsync("REQ-0002", "9811110012");

        (HttpStatusCode approveStatus, JsonElement approved) = await PutAsync(registrationAdmin, $"{Decision}/{toApprove}/decision", new { decision = "Approved", remarks = "ok" });
        Assert.Equal(HttpStatusCode.OK, approveStatus);
        Assert.Equal("Mobile approval request approved successfully.", approved.GetProperty("message").GetString());
        Assert.Equal(adminProfileId, approved.GetProperty("data").GetProperty("reviewedBy").GetInt32());

        (HttpStatusCode rejectStatus, JsonElement rejected) = await PutAsync(registrationAdmin, $"{Decision}/{toReject}/decision", new { decision = "Rejected" });
        Assert.Equal(HttpStatusCode.OK, rejectStatus);
        Assert.Equal("Mobile approval request rejected successfully.", rejected.GetProperty("message").GetString());

        await using AppDbContext ctx = _factory.CreateContext();
        ApproveMobileUser approvedRow = await ctx.ApproveMobileUsers.AsNoTracking().SingleAsync(r => r.Id == toApprove);
        ApproveMobileUser rejectedRow = await ctx.ApproveMobileUsers.AsNoTracking().SingleAsync(r => r.Id == toReject);
        Assert.Equal(MobileAppActivationStatus.Approved, approvedRow.ApprovalStatus);
        Assert.Equal(adminProfileId, approvedRow.ApprovedBy);
        Assert.NotNull(approvedRow.ApprovedAtIst);
        Assert.Equal(MobileAppActivationStatus.Rejected, rejectedRow.ApprovalStatus);
        Assert.Equal(adminProfileId, rejectedRow.ApprovedBy);

        Assert.Equal(MobileAppActivationStatus.Approved, (await ctx.UsersPis.AsNoTracking().SingleAsync(p => p.NsgNumber == "REQ-0001")).MobileAppActivationStatus);
        Assert.Equal(MobileAppActivationStatus.Rejected, (await ctx.UsersPis.AsNoTracking().SingleAsync(p => p.NsgNumber == "REQ-0002")).MobileAppActivationStatus);

        AuditLog granted = await ctx.AuditLogs.AsNoTracking().SingleAsync(a => a.Action == AuditAction.ITApprovalGranted && a.EntityId == toApprove.ToString());
        AuditLog denied = await ctx.AuditLogs.AsNoTracking().SingleAsync(a => a.Action == AuditAction.ITApprovalRejected && a.EntityId == toReject.ToString());
        Assert.Equal(adminProfileId, granted.UserId);
        Assert.Equal(adminProfileId, denied.UserId);

        // First decision wins: deciding again is a conflict and changes nothing.
        Assert.Equal(HttpStatusCode.Conflict, (await PutAsync(registrationAdmin, $"{Decision}/{toApprove}/decision", new { decision = "Rejected" })).Status);
    }

    [Fact]
    public async Task Only_a_registration_admin_may_decide_every_other_caller_is_refused_and_nothing_changes()
    {
        HttpClient superAdmin = await SuperAdminClientAsync();
        await SeedPisAsync(PisNsg, PisMobile, "Pis Person");
        await CreateAccountAsync(superAdmin, PisNsg, PisMobile, AdminRoleCodes.PisAdmin);
        await ActivateAsync(PisNsg, PisMobile);
        HttpClient pisAdmin = Bearer((await SignInAsync(PisNsg)).GetProperty("accessToken").GetString()!);

        long requestId = await SeedPendingRequestAsync("REQ-0003", "9811110013");
        string url = $"{Decision}/{requestId}/decision";
        object body = new { decision = "Approved" };

        Assert.Equal(HttpStatusCode.Forbidden, (await PutAsync(superAdmin, url, body)).Status);
        Assert.Equal(HttpStatusCode.Forbidden, (await PutAsync(pisAdmin, url, body)).Status);
        Assert.Equal(HttpStatusCode.Unauthorized, (await PutAsync(_anonymous, url, body)).Status);
        foreach (string role in new[] { DomainConstants.Roles.PisAdmin, DomainConstants.Roles.SuperAdmin, AdminRoleCodes.RegistrationAdmin })
            Assert.Equal(HttpStatusCode.Unauthorized, (await PutAsync(_factory.CreateClientFor(role), url, body)).Status); // staff/mobile-audience tokens, even with the role name

        await using AppDbContext ctx = _factory.CreateContext();
        ApproveMobileUser row = await ctx.ApproveMobileUsers.AsNoTracking().SingleAsync(r => r.Id == requestId);
        Assert.Equal(MobileAppActivationStatus.Pending, row.ApprovalStatus);
        Assert.Null(row.ApprovedBy);
        Assert.False(await ctx.AuditLogs.AnyAsync(a => a.Action == AuditAction.ITApprovalGranted || a.Action == AuditAction.ITApprovalRejected));
    }

    // ───────────────────────── deactivation ─────────────────────────

    [Fact]
    public async Task Deactivation_blocks_every_path_at_once_and_is_idempotent_and_reversible()
    {
        HttpClient superAdmin = await SuperAdminClientAsync();
        await SeedPisAsync(PisNsg, PisMobile, "Pis Person");
        long pisId = await CreateAccountAsync(superAdmin, PisNsg, PisMobile, AdminRoleCodes.PisAdmin);
        await ActivateAsync(PisNsg, PisMobile);
        JsonElement session = await SignInAsync(PisNsg);
        HttpClient pisAdmin = Bearer(session.GetProperty("accessToken").GetString()!);
        string refresh = session.GetProperty("refreshToken").GetString()!;
        Assert.Equal(HttpStatusCode.OK, (await GetAsync(pisAdmin, $"{Auth}/me")).Status);

        (HttpStatusCode deactivated, JsonElement body) = await PostAsync(superAdmin, $"{Accounts}/{pisId}/deactivate");
        Assert.Equal(HttpStatusCode.OK, deactivated);
        Assert.Equal("Admin account deactivated successfully.", body.GetProperty("message").GetString());
        Assert.False(body.GetProperty("data").GetProperty("isActive").GetBoolean());

        // The live access token, the refresh token and a fresh sign-in all stop working.
        Assert.Equal(HttpStatusCode.Unauthorized, (await GetAsync(pisAdmin, $"{Auth}/me")).Status);
        Assert.Equal(HttpStatusCode.Unauthorized, (await PostAsync(_anonymous, $"{Auth}/token/refresh", new { refreshToken = refresh })).Status);
        await AgeOtpsAsync();
        int otpsBeforeLogin;
        await using (AppDbContext before = _factory.CreateContext())
            otpsBeforeLogin = await before.OtpRequests.CountAsync();
        (HttpStatusCode login, _) = await PostAsync(_anonymous, $"{Auth}/login", new { nsgNumber = PisNsg, password = Password });
        Assert.Equal(HttpStatusCode.Forbidden, login);
        await using (AppDbContext ctx = _factory.CreateContext())
            Assert.Equal(otpsBeforeLogin, await ctx.OtpRequests.CountAsync());   // no OTP is sent for a disabled account

        // Repeating answers with a message; the Super Admin cannot deactivate themself.
        (HttpStatusCode again, JsonElement againBody) = await PostAsync(superAdmin, $"{Accounts}/{pisId}/deactivate");
        Assert.Equal(HttpStatusCode.OK, again);
        Assert.Equal("Admin account is already inactive.", againBody.GetProperty("message").GetString());
        long superId = (await GetAsync(superAdmin, $"{Auth}/me")).Body.GetProperty("data").GetProperty("id").GetInt64();
        Assert.Equal(HttpStatusCode.Conflict, (await PostAsync(superAdmin, $"{Accounts}/{superId}/deactivate")).Status);
        Assert.Equal(HttpStatusCode.NotFound, (await PostAsync(superAdmin, $"{Accounts}/987654/deactivate")).Status);

        // Reactivation restores sign-in (a new session, with OTP).
        (HttpStatusCode reactivated, JsonElement reBody) = await PostAsync(superAdmin, $"{Accounts}/{pisId}/reactivate");
        Assert.Equal(HttpStatusCode.OK, reactivated);
        Assert.Equal("Admin account reactivated successfully.", reBody.GetProperty("message").GetString());
        JsonElement back = await SignInAsync(PisNsg);
        Assert.Equal(HttpStatusCode.OK, (await GetAsync(Bearer(back.GetProperty("accessToken").GetString()!), $"{Auth}/me")).Status);
    }

    [Fact]
    public async Task Audit_records_the_admin_events_without_secrets()
    {
        HttpClient superAdmin = await SuperAdminClientAsync();
        await SeedPisAsync(PisNsg, PisMobile, "Pis Person");
        await CreateAccountAsync(superAdmin, PisNsg, PisMobile, AdminRoleCodes.PisAdmin);

        await using AppDbContext ctx = _factory.CreateContext();
        List<AuditLog> logs = await ctx.AuditLogs.AsNoTracking().ToListAsync();
        Assert.Contains(logs, l => l.Action == AuditAction.LoginSuccess && l.EntityType == nameof(AdminProfile));
        Assert.Contains(logs, l => l.Action == AuditAction.AdminAction && (l.AdditionalData ?? "").Contains("AdminAccountCreated"));
        Assert.Contains(logs, l => l.Action == AuditAction.AdminAction && (l.AdditionalData ?? "").Contains("SuperAdminBootstrapped"));

        string everything = string.Join(" ", logs.Select(l => l.AdditionalData));
        Assert.DoesNotContain(Password, everything);
        Assert.DoesNotContain(DemoOtp, everything);
        Assert.DoesNotContain(SuperMobile, everything);
    }
}
