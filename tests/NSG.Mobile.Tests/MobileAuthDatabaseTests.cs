using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NSG.Mobile.Domain.Common;
using NSG.Mobile.Domain.Constants;
using NSG.Mobile.Domain.Entities;
using NSG.Mobile.Domain.Enums;
using NSG.Mobile.Infrastructure.Persistence;

namespace NSG.Mobile.Tests;

/// <summary>
/// Real-MySQL tests for the mobile approval workflow: the generated-column unique index of ADR-014, the unique profile keys,
/// the decision transaction and concurrency, admin authorization, and "no blank response" for the HTTP surface.
/// Needs NSG_TEST_MYSQL (a database named *_test); the shared fixture resets that database through migrations.
/// </summary>
[Collection("mysql")]
public class MobileAuthDatabaseTests : IAsyncLifetime
{
    private const string Approvals = "/api/v1/mobile-approval-requests";
    private const string Auth = "/api/v1/mobile-auth";

    private readonly ApiFactory _factory;
    private readonly HttpClient _admin, _employee, _anonymous;

    public MobileAuthDatabaseTests(DatabaseFixture fixture)
    {
        _factory = fixture.Factory;
        _admin = _factory.CreateClientFor(DomainConstants.Roles.ITAdmin);
        _employee = _factory.CreateClientFor(DomainConstants.Roles.Employee);
        _anonymous = _factory.CreateClientFor(null);
    }

    public async Task InitializeAsync() => await CleanAsync();
    public async Task DisposeAsync() => await CleanAsync();

    private async Task CleanAsync()
    {
        await using AppDbContext ctx = _factory.CreateContext();
        await ctx.Database.ExecuteSqlRawAsync("DELETE FROM `mobile_refresh_tokens`");
        await ctx.Database.ExecuteSqlRawAsync("DELETE FROM `approve_mobile_users`");
        await ctx.Database.ExecuteSqlRawAsync("DELETE FROM `mobile_user_profiles`");
        await ctx.Database.ExecuteSqlRawAsync("DELETE FROM `mobile_auth_grants`");
        await ctx.Database.ExecuteSqlRawAsync("DELETE FROM `users_pis`");
        await ctx.Database.ExecuteSqlRawAsync("DELETE FROM `audit_logs`");
    }

    private static ApproveMobileUser Request(string nsg, MobileAppActivationStatus status = MobileAppActivationStatus.Pending) => new()
    {
        NsgNumber = nsg, MobileNumber = "9876543210", DateOfBirth = new DateOnly(1990, 5, 15), ApprovalStatus = status,
        ApprovedBy = status == MobileAppActivationStatus.Pending ? null : 1,
        ApprovedAtIst = status == MobileAppActivationStatus.Pending ? null : IndianStandardTime.Now
    };

    private async Task<long> SeedPendingAsync(string nsg = "NSG-1")
    {
        await using AppDbContext ctx = _factory.CreateContext();
        ctx.UsersPis.Add(new UserPis { NsgNumber = nsg, ServiceNumber = "S", FullName = "Test Person", MobileNumber = "9876543210", DateOfBirth = new DateOnly(1990, 5, 15) });
        ApproveMobileUser request = Request(nsg);
        ctx.ApproveMobileUsers.Add(request);
        await ctx.SaveChangesAsync();
        return request.Id;
    }

    private static StringContent Json(object body) => new(JsonSerializer.Serialize(body), System.Text.Encoding.UTF8, "application/json");

    // ───── ADR-014: one pending request per NSG number (generated column + unique index) ─────

    [Fact]
    public async Task A_second_pending_request_for_the_same_nsg_number_is_rejected_by_the_database()
    {
        await using AppDbContext ctx = _factory.CreateContext();
        ctx.ApproveMobileUsers.Add(Request("NSG-1"));
        await ctx.SaveChangesAsync();

        await using AppDbContext second = _factory.CreateContext();
        second.ApproveMobileUsers.Add(Request("nsg-1"));   // the collation is case-insensitive
        DbUpdateException ex = await Assert.ThrowsAsync<DbUpdateException>(() => second.SaveChangesAsync());
        Assert.Contains("ix_approve_mobile_users_pending_nsg_number", ex.InnerException!.Message);
    }

