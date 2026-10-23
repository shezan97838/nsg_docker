using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using NSG.Mobile.Application.Interfaces.Repositories;
using NSG.Mobile.Domain.Constants;
using Microsoft.EntityFrameworkCore;
using NSG.Mobile.Domain.Common;
using NSG.Mobile.Domain.Entities;
using NSG.Mobile.Infrastructure.Persistence;

namespace NSG.Mobile.Tests;

/// <summary>
/// DATABASE-BACKED INTEGRATION TESTS (real MySQL, real host). They run only when NSG_TEST_MYSQL names a database ending in "_test"
/// (see ApiFactory, which recreates it); without it the whole database collection fails to start, as for the other MySQL tests.
/// Covers the Hindi (Devanagari) round trip through the real <c>message_hindi</c> column and the TEMPORARY anonymous admin writes.
/// </summary>
[Collection("mysql")]
public class DgMessageHindiDatabaseTests : IAsyncLifetime
{
    private const string Hindi = "एनएसजी मोबाइल एप्लिकेशन में आपका स्वागत है।";

    private readonly ApiFactory _factory;

    public DgMessageHindiDatabaseTests(DatabaseFixture fixture) => _factory = fixture.Factory;

    public async Task InitializeAsync() => await CleanAsync();

    public async Task DisposeAsync() => await CleanAsync();

    private async Task CleanAsync()
    {
        await using AppDbContext ctx = _factory.CreateContext();
        await ctx.Database.ExecuteSqlRawAsync("DELETE FROM `dg_message`");
        await ctx.Database.ExecuteSqlRawAsync("DELETE FROM `audit_logs`");
    }

    private static MultipartFormDataContent Form(string message, string? hindi, bool? isActive = null)
    {
        MultipartFormDataContent form = new()
        {
            { new StringContent("Shri A. B. Singh"), "dgName" },
            { new StringContent("Director General"), "designation" },
            { new StringContent(message), "message" }
        };
        if (hindi != null) form.Add(new StringContent(hindi), "messageHindi");
        if (isActive != null) form.Add(new StringContent(isActive.Value ? "true" : "false"), "isActive");
        return form;
    }

    [Fact]
    public async Task Anonymous_post_put_and_delete_work_and_hindi_survives_the_mysql_round_trip()
    {
        HttpClient anonymous = _factory.CreateClientFor(null);

        HttpResponseMessage created = await anonymous.PostAsync("/api/v1/admin/dg-messages", Form("Welcome to the NSG Mobile Application.", Hindi));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        System.Text.Json.JsonElement data = await created.ReadDataAsync();
        int id = data.GetProperty("id").GetInt32();
        Assert.Equal(Hindi, data.GetProperty("messageHindi").GetString());

        await using (AppDbContext ctx = _factory.CreateContext())
        {
            DgMessage row = await ctx.DgMessages.AsNoTracking().SingleAsync(m => m.Id == id);
            Assert.Equal(Hindi, row.MessageHindi);                           // exact code points, no '?' or U+FFFD
            Assert.DoesNotContain('�', row.MessageHindi);
            Assert.DoesNotContain('?', row.MessageHindi);
            Assert.Equal("Welcome to the NSG Mobile Application.", row.Message);
        }

        string updatedHindi = Hindi + " धन्यवाद।";
        HttpResponseMessage updated = await anonymous.PutAsync($"/api/v1/admin/dg-messages/{id}", Form("Updated English.", updatedHindi, isActive: true));
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        System.Text.Json.JsonElement updatedData = await updated.ReadDataAsync();
        Assert.Equal("Updated English.", updatedData.GetProperty("message").GetString());
        Assert.Equal(updatedHindi, updatedData.GetProperty("messageHindi").GetString());

        HttpResponseMessage deleted = await anonymous.DeleteAsync($"/api/v1/admin/dg-messages/{id}");
        Assert.Equal(HttpStatusCode.OK, deleted.StatusCode);

        await using AppDbContext after = _factory.CreateContext();
        DgMessage soft = await after.DgMessages.AsNoTracking().SingleAsync(m => m.Id == id);   // soft delete: the row stays
        Assert.True(soft.IsDeleted);
        Assert.Equal(updatedHindi, soft.MessageHindi);
    }

