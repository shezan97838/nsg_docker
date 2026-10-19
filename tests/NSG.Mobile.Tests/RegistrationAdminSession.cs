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
using NSG.Mobile.Infrastructure.Persistence;

namespace NSG.Mobile.Tests;

/// <summary>
/// Real-MySQL helper: signs a genuine Registration Admin into the admin portal (bootstrap Super Admin, create and activate a
/// REGISTRATION_ADMIN account, password + demo-OTP sign-in) and returns a client carrying the admin access token. Mobile approval is
/// admin-portal only, so a fake staff token is refused (401). Leaves no audit or OTP rows behind; the caller's cleanup must delete
/// <c>admin_refresh_tokens</c> and <c>admin_profiles</c> before <c>users_pis</c>.
/// </summary>
internal static class RegistrationAdminSession
{
    private const string Auth = "/api/v1/admin/auth", Activation = "/api/v1/admin/activation", Accounts = "/api/v1/admin/accounts";
    private const string DemoOtp = "123456", Password = "Str0ng!Passw0rd#1", Dob = "1985-06-15";
    private const string SuperNsg = "RAS-0001", SuperMobile = "9800000001", RegNsg = "RAS-0002", RegMobile = "9800000002";

    public static async Task<HttpClient> CreateAsync(ApiFactory factory)
    {
        await using (AppDbContext ctx = factory.CreateContext())
        {
            await ctx.Database.ExecuteSqlRawAsync("DELETE FROM `admin_refresh_tokens`");
            await ctx.Database.ExecuteSqlRawAsync("DELETE FROM `admin_profiles`");
            await ctx.Database.ExecuteSqlRawAsync("DELETE FROM `users_pis` WHERE `nsg_number` IN ('" + SuperNsg + "', '" + RegNsg + "')");
            ctx.UsersPis.Add(new UserPis { NsgNumber = SuperNsg, ServiceNumber = "S-" + SuperNsg, FullName = "Super Person", MobileNumber = SuperMobile, DateOfBirth = DateOnly.Parse(Dob), IsActive = true });
            ctx.UsersPis.Add(new UserPis { NsgNumber = RegNsg, ServiceNumber = "S-" + RegNsg, FullName = "Registration Person", MobileNumber = RegMobile, DateOfBirth = DateOnly.Parse(Dob), IsActive = true });
            await ctx.SaveChangesAsync();
        }

        await using (AsyncServiceScope scope = factory.Services.CreateAsyncScope())
        {
            Result<long> bootstrap = await scope.ServiceProvider.GetRequiredService<IAdminBootstrapService>().CreateFirstSuperAdminAsync(SuperNsg);
            Assert.True(bootstrap.IsSuccess, bootstrap.ErrorMessage);
        }

        HttpClient anonymous = factory.CreateClientFor(null);
        await ActivateAsync(factory, anonymous, SuperNsg, SuperMobile);
        HttpClient superAdmin = Bearer(factory, await SignInAsync(factory, anonymous, SuperNsg));

        HttpResponseMessage created = await superAdmin.PostAsync(Accounts, Json(new { nsgNumber = RegNsg, mobileNumber = RegMobile, dateOfBirth = Dob, roleCode = AdminRoleCodes.RegistrationAdmin }));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        await ActivateAsync(factory, anonymous, RegNsg, RegMobile);
        HttpClient registrationAdmin = Bearer(factory, await SignInAsync(factory, anonymous, RegNsg));

        // The set-up is not part of any test's audit or OTP expectations.
        await using AppDbContext cleanup = factory.CreateContext();
        await cleanup.Database.ExecuteSqlRawAsync("DELETE FROM `audit_logs`");
        await cleanup.Database.ExecuteSqlRawAsync("DELETE FROM `otp_requests`");
        return registrationAdmin;
    }

    private static StringContent Json(object body) => new(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

    private static HttpClient Bearer(ApiFactory factory, string accessToken)
    {
        HttpClient client = factory.CreateClientFor(null);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return client;
    }

    private static async Task AgeOtpsAsync(ApiFactory factory)
    {
        await using AppDbContext ctx = factory.CreateContext();
        await ctx.Database.ExecuteSqlRawAsync("UPDATE `otp_requests` SET `created_at_ist` = `created_at_ist` - INTERVAL 3 HOUR");
    }

    private static async Task<JsonElement> PostOkAsync(HttpClient client, string url, object body)
    {
        HttpResponseMessage response = await client.PostAsync(url, Json(body));
        string text = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.OK, $"POST {url} returned {(int)response.StatusCode}: {text}");
        return JsonDocument.Parse(text).RootElement.Clone();
    }

    private static async Task ActivateAsync(ApiFactory factory, HttpClient anonymous, string nsg, string mobile)
    {
        await AgeOtpsAsync(factory);
        JsonElement sent = await PostOkAsync(anonymous, $"{Activation}/otp", new { nsgNumber = nsg, mobileNumber = mobile, dateOfBirth = Dob });
        Guid reference = sent.GetProperty("data").GetProperty("otpReferenceId").GetGuid();
        await PostOkAsync(anonymous, $"{Activation}/complete", new
        {
            nsgNumber = nsg, mobileNumber = mobile, dateOfBirth = Dob, otpReferenceId = reference, otpCode = DemoOtp,
            newPassword = Password, confirmPassword = Password
        });
    }

    private static async Task<string> SignInAsync(ApiFactory factory, HttpClient anonymous, string nsg)
    {
        await AgeOtpsAsync(factory);
        JsonElement sent = await PostOkAsync(anonymous, $"{Auth}/login", new { nsgNumber = nsg, password = Password });
        Guid reference = sent.GetProperty("data").GetProperty("otpReferenceId").GetGuid();
        JsonElement tokens = await PostOkAsync(anonymous, $"{Auth}/login/verify", new { nsgNumber = nsg, password = Password, otpReferenceId = reference, otpCode = DemoOtp });
        return tokens.GetProperty("data").GetProperty("accessToken").GetString()!;
    }
}
