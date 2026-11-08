using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSG.Mobile.Application.Interfaces.Repositories;
using NSG.Mobile.Domain.Constants;
using NSG.Mobile.Domain.Entities;
using NSG.Mobile.Domain.Enums;
using NSG.Mobile.Infrastructure.Persistence;
using NSG.Mobile.Infrastructure.Repositories;

namespace NSG.Mobile.Tests;

[CollectionDefinition("mysql-approval-ownership", DisableParallelization = true)]
public sealed class MySqlApprovalOwnershipCollection { }

/// <summary>
/// DATABASE-BACKED tests of registration approval ownership on real MySQL 8 through the real host: PIS_ADMIN-only authorization (staff Bearer,
/// Super Admin, Registration Admin and anonymous callers are refused), the ownership join of the list, the ownership check and row lock of the
/// decision, ownership transfer (a plain UPDATE of <c>users_pis.assigned_to</c>) and the lock/transfer race. They run ONLY when NSG_TEST_MYSQL
/// names a disposable database ending in "_test" (ApiFactory drops and recreates it through the migrations), and are SKIPPED otherwise: in
/// that case none of the transaction, row-lock or concurrency behaviour below has been verified.
/// </summary>
[Collection("mysql-approval-ownership")]
public class MobileApprovalOwnershipDatabaseTests : IAsyncLifetime
{
    private const string Approvals = "/api/v1/mobile-approval-requests";

    private ApiFactory _factory = null!;
    private ApprovalAdmins _admins = null!;
    private int _counter;

    public async Task InitializeAsync()
    {
        _factory = new ApiFactory();
        await _factory.ResetDatabaseAsync();
        _admins = await ApprovalAdminSession.CreateAsync(_factory);
    }

    public async Task DisposeAsync() => await _factory.DisposeAsync();

    private async Task<long> SeedAsync(string nsg, long? owner, bool personnel = true, MobileAppActivationStatus status = MobileAppActivationStatus.Pending)
    {
        string mobile = (9830000000L + ++_counter).ToString();
        await using AppDbContext ctx = _factory.CreateContext();
        if (personnel)
            ctx.UsersPis.Add(new UserPis { NsgNumber = nsg, ServiceNumber = "S-" + nsg, FullName = "Person " + nsg, MobileNumber = mobile, DateOfBirth = new DateOnly(1990, 5, 15), AssignedTo = owner });
        ApproveMobileUser request = new()
        {
            NsgNumber = nsg, MobileNumber = mobile, DateOfBirth = new DateOnly(1990, 5, 15), ApprovalStatus = status,
            ApprovedBy = status == MobileAppActivationStatus.Pending ? null : 1,
            ApprovedAtIst = status == MobileAppActivationStatus.Pending ? null : NSG.Mobile.Domain.Common.IndianStandardTime.Now
        };
        ctx.ApproveMobileUsers.Add(request);
        await ctx.SaveChangesAsync();
        return request.Id;
    }

    private async Task TransferAsync(string nsg, long? to)
    {
        await using AppDbContext ctx = _factory.CreateContext();
        await ctx.Database.ExecuteSqlRawAsync("UPDATE `users_pis` SET `assigned_to` = {0} WHERE `nsg_number` = {1}", new object[] { (object?)to ?? DBNull.Value, nsg });
    }