    [Fact]
    public async Task A_legacy_row_without_hindi_reads_back_as_null_and_the_table_and_column_are_utf8mb4()
    {
        await using AppDbContext ctx = _factory.CreateContext();
        ctx.DgMessages.Add(new DgMessage { DgName = "Legacy", Designation = "DG", Message = "Old message.", CreatedAtIst = IndianStandardTime.Now });
        await ctx.SaveChangesAsync();

        DgMessage legacy = await ctx.DgMessages.AsNoTracking().SingleAsync();
        Assert.Null(legacy.MessageHindi);

        var charset = await ctx.Database.SqlQueryRaw<string>(
            "SELECT CHARACTER_SET_NAME AS `Value` FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'dg_message' AND COLUMN_NAME = 'message_hindi'").ToListAsync();
        Assert.Equal("utf8mb4", Assert.Single(charset));
    }

    [Fact]
    public async Task Reading_the_list_without_a_token_is_still_401()
    {
        HttpResponseMessage response = await _factory.CreateClientFor(null).GetAsync("/api/v1/dg-messages");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Get_with_a_valid_mobile_token_returns_messageHindi_and_null_for_legacy_rows_inside_the_envelope()
    {
        Guid session = Guid.NewGuid();
        FakeSessionRepository sessions = new();
        sessions.Tokens.Add(new MobileRefreshToken { ProfileId = 501, FamilyId = session });   // only the session lookup is faked; dg_message is real MySQL
        await using (AppDbContext ctx = _factory.CreateContext())
        {
            ctx.DgMessages.Add(new DgMessage { DgName = "Legacy", Designation = "DG", Message = "Old.", CreatedAtIst = IndianStandardTime.Now.AddMinutes(-5) });
            ctx.DgMessages.Add(new DgMessage { DgName = "Hindi", Designation = "DG", Message = "New.", MessageHindi = Hindi, CreatedAtIst = IndianStandardTime.Now });
            await ctx.SaveChangesAsync();
        }

        using WebApplicationFactory<Program> host = _factory.WithWebHostBuilder(b => b.ConfigureServices(s => s.AddSingleton<IMobileRefreshTokenRepository>(sessions)));
        IConfigurationSection jwt = host.Services.GetRequiredService<IConfiguration>().GetSection("Jwt");
        JwtSecurityToken token = new(jwt["Issuer"], jwt["Audience"],
            new[]
            {
                new Claim(DomainConstants.MobileClaims.TokenUse, DomainConstants.MobileClaims.MobileAccess),
                new Claim(DomainConstants.MobileClaims.ProfileId, "501"), new Claim(DomainConstants.MobileClaims.SessionId, session.ToString())
            },
            expires: DateTime.UtcNow.AddMinutes(10),
            signingCredentials: new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt["Secret"]!)), SecurityAlgorithms.HmacSha256));
        HttpClient client = host.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", new JwtSecurityTokenHandler().WriteToken(token));

        HttpResponseMessage response = await client.GetAsync("/api/v1/dg-messages");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        System.Text.Json.JsonElement data = await response.ReadDataAsync();
        System.Text.Json.JsonElement[] items = data.GetProperty("items").EnumerateArray().ToArray();
        Assert.Equal(2, items.Length);
        Assert.Equal(Hindi, items.Single(i => i.GetProperty("dgName").GetString() == "Hindi").GetProperty("messageHindi").GetString());
        Assert.Equal(System.Text.Json.JsonValueKind.Null, items.Single(i => i.GetProperty("dgName").GetString() == "Legacy").GetProperty("messageHindi").ValueKind);
    }
}