    [Fact]
    public async Task History_is_unlimited_and_a_new_pending_request_is_allowed_after_a_decision()
    {
        await using AppDbContext ctx = _factory.CreateContext();
        ctx.ApproveMobileUsers.AddRange(Request("NSG-2", MobileAppActivationStatus.Approved), Request("NSG-2", MobileAppActivationStatus.Rejected),
            Request("NSG-2", MobileAppActivationStatus.Rejected), Request("NSG-2"));
        await ctx.SaveChangesAsync();

        Assert.Equal(4, await ctx.ApproveMobileUsers.CountAsync(a => a.NsgNumber == "NSG-2"));
        Assert.Equal("NSG-2", await ctx.ApproveMobileUsers.Where(a => a.ApprovalStatus == MobileAppActivationStatus.Pending).Select(a => a.PendingNsgNumber).SingleAsync());
        Assert.Equal(3, await ctx.ApproveMobileUsers.CountAsync(a => a.PendingNsgNumber == null));
    }

    [Fact]
    public async Task Profiles_have_unique_nsg_and_mobile_numbers_but_not_unique_dates_of_birth()
    {
        MobileUserProfile Profile(string nsg, string mobile) => new() { NsgNumber = nsg, MobileNumber = mobile, DateOfBirth = new DateOnly(1990, 5, 15), MpinHash = "h" };
        await using (AppDbContext ctx = _factory.CreateContext())
        {
            ctx.MobileUserProfiles.AddRange(Profile("A-1", "9876543210"), Profile("A-2", "9876543211"));
            await ctx.SaveChangesAsync();
        }

        await using AppDbContext dupNsg = _factory.CreateContext();
        dupNsg.MobileUserProfiles.Add(Profile("a-1", "9876543299"));
        Assert.Contains("ix_mobile_user_profiles_nsg_number", (await Assert.ThrowsAsync<DbUpdateException>(() => dupNsg.SaveChangesAsync())).InnerException!.Message);

        await using AppDbContext dupMobile = _factory.CreateContext();
        dupMobile.MobileUserProfiles.Add(Profile("A-3", "9876543210"));
        Assert.Contains("ix_mobile_user_profiles_mobile_number", (await Assert.ThrowsAsync<DbUpdateException>(() => dupMobile.SaveChangesAsync())).InnerException!.Message);
    }

    [Fact]
    public async Task Review_fields_check_rejects_a_decided_request_without_a_reviewer()
    {
        await using AppDbContext ctx = _factory.CreateContext();
        ApproveMobileUser bad = Request("NSG-3", MobileAppActivationStatus.Approved);
        bad.ApprovedBy = null;
        ctx.ApproveMobileUsers.Add(bad);
        await Assert.ThrowsAsync<DbUpdateException>(() => ctx.SaveChangesAsync());
    }

    // ───── decision endpoint ─────

    [Fact]
    public async Task Approving_updates_the_request_and_the_pis_status_together_with_server_side_audit_fields()
    {
        long id = await SeedPendingAsync();
        HttpResponseMessage response = await _admin.PutAsync($"{Approvals}/{id}/decision", Json(new { decision = "Approved", remarks = "ok" }));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using JsonDocument body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("Mobile approval request approved successfully.", body.RootElement.GetProperty("message").GetString());
        Assert.Equal("Approved", body.RootElement.GetProperty("data").GetProperty("status").GetString());

        await using AppDbContext ctx = _factory.CreateContext();
        ApproveMobileUser request = await ctx.ApproveMobileUsers.SingleAsync(a => a.Id == id);
        Assert.Equal(MobileAppActivationStatus.Approved, request.ApprovalStatus);
        Assert.Equal(9001, request.ApprovedBy);            // from the token, never from the body
        Assert.NotNull(request.ApprovedAtIst);
        Assert.Null(request.PendingNsgNumber);
        Assert.Equal(MobileAppActivationStatus.Approved, (await ctx.UsersPis.SingleAsync()).MobileAppActivationStatus);
        Assert.True(await ctx.AuditLogs.AnyAsync(a => a.Action == AuditAction.ITApprovalGranted && a.UserId == 9001));
    }