    private static StringContent Json(object body) => new(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

    private static async Task<(HttpStatusCode Status, JsonElement Body)> SendAsync(HttpClient client, HttpMethod method, string url, object? body = null)
    {
        using HttpRequestMessage request = new(method, url) { Content = body == null ? null : Json(body) };
        HttpResponseMessage response = await client.SendAsync(request);
        string text = await response.Content.ReadAsStringAsync();
        return (response.StatusCode, string.IsNullOrWhiteSpace(text) ? default : JsonDocument.Parse(text).RootElement.Clone());
    }

    private static Task<(HttpStatusCode Status, JsonElement Body)> GetAsync(HttpClient c, string url = Approvals) => SendAsync(c, HttpMethod.Get, url);

    private static Task<(HttpStatusCode Status, JsonElement Body)> DecideAsync(HttpClient c, long id, string decision = "Approved", object? body = null)
        => SendAsync(c, HttpMethod.Put, $"{Approvals}/{id}/decision", body ?? new { decision });

    private static string[] Nsgs(JsonElement body)
        => body.GetProperty("data").GetProperty("items").EnumerateArray().Select(i => i.GetProperty("nsgNumber").GetString()!).OrderBy(n => n).ToArray();

    private async Task<ApproveMobileUser> RowAsync(long id)
    {
        await using AppDbContext ctx = _factory.CreateContext();
        return await ctx.ApproveMobileUsers.AsNoTracking().SingleAsync(a => a.Id == id);
    }

    // ═══════════ Authorization through the real pipeline ═══════════

    [MySqlFact]
    public async Task Only_an_admin_portal_pis_admin_reaches_the_endpoints()
    {
        long id = await SeedAsync("OWN-1", _admins.PisAId);

        Assert.Equal(HttpStatusCode.OK, (await GetAsync(_admins.PisA)).Status);
        Assert.Equal(HttpStatusCode.OK, (await DecideAsync(_admins.PisA, id)).Status);

        long other = await SeedAsync("OWN-2", _admins.PisAId);
        foreach (HttpClient refused in new[] { _admins.Super, _admins.Registration })   // authenticated, but not PIS_ADMIN
        {
            Assert.Equal(HttpStatusCode.Forbidden, (await GetAsync(refused)).Status);
            Assert.Equal(HttpStatusCode.Forbidden, (await DecideAsync(refused, other)).Status);
        }

        HttpClient legacyStaff = _factory.CreateClientFor(DomainConstants.Roles.PisAdmin);   // staff Bearer token with the PisAdmin role
        Assert.Equal(HttpStatusCode.Unauthorized, (await GetAsync(legacyStaff)).Status);
        Assert.Equal(HttpStatusCode.Unauthorized, (await DecideAsync(legacyStaff, other)).Status);

        HttpClient anonymous = _factory.CreateClientFor(null);
        Assert.Equal(HttpStatusCode.Unauthorized, (await GetAsync(anonymous)).Status);
        Assert.Equal(HttpStatusCode.Unauthorized, (await DecideAsync(anonymous, other)).Status);

        Assert.Equal(MobileAppActivationStatus.Pending, (await RowAsync(other)).ApprovalStatus);   // every refused call changed nothing
    }

    [MySqlFact]
    public async Task The_reviewer_is_the_admin_profile_id_of_the_token_and_a_body_cannot_spoof_it()
    {
        long id = await SeedAsync("OWN-1", _admins.PisAId);

        (HttpStatusCode status, _) = await DecideAsync(_admins.PisA, id, body: new { decision = "Approved", approvedBy = _admins.PisBId, adminProfileId = _admins.PisBId, reviewerId = 999 });

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal((int)_admins.PisAId, (await RowAsync(id)).ApprovedBy);
        await using AppDbContext ctx = _factory.CreateContext();
        Assert.True(await ctx.AuditLogs.AnyAsync(a => a.Action == AuditAction.ITApprovalGranted && a.UserId == (int)_admins.PisAId));
    }

    // ═══════════ GET isolation ═══════════

    [MySqlFact]
    public async Task Each_admin_sees_only_their_own_requests_and_unassigned_or_orphaned_ones_are_hidden()
    {
        await SeedAsync("OWN-A1", _admins.PisAId);
        await SeedAsync("OWN-A2", _admins.PisAId);
        await SeedAsync("OWN-B1", _admins.PisBId);
        await SeedAsync("OWN-NONE", owner: null);
        await SeedAsync("OWN-GONE", _admins.PisAId, personnel: false);

        Assert.Equal(new[] { "OWN-A1", "OWN-A2" }, Nsgs((await GetAsync(_admins.PisA)).Body));
        Assert.Equal(new[] { "OWN-B1" }, Nsgs((await GetAsync(_admins.PisB)).Body));
    }

    [MySqlFact]
    public async Task Paging_total_count_search_and_status_filters_all_stay_inside_the_ownership_scope()
    {
        for (int i = 0; i < 5; i++) { await SeedAsync($"OWN-A{i}", _admins.PisAId); await SeedAsync($"OWN-B{i}", _admins.PisBId); }
        await SeedAsync("OWN-BDONE", _admins.PisBId, status: MobileAppActivationStatus.Approved);

        JsonElement page3 = (await GetAsync(_admins.PisA, $"{Approvals}?pageNumber=3&pageSize=2")).Body.GetProperty("data");
        Assert.Equal((5, 1), (page3.GetProperty("totalCount").GetInt32(), page3.GetProperty("items").GetArrayLength()));

        Assert.Equal(5, (await GetAsync(_admins.PisA, $"{Approvals}?search=OWN-")).Body.GetProperty("data").GetProperty("totalCount").GetInt32());
        JsonElement noMatch = (await GetAsync(_admins.PisA, $"{Approvals}?search=OWN-BDONE")).Body;
        Assert.True(noMatch.TryGetProperty("message", out _));   // another admin's NSG number finds nothing: a message, not a list
        Assert.True((await GetAsync(_admins.PisA, $"{Approvals}?status=Approved")).Body.TryGetProperty("message", out _));
    }

    // ═══════════ Decision ownership ═══════════

    [MySqlFact]
    public async Task The_owner_can_approve_and_reject_and_a_repeated_decision_is_still_a_conflict()
    {
        long approve = await SeedAsync("OWN-1", _admins.PisAId);
        long reject = await SeedAsync("OWN-2", _admins.PisAId);

        Assert.Equal(HttpStatusCode.OK, (await DecideAsync(_admins.PisA, approve, "Approved")).Status);
        Assert.Equal(HttpStatusCode.OK, (await DecideAsync(_admins.PisA, reject, "Rejected")).Status);
        Assert.Equal(HttpStatusCode.Conflict, (await DecideAsync(_admins.PisA, approve, "Rejected")).Status);

        await using AppDbContext ctx = _factory.CreateContext();
        Assert.Equal(MobileAppActivationStatus.Approved, (await ctx.UsersPis.AsNoTracking().SingleAsync(p => p.NsgNumber == "OWN-1")).MobileAppActivationStatus);
        Assert.Equal(MobileAppActivationStatus.Rejected, (await ctx.UsersPis.AsNoTracking().SingleAsync(p => p.NsgNumber == "OWN-2")).MobileAppActivationStatus);
    }

    [MySqlFact]
    public async Task Another_admin_and_an_unassigned_record_are_refused_with_403_and_nothing_changes()
    {
        long owned = await SeedAsync("OWN-1", _admins.PisAId);
        long unassigned = await SeedAsync("OWN-2", owner: null);

        foreach (string decision in new[] { "Approved", "Rejected" })
        {
            Assert.Equal(HttpStatusCode.Forbidden, (await DecideAsync(_admins.PisB, owned, decision)).Status);
            Assert.Equal(HttpStatusCode.Forbidden, (await DecideAsync(_admins.PisB, unassigned, decision)).Status);
            Assert.Equal(HttpStatusCode.Forbidden, (await DecideAsync(_admins.PisA, unassigned, decision)).Status);
        }

        foreach (long id in new[] { owned, unassigned })
        {
            ApproveMobileUser row = await RowAsync(id);
            Assert.Equal((MobileAppActivationStatus.Pending, (int?)null), (row.ApprovalStatus, row.ApprovedBy));
        }

        await using AppDbContext ctx = _factory.CreateContext();
        Assert.All(await ctx.UsersPis.AsNoTracking().Where(p => p.NsgNumber.StartsWith("OWN-")).ToListAsync(), p => Assert.Equal(MobileAppActivationStatus.Pending, p.MobileAppActivationStatus));
    }

    [MySqlFact]
    public async Task A_request_without_a_personnel_record_keeps_its_conflict_and_a_missing_request_its_404()
    {
        long orphan = await SeedAsync("OWN-GONE", _admins.PisAId, personnel: false);

        Assert.Equal(HttpStatusCode.Conflict, (await DecideAsync(_admins.PisA, orphan)).Status);
        Assert.Equal(HttpStatusCode.NotFound, (await DecideAsync(_admins.PisA, 987654)).Status);
        Assert.Equal(MobileAppActivationStatus.Pending, (await RowAsync(orphan)).ApprovalStatus);
    }

    // ═══════════ Ownership transfer ═══════════

    [MySqlFact]
    public async Task After_a_transfer_the_old_owner_loses_the_request_and_the_new_owner_gains_it_with_no_request_row_rewritten()
    {
        long id = await SeedAsync("OWN-1", _admins.PisAId);
        ApproveMobileUser before = await RowAsync(id);
        Assert.Equal(new[] { "OWN-1" }, Nsgs((await GetAsync(_admins.PisA)).Body));

        await TransferAsync("OWN-1", _admins.PisBId);

        Assert.True((await GetAsync(_admins.PisA)).Body.TryGetProperty("message", out _));   // A: nothing left to see
        Assert.Equal(HttpStatusCode.Forbidden, (await DecideAsync(_admins.PisA, id)).Status);
        Assert.Equal(new[] { "OWN-1" }, Nsgs((await GetAsync(_admins.PisB)).Body));
        ApproveMobileUser after = await RowAsync(id);
        Assert.Equal((before.ApprovalStatus, before.ApprovedBy, before.UpdatedAtIst, before.UpdatedBy), (after.ApprovalStatus, after.ApprovedBy, after.UpdatedAtIst, after.UpdatedBy));

        Assert.Equal(HttpStatusCode.OK, (await DecideAsync(_admins.PisB, id)).Status);
        Assert.Equal((int)_admins.PisBId, (await RowAsync(id)).ApprovedBy);
    }

    // ═══════════ Concurrency (transaction + row lock) ═══════════

    private ApproveMobileUserRepository Repository(AppDbContext ctx) => new(ctx);

    [MySqlFact]
    public async Task A_decision_waits_for_an_open_transfer_and_is_then_refused_with_nothing_written()
    {
        long id = await SeedAsync("OWN-1", _admins.PisAId);

        // The "transfer": a transaction that has rewritten assigned_to (A -> B) but not committed yet, so it holds the personnel row's lock.
        await using AppDbContext transferContext = _factory.CreateContext();
        await using var transfer = await transferContext.Database.BeginTransactionAsync();
        await transferContext.Database.ExecuteSqlRawAsync("UPDATE `users_pis` SET `assigned_to` = {0} WHERE `nsg_number` = 'OWN-1'", _admins.PisBId);

        await using AppDbContext decisionContext = _factory.CreateContext();
        Task<ApprovalDecisionResult> decision = Repository(decisionContext).DecideAsync(id, MobileAppActivationStatus.Approved, _admins.PisAId, null, DateTime.Now);

        await Task.Delay(1500);
        Assert.False(decision.IsCompleted, "the decision must wait for the transfer's row lock instead of reading a stale owner");

        await transfer.CommitAsync();
        ApprovalDecisionResult result = await decision.WaitAsync(TimeSpan.FromSeconds(20));

        Assert.Equal(ApprovalDecisionOutcome.NotOwner, result.Outcome);   // re-evaluated against the committed owner (B)
        ApproveMobileUser row = await RowAsync(id);
        Assert.Equal((MobileAppActivationStatus.Pending, (int?)null), (row.ApprovalStatus, row.ApprovedBy));
        await using AppDbContext check = _factory.CreateContext();
        Assert.Equal(MobileAppActivationStatus.Pending, (await check.UsersPis.AsNoTracking().SingleAsync(p => p.NsgNumber == "OWN-1")).MobileAppActivationStatus);
    }

    [MySqlFact]
    public async Task A_rolled_back_transfer_leaves_the_decision_valid()
    {
        long id = await SeedAsync("OWN-1", _admins.PisAId);

        await using AppDbContext transferContext = _factory.CreateContext();
        await using var transfer = await transferContext.Database.BeginTransactionAsync();
        await transferContext.Database.ExecuteSqlRawAsync("UPDATE `users_pis` SET `assigned_to` = {0} WHERE `nsg_number` = 'OWN-1'", _admins.PisBId);

        await using AppDbContext decisionContext = _factory.CreateContext();
        Task<ApprovalDecisionResult> decision = Repository(decisionContext).DecideAsync(id, MobileAppActivationStatus.Approved, _admins.PisAId, null, DateTime.Now);
        await Task.Delay(1000);
        Assert.False(decision.IsCompleted);

        await transfer.RollbackAsync();

        Assert.Equal(ApprovalDecisionOutcome.Decided, (await decision.WaitAsync(TimeSpan.FromSeconds(20))).Outcome);
        Assert.Equal((int)_admins.PisAId, (await RowAsync(id)).ApprovedBy);
    }

    [MySqlFact]
    public async Task Racing_transfers_and_decisions_never_let_the_previous_owner_decide_after_the_transfer_committed()
    {
        const int rounds = 12;
        int decided = 0, refused = 0;
        for (int i = 0; i < rounds; i++)
        {
            string nsg = $"OWN-R{i}";
            long id = await SeedAsync(nsg, _admins.PisAId);

            await using AppDbContext decisionContext = _factory.CreateContext();
            Task<ApprovalDecisionResult> decide = Task.Run(() => Repository(decisionContext).DecideAsync(id, MobileAppActivationStatus.Approved, _admins.PisAId, null, DateTime.Now));
            Task transfer = Task.Run(() => TransferAsync(nsg, _admins.PisBId));
            await Task.WhenAll(decide, transfer);

            ApprovalDecisionResult result = decide.Result;
            ApproveMobileUser row = await RowAsync(id);
            await using AppDbContext ctx = _factory.CreateContext();
            UserPis person = await ctx.UsersPis.AsNoTracking().SingleAsync(p => p.NsgNumber == nsg);

            Assert.Equal(_admins.PisBId, person.AssignedTo);   // the transfer always lands
            if (result.Outcome == ApprovalDecisionOutcome.Decided)
            {
                // The decision serialised BEFORE the transfer: one coherent decision by A, recorded once.
                decided++;
                Assert.Equal((MobileAppActivationStatus.Approved, (int?)_admins.PisAId), (row.ApprovalStatus, row.ApprovedBy));
                Assert.Equal(MobileAppActivationStatus.Approved, person.MobileAppActivationStatus);
            }
            else
            {
                // The transfer committed first: A decided nothing and no state moved.
                refused++;
                Assert.Equal(ApprovalDecisionOutcome.NotOwner, result.Outcome);
                Assert.Equal((MobileAppActivationStatus.Pending, (int?)null), (row.ApprovalStatus, row.ApprovedBy));
                Assert.Equal(MobileAppActivationStatus.Pending, person.MobileAppActivationStatus);
            }
        }

        Assert.Equal(rounds, decided + refused);   // every round resolved one way or the other, none half-applied
    }
}
