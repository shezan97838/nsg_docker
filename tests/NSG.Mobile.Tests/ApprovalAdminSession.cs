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

/// <summary>Genuine Admin Portal sessions for the registration-approval tests. <see cref="PisA"/>/<see cref="PisB"/> are PIS_ADMIN accounts.</summary>
internal sealed record ApprovalAdmins(HttpClient PisA, long PisAId, HttpClient PisB, long PisBId, HttpClient Super, HttpClient Registration);

/// <summary>
/// Real-MySQL helper: signs genuine admins into the admin portal (bootstrap Super Admin, create and activate two PIS_ADMIN accounts and one
/// REGISTRATION_ADMIN account, password + demo-OTP sign-in). Mobile approval is PIS-Admin-only and admin-portal only, so a fake staff token
/// is refused (401). Leaves no audit or OTP rows behind; the caller's cleanup must delete <c>admin_refresh_tokens</c> and
/// <c>admin_profiles</c> before <c>users_pis</c>.
/// </summary>
internal static class ApprovalAdminSession
{
    private const string Auth = "/api/v1/admin/auth", Activation = "/api/v1/admin/activation", Accounts = "/api/v1/admin/accounts";
    private const string DemoOtp = "123456", Password = "Str0ng!Passw0rd#1", Dob = "1985-06-15";
    private static readonly (string Nsg, string Mobile)[] People =
    {
        ("RAS-0001", "9800000001"), ("RAS-0002", "9800000002"), ("RAS-0003", "9800000003"), ("RAS-0004", "9800000004")
    };

    public static async Task<ApprovalAdmins> CreateAsync(ApiFactory factory)
    {
        await using (AppDbContext ctx = factory.CreateContext())
        {
            await ctx.Database.ExecuteSqlRawAsync("DELETE FROM `admin_refresh_tokens`");
            await ctx.Database.ExecuteSqlRawAsync("DELETE FROM `admin_profiles`");
            foreach ((string nsg, string mobile) in People)
            {
                await ctx.Database.ExecuteSqlRawAsync("DELETE FROM `users_pis` WHERE `nsg_number` = {0}", nsg);
                ctx.UsersPis.Add(new UserPis { NsgNumber = nsg, ServiceNumber = "S-" + nsg, FullName = "Admin " + nsg, MobileNumber = mobile, DateOfBirth = DateOnly.Parse(Dob), IsActive = true });
            }
            await ctx.SaveChangesAsync();
        }

        await using (AsyncServiceScope scope = factory.Services.CreateAsyncScope())
        {
            Result<long> bootstrap = await scope.ServiceProvider.GetRequiredService<IAdminBootstrapService>().CreateFirstSuperAdminAsync(People[0].Nsg);
            Assert.True(bootstrap.IsSuccess, bootstrap.ErrorMessage);
        }

        HttpClient anonymous = factory.CreateClientFor(null);
        await ActivateAsync(factory, anonymous, People[0].Nsg, People[0].Mobile);
        HttpClient superAdmin = Bearer(factory, await SignInAsync(factory, anonymous, People[0].Nsg));

        string[] roles = { AdminRoleCodes.PisAdmin, AdminRoleCodes.PisAdmin, AdminRoleCodes.RegistrationAdmin };
        List<HttpClient> clients = new();
        for (int i = 1; i <= 3; i++)
        {
            HttpResponseMessage created = await superAdmin.PostAsync(Accounts, Json(new { nsgNumber = People[i].Nsg, mobileNumber = People[i].Mobile, dateOfBirth = Dob, roleCode = roles[i - 1] }));
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
            await ActivateAsync(factory, anonymous, People[i].Nsg, People[i].Mobile);
            clients.Add(Bearer(factory, await SignInAsync(factory, anonymous, People[i].Nsg)));
        }

        // The set-up is not part of any test's audit or OTP expectations.
        await using AppDbContext cleanup = factory.CreateContext();
        long a = await cleanup.AdminProfiles.AsNoTracking().Where(p => p.NsgNumber == People[1].Nsg).Select(p => p.Id).SingleAsync();
        long b = await cleanup.AdminProfiles.AsNoTracking().Where(p => p.NsgNumber == People[2].Nsg).Select(p => p.Id).SingleAsync();
        await cleanup.Database.ExecuteSqlRawAsync("DELETE FROM `audit_logs`");
        await cleanup.Database.ExecuteSqlRawAsync("DELETE FROM `otp_requests`");
        return new ApprovalAdmins(clients[0], a, clients[1], b, superAdmin, clients[2]);
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