    [Fact]
    public async Task Rejecting_sets_pis_to_rejected_and_a_repeated_or_conflicting_decision_is_409()
    {
        long id = await SeedPendingAsync();
        Assert.Equal(HttpStatusCode.OK, (await _admin.PutAsync($"{Approvals}/{id}/decision", Json(new { decision = "Rejected" }))).StatusCode);

        foreach (string decision in new[] { "Rejected", "Approved" })
        {
            HttpResponseMessage again = await _admin.PutAsync($"{Approvals}/{id}/decision", Json(new { decision }));
            Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
            Assert.Contains("already been rejected", await again.Content.ReadAsStringAsync());
        }

        await using AppDbContext ctx = _factory.CreateContext();
        Assert.Equal(MobileAppActivationStatus.Rejected, (await ctx.UsersPis.SingleAsync()).MobileAppActivationStatus);
        Assert.Equal(MobileAppActivationStatus.Rejected, (await ctx.ApproveMobileUsers.SingleAsync()).ApprovalStatus);
    }

    [Fact]
    public async Task Simultaneous_decisions_have_exactly_one_winner()
    {
        long id = await SeedPendingAsync();
        HttpResponseMessage[] responses = await Task.WhenAll(
            _admin.PutAsync($"{Approvals}/{id}/decision", Json(new { decision = "Approved" })),
            _admin.PutAsync($"{Approvals}/{id}/decision", Json(new { decision = "Rejected" })),
            _admin.PutAsync($"{Approvals}/{id}/decision", Json(new { decision = "Approved" })));

        Assert.Equal(1, responses.Count(r => r.StatusCode == HttpStatusCode.OK));
        Assert.Equal(2, responses.Count(r => r.StatusCode == HttpStatusCode.Conflict));

        await using AppDbContext ctx = _factory.CreateContext();
        Assert.Equal((await ctx.ApproveMobileUsers.SingleAsync()).ApprovalStatus, (await ctx.UsersPis.SingleAsync()).MobileAppActivationStatus);
    }

    [Fact]
    public async Task A_missing_personnel_record_rolls_the_whole_decision_back()
    {
        long id = await SeedPendingAsync();
        await using (AppDbContext ctx = _factory.CreateContext())
            await ctx.Database.ExecuteSqlRawAsync("DELETE FROM `users_pis`");

        HttpResponseMessage response = await _admin.PutAsync($"{Approvals}/{id}/decision", Json(new { decision = "Approved" }));
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        await using AppDbContext check = _factory.CreateContext();
        ApproveMobileUser request = await check.ApproveMobileUsers.SingleAsync();
        Assert.Equal(MobileAppActivationStatus.Pending, request.ApprovalStatus);
        Assert.Null(request.ApprovedBy);
    }

    [Fact]
    public async Task Decision_input_is_validated_and_unknown_ids_are_404_with_a_message()
    {
        long id = await SeedPendingAsync();
        foreach (object body in new object[] { new { decision = "Pending" }, new { decision = "Maybe" }, new { decision = "Approved", remarks = new string('x', 501) } })
        {
            HttpResponseMessage bad = await _admin.PutAsync($"{Approvals}/{id}/decision", Json(body));
            Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
            Assert.False(string.IsNullOrWhiteSpace(await bad.Content.ReadAsStringAsync()));
        }

        HttpResponseMessage missing = await _admin.PutAsync($"{Approvals}/987654/decision", Json(new { decision = "Approved" }));
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        Assert.Contains("not found", await missing.Content.ReadAsStringAsync());
    }

    // ───── authorization and non-empty bodies ─────

    [Fact]
    public async Task Admin_endpoints_answer_401_and_403_with_a_body()
    {
        long id = await SeedPendingAsync();
        foreach ((HttpClient client, HttpStatusCode expected) in new[] { (_anonymous, HttpStatusCode.Unauthorized), (_employee, HttpStatusCode.Forbidden) })
        {
            HttpResponseMessage list = await client.GetAsync(Approvals);
            HttpResponseMessage decide = await client.PutAsync($"{Approvals}/{id}/decision", Json(new { decision = "Approved" }));
            Assert.Equal(expected, list.StatusCode);
            Assert.Equal(expected, decide.StatusCode);
            Assert.False(string.IsNullOrWhiteSpace(await list.Content.ReadAsStringAsync()));
            Assert.False(string.IsNullOrWhiteSpace(await decide.Content.ReadAsStringAsync()));
        }

        await using AppDbContext ctx = _factory.CreateContext();
        Assert.Equal(MobileAppActivationStatus.Pending, (await ctx.ApproveMobileUsers.SingleAsync()).ApprovalStatus);
    }

    [Fact]
    public async Task List_filters_by_status_searches_and_returns_a_message_instead_of_an_empty_list()
    {
        HttpResponseMessage empty = await _admin.GetAsync(Approvals);
        Assert.Equal(HttpStatusCode.OK, empty.StatusCode);
        Assert.Equal("No mobile approval requests found.", JsonDocument.Parse(await empty.Content.ReadAsStringAsync()).RootElement.GetProperty("message").GetString());

        await SeedPendingAsync("NSG-77");
        JsonElement page = JsonDocument.Parse(await (await _admin.GetAsync($"{Approvals}?status=Pending&search=77")).Content.ReadAsStringAsync()).RootElement;
        Assert.Equal(1, page.GetProperty("totalCount").GetInt32());
        JsonElement item = page.GetProperty("items")[0];
        Assert.Equal("NSG-77", item.GetProperty("nsgNumber").GetString());
        Assert.Equal("Test Person", item.GetProperty("fullName").GetString());
        Assert.Equal("XXXXXX3210", item.GetProperty("maskedMobileNumber").GetString());
        Assert.EndsWith("+05:30", item.GetProperty("requestedAt").GetString());

        JsonElement none = JsonDocument.Parse(await (await _admin.GetAsync($"{Approvals}?status=Approved")).Content.ReadAsStringAsync()).RootElement;
        Assert.True(none.TryGetProperty("message", out _));
        Assert.Equal(HttpStatusCode.BadRequest, (await _admin.GetAsync($"{Approvals}?status=Bogus")).StatusCode);
    }

    [Fact]
    public async Task Public_mobile_auth_endpoints_always_answer_with_a_body()
    {
        // login/check for an unknown person: generic 404 with a message, no account hints.
        HttpResponseMessage unknown = await _anonymous.PostAsync($"{Auth}/login/check", Json(new { nsgNumber = "NOPE", mobileNumber = "9876543210", dateOfBirth = "1990-05-15" }));
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        Assert.Contains("could not find a matching record", await unknown.Content.ReadAsStringAsync());

        // Malformed input: 400 ProblemDetails.
        HttpResponseMessage invalid = await _anonymous.PostAsync($"{Auth}/login/check", Json(new { nsgNumber = "", mobileNumber = "12", dateOfBirth = "1990-05-15" }));
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        Assert.False(string.IsNullOrWhiteSpace(await invalid.Content.ReadAsStringAsync()));

        // Protected endpoint without a token.
        HttpResponseMessage change = await _anonymous.PostAsync($"{Auth}/mpin/change", Json(new { currentMpin = "2580", newMpin = "1357", confirmNewMpin = "1357" }));
        Assert.Equal(HttpStatusCode.Unauthorized, change.StatusCode);
        Assert.False(string.IsNullOrWhiteSpace(await change.Content.ReadAsStringAsync()));

        // A staff token is not a mobile token: mpin/change is refused even though the caller is authenticated.
        HttpResponseMessage staff = await _admin.PostAsync($"{Auth}/mpin/change", Json(new { currentMpin = "2580", newMpin = "1357", confirmNewMpin = "1357" }));
        Assert.Equal(HttpStatusCode.Forbidden, staff.StatusCode);
        Assert.False(string.IsNullOrWhiteSpace(await staff.Content.ReadAsStringAsync()));
    }
}
